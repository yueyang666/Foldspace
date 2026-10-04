using System.Buffers;
using System.IO.Hashing;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using Serilog;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Transfer;

/// <summary>
/// 在已經建立好的資料通道上依序送出所有項目。
/// <para>
/// 讀檔與送出是重疊進行的，網路不必等磁碟：
/// 小檔案由背景工作並行預讀（最多 <see cref="ParallelReads"/> 個同時讀、預先準備 <see cref="PrefetchDepth"/> 個項目），
/// 大檔案用兩個緩衝區輪流，送出這一塊的同時讀下一塊。
/// </para>
/// </summary>
internal sealed class TransferSender(TransferJob job, ScanResult scan, ILogger log)
{
    /// <summary>不超過這個大小的檔案整個讀進記憶體，可以並行讀取。</summary>
    internal const int SmallFileLimit = 256 * 1024;
    private const int PrefetchDepth = 64;
    private const int ParallelReads = 8;


    public async Task SendAsync(Stream dataStream, CancellationToken ct)
    {
        // 小檔案會產生大量小紀錄，先緩衝起來再交給 TLS / socket；1 MB 的區塊會直接穿過緩衝。
        await using var buffered = new BufferedStream(dataStream, 256 * 1024);
        var writer = new DataStreamWriter(buffered);

        IReadOnlyList<ScanEntry> pass = scan.Entries;
        for (var round = 1; ; round++)
        {
            await SendPassAsync(writer, pass, ct);
            await writer.WriteEndOfPassAsync(ct);
            var response = await PassResponseFraming.ReadAsync(dataStream, ct);
            if (response.Retransmit.Count == 0 || round >= 2)
                return;

            log.Warning("Job {JobId}: {Count} file(s) failed the hash check, retransmitting once", job.JobId, response.Retransmit.Count);
            var wanted = response.Retransmit.ToHashSet(StringComparer.Ordinal);
            pass = scan.Entries.Where(e => !e.IsDirectory && wanted.Contains(e.RelativePath)).ToList();
        }
    }

    /// <summary>預讀好的項目。小檔案帶著內容與雜湊；大檔案與資料夾只帶項目本身。</summary>
    private sealed record Prepared(ScanEntry Entry, byte[]? Data = null, int Length = 0, ulong Hash = 0, FileIssue? AbortReason = null)
    {
        public void Release()
        {
            if (Data is not null)
                ArrayPool<byte>.Shared.Return(Data);
        }
    }

    private async Task SendPassAsync(DataStreamWriter writer, IReadOnlyList<ScanEntry> entries, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var readSlots = new SemaphoreSlim(ParallelReads);
        var queue = Channel.CreateBounded<Task<Prepared>>(new BoundedChannelOptions(PrefetchDepth)
        {
            SingleReader = true,
            SingleWriter = true,
        });

        // 依原本的順序放入佇列；小檔案的讀取在背景並行進行。
        var producer = Task.Run(async () =>
        {
            try
            {
                foreach (var entry in entries)
                {
                    var prepared = entry.IsDirectory || entry.Size > SmallFileLimit
                        ? Task.FromResult(new Prepared(entry))
                        : PrefetchSmallAsync(entry, readSlots, cts.Token);
                    await queue.Writer.WriteAsync(prepared, cts.Token);
                }
                queue.Writer.Complete();
            }
            catch (Exception ex)
            {
                queue.Writer.TryComplete(ex);
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var task in queue.Reader.ReadAllAsync(cts.Token))
            {
                var prepared = await task;
                try
                {
                    await SendPreparedAsync(writer, prepared, cts.Token);
                }
                finally
                {
                    prepared.Release();
                }
            }
        }
        finally
        {
            cts.Cancel();
            try { await producer; } catch { /* 取消時的例外已由主流程處理 */ }
            while (queue.Reader.TryRead(out var leftover))
            {
                try { (await leftover).Release(); } catch { /* 已取消的預讀 */ }
            }
        }
    }

    private static async Task<Prepared> PrefetchSmallAsync(ScanEntry entry, SemaphoreSlim slots, CancellationToken ct)
    {
        await slots.WaitAsync(ct);
        try
        {
            return await Task.Run(() => ReadSmall(entry), ct);
        }
        finally
        {
            slots.Release();
        }
    }

    private static Prepared ReadSmall(ScanEntry entry)
    {
        byte[]? buffer = null;
        try
        {
            // 被其他程式開著的檔案也盡量讀得到。
            using var handle = File.OpenHandle(entry.SourcePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
            var length = RandomAccess.GetLength(handle);
            if (length != entry.Size)
                return new Prepared(entry, AbortReason: FileIssue.SourceModified);

            buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, (int)length));
            var total = 0;
            while (total < length)
            {
                var read = RandomAccess.Read(handle, buffer.AsSpan(total, (int)length - total), total);
                if (read == 0)
                    break;
                total += read;
            }
            if (total != length)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                return new Prepared(entry, AbortReason: FileIssue.SourceModified);
            }

            return new Prepared(entry, buffer, total, XxHash3.HashToUInt64(buffer.AsSpan(0, total)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (buffer is not null)
                ArrayPool<byte>.Shared.Return(buffer);
            return new Prepared(entry, AbortReason: OpenFailureReason(ex));
        }
    }

    private async Task SendPreparedAsync(DataStreamWriter writer, Prepared prepared, CancellationToken ct)
    {
        var entry = prepared.Entry;
        await writer.WriteHeaderAsync(new EntryHeader
        {
            Path = entry.RelativePath,
            IsDirectory = entry.IsDirectory,
            Size = entry.Size,
            LastWriteUtcTicks = entry.LastWriteUtc.Ticks,
        }, ct);

        if (entry.IsDirectory)
            return;

        if (prepared.AbortReason is { } reason)
        {
            log.Information("Job {JobId}: skipping {Path}: {Reason}", job.JobId, entry.RelativePath, reason);
            await writer.WriteFileAbortAsync(reason, ct);
            job.AddBytes(entry.Size);
            return;
        }

        if (prepared.Data is { } data)
        {
            if (prepared.Length > 0)
                await writer.WriteChunkAsync(data.AsMemory(0, prepared.Length), ct);
            await writer.WriteFileEndAsync(prepared.Hash, ct);
            job.AddBytes(prepared.Length);
            return;
        }

        await SendLargeAsync(writer, entry, ct);
    }

    /// <summary>大檔案：兩個緩衝區輪流，送出目前這塊時已經在讀下一塊。</summary>
    private async Task SendLargeAsync(DataStreamWriter writer, ScanEntry entry, CancellationToken ct)
    {
        SafeFileHandle handle;
        try
        {
            handle = File.OpenHandle(entry.SourcePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Information("Job {JobId}: skipping {Path}: {Error}", job.JobId, entry.RelativePath, ex.Message);
            await writer.WriteFileAbortAsync(OpenFailureReason(ex), ct);
            job.AddBytes(entry.Size);
            return;
        }

        var current = ArrayPool<byte>.Shared.Rent(ProtocolConstants.TransferBufferSize);
        var next = ArrayPool<byte>.Shared.Rent(ProtocolConstants.TransferBufferSize);
        Task<int>? pending = null;
        try
        {
            var hasher = new XxHash3();
            long offset = 0;
            FileIssue? abortReason = null;
            pending = ReadChunkAsync(handle, current, 0, ct);

            while (true)
            {
                int read;
                try
                {
                    read = await pending;
                }
                catch (IOException ex)
                {
                    log.Warning(ex, "Job {JobId}: failed to read {Path}", job.JobId, entry.RelativePath);
                    abortReason = FileIssue.ReadFailed;
                    pending = null;
                    break;
                }
                pending = null;

                if (read == 0)
                    break;
                if (offset + read > entry.Size)
                {
                    abortReason = FileIssue.SourceModified;
                    break;
                }

                offset += read;
                pending = ReadChunkAsync(handle, next, offset, ct);
                hasher.Append(current.AsSpan(0, read));
                await writer.WriteChunkAsync(current.AsMemory(0, read), ct);
                job.AddBytes(read);
                (current, next) = (next, current);
            }

            if (abortReason is null && offset != entry.Size)
                abortReason = FileIssue.SourceModified;

            if (abortReason is null)
            {
                await writer.WriteFileEndAsync(hasher.GetCurrentHashAsUInt64(), ct);
            }
            else
            {
                await writer.WriteFileAbortAsync(abortReason.Value, ct);
                job.AddBytes(entry.Size - offset);
            }
        }
        finally
        {
            // 送出失敗時，背景讀取可能還在用緩衝區，等它結束再歸還。
            if (pending is not null)
            {
                try { await pending; } catch { /* 已經在處理其他錯誤 */ }
            }
            handle.Dispose();
            ArrayPool<byte>.Shared.Return(current);
            ArrayPool<byte>.Shared.Return(next);
        }
    }

    private static Task<int> ReadChunkAsync(SafeFileHandle handle, byte[] buffer, long offset, CancellationToken ct) =>
        RandomAccess.ReadAsync(handle, buffer.AsMemory(0, ProtocolConstants.TransferBufferSize), offset, ct).AsTask();

    private static FileIssue OpenFailureReason(Exception ex) =>
        ex is UnauthorizedAccessException ? FileIssue.NoReadPermission : FileIssue.Locked;
}
