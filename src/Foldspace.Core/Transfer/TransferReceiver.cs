using System.Buffers;
using System.IO.Hashing;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using Serilog;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Transfer;

/// <summary>磁碟寫滿：整個任務失敗並清除暫存。</summary>
public sealed class DiskFullException(Exception inner) : IOException("The receiving disk is full", inner);

/// <summary>
/// 把資料通道的內容寫入 <c>&lt;接收資料夾&gt;\.foldspace-tmp\&lt;jobId&gt;\</c>，每個檔案收完就驗證雜湊。
/// 驗證通過的檔案才會在 <see cref="TransferFinalizer"/> 移到正式位置。
/// <para>
/// 讀網路與寫磁碟是分開的，網路不必等磁碟：
/// 小檔案在記憶體收齊、驗證後交給背景並行寫入（Windows 上每個檔案的建立與掃描有固定延遲，並行可以互相掩蓋）；
/// 大檔案由專屬的背景寫入工作依序寫，中間最多緩衝 <see cref="LargeFileQueueDepth"/> 個區塊。
/// </para>
/// </summary>
internal sealed class TransferReceiver
{
    private const int MaxRetransmit = 1000;
    private const int ParallelWrites = 8;
    private const int LargeFileQueueDepth = 8;

    private readonly TransferJob _job;
    private readonly string _tempDir;
    private readonly IReadOnlySet<string> _topLevelNames;
    private readonly ILogger _log;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _writeSlots = new(ParallelWrites);
    private readonly List<Task> _pendingWrites = [];
    private Exception? _writeError;

    public TransferReceiver(TransferJob job, TransferOfferMessage offer, string tempDir, ILogger log)
    {
        _job = job;
        _tempDir = tempDir;
        _log = log;
        _topLevelNames = offer.Items.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>雜湊驗證通過且已寫入暫存區的檔案（相對路徑）。</summary>
    public HashSet<string> VerifiedFiles { get; } = new(StringComparer.Ordinal);

    /// <summary>收到的資料夾與其修改時間（最後移到正式位置後再套用）。</summary>
    public Dictionary<string, DateTime> Directories { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, FileIssue> FailedFiles { get; } = new(StringComparer.Ordinal);

    public long ReceivedBytes { get; private set; }

    /// <summary>目前接收中的檔案。</summary>
    private sealed class Incoming
    {
        public required string Path { get; init; }
        /// <summary>暫存區中的完整路徑；null 表示丟棄內容（名稱在 Windows 上不合法）。</summary>
        public required string? Target { get; init; }
        public required long Expected { get; init; }
        public required DateTime LastWriteUtc { get; init; }
        public XxHash3 Hasher { get; } = new();
        public long Received { get; set; }
        /// <summary>小檔案：整個收進這個緩衝區。</summary>
        public byte[]? Buffer { get; set; }
        /// <summary>大檔案：交給背景寫入。</summary>
        public LargeFileWriter? Writer { get; set; }
    }

    public async Task ReceiveAsync(Stream dataStream, CancellationToken ct)
    {
        var reader = new DataStreamReader(dataStream);
        var scratch = new byte[ProtocolConstants.TransferBufferSize];
        var round = 1;
        var retransmit = new List<string>();
        Incoming? current = null;

        try
        {
            while (true)
            {
                ThrowIfWriteFailed();
                // 網路緩衝區還有資料時，讀取會直接完成而不檢查取消，所以每筆紀錄都要自己檢查。
                ct.ThrowIfCancellationRequested();
                var record = await reader.ReadRecordAsync(ct);
                switch (record.Kind)
                {
                    case RecordKind.EntryHeader:
                    {
                        if (current is not null)
                            throw new ProtocolException("Received a new header before the previous file ended");

                        var header = await reader.ReadHeaderPayloadAsync(record.Length, ct);
                        var segments = PathSafety.SplitRelative(header.Path, _topLevelNames);
                        var target = PathSafety.Combine(_tempDir, segments);
                        if (header.Size < 0)
                            throw new ProtocolException("Invalid file size");

                        var unsupported = segments.Any(s => PathSafety.CheckName(s) != NameCheck.Ok);
                        var lastWrite = new DateTime(header.LastWriteUtcTicks, DateTimeKind.Utc);
                        if (header.IsDirectory)
                        {
                            if (unsupported)
                            {
                                AddFailure(header.Path, FileIssue.InvalidName);
                                continue;
                            }
                            // 資料夾一定在它的內容之前送達，所以寫檔時不必再檢查上層資料夾。
                            Directory.CreateDirectory(target);
                            Directories[header.Path] = lastWrite;
                            continue;
                        }

                        current = new Incoming
                        {
                            Path = header.Path,
                            Target = unsupported ? null : target,
                            Expected = header.Size,
                            LastWriteUtc = lastWrite,
                        };
                        if (unsupported)
                            AddFailure(header.Path, FileIssue.InvalidName);
                        else if (header.Size <= TransferSender.SmallFileLimit)
                            current.Buffer = ArrayPool<byte>.Shared.Rent((int)Math.Max(1, header.Size));
                        else
                            current.Writer = LargeFileWriter.Open(target, header.Size);
                        continue;
                    }

                    case RecordKind.Chunk:
                    {
                        if (current is null)
                            throw new ProtocolException("Received data outside of a file");
                        if (current.Received + record.Length > current.Expected)
                            throw new ProtocolException($"Data for {current.Path} exceeds its declared size");

                        if (current.Buffer is { } buffer)
                        {
                            var slice = buffer.AsMemory((int)current.Received, record.Length);
                            await reader.ReadPayloadAsync(slice, ct);
                            current.Hasher.Append(slice.Span);
                        }
                        else if (current.Writer is { } writer)
                        {
                            var chunk = ArrayPool<byte>.Shared.Rent(record.Length);
                            await reader.ReadPayloadAsync(chunk.AsMemory(0, record.Length), ct);
                            current.Hasher.Append(chunk.AsSpan(0, record.Length));
                            await writer.EnqueueAsync(chunk, record.Length, ct); // 交出緩衝區
                        }
                        else
                        {
                            await reader.ReadPayloadAsync(scratch.AsMemory(0, record.Length), ct);
                            current.Hasher.Append(scratch.AsSpan(0, record.Length));
                        }

                        current.Received += record.Length;
                        ReceivedBytes += record.Length;
                        _job.AddBytes(record.Length);
                        continue;
                    }

                    case RecordKind.FileEnd:
                    {
                        if (current is null)
                            throw new ProtocolException("Received a file end outside of a file");

                        var file = current;
                        current = null;
                        var ok = file.Received == file.Expected && file.Hasher.GetCurrentHashAsUInt64() == record.Hash;

                        if (file.Target is null)
                            continue; // 不支援的名稱，已記錄為失敗

                        if (ok)
                        {
                            await ScheduleWriteAsync(file, ct);
                            continue;
                        }

                        await DiscardAsync(file);
                        if (round == 1 && retransmit.Count < MaxRetransmit)
                            retransmit.Add(file.Path);
                        else
                            AddFailure(file.Path, FileIssue.HashMismatch);
                        _log.Warning("Job {JobId}: hash mismatch for {Path} (pass {Round})", _job.JobId, file.Path, round);
                        continue;
                    }

                    case RecordKind.FileAbort:
                    {
                        var reason = await reader.ReadStringPayloadAsync(record.Length, ct);
                        if (current is null)
                            throw new ProtocolException("Received a file abort outside of a file");

                        // 未收到的部分也算進進度，讓進度條能走到底。
                        _job.AddBytes(current.Expected - current.Received);
                        await DiscardAsync(current);
                        AddFailure(current.Path, Enum.TryParse<FileIssue>(reason, out var issue) ? issue : FileIssue.ReadFailed);
                        current = null;
                        continue;
                    }

                    case RecordKind.EndOfPass:
                    {
                        if (current is not null)
                            throw new ProtocolException("Received end of pass before the file ended");

                        // 這一輪的檔案都寫完才回覆，寫入錯誤（例如磁碟已滿）才能在這裡被發現。
                        await WaitForWritesAsync();
                        ThrowIfWriteFailed();

                        List<string> request = round == 1 ? [.. retransmit] : [];
                        await PassResponseFraming.WriteAsync(dataStream, new PassResponse { Retransmit = request }, ct);
                        if (request.Count == 0)
                            return;
                        round++;
                        retransmit.Clear();
                        continue;
                    }
                }
            }
        }
        finally
        {
            // 中斷時：未完成的檔案關閉並刪除（一定要在這裡完成，之後才會整理暫存區）；
            // 已驗證、正在寫的檔案讓它寫完（會被保留）。
            if (current is not null)
                await DiscardAsync(current);
            await WaitForWritesAsync();
        }
    }

    /// <summary>驗證通過的檔案：小檔案交給並行寫入，大檔案通知背景寫入工作收尾。</summary>
    private async Task ScheduleWriteAsync(Incoming file, CancellationToken ct)
    {
        Task write;
        if (file.Writer is { } writer)
        {
            write = FinishLargeAsync(file, writer);
        }
        else
        {
            // 同時最多 ParallelWrites 個小檔案在寫；都在忙時讀網路的一方等待，記憶體用量因此有上限。
            await _writeSlots.WaitAsync(ct);
            write = Task.Run(() => WriteSmall(file));
        }
        lock (_gate)
            _pendingWrites.Add(write);
    }

    private void WriteSmall(Incoming file)
    {
        try
        {
            using (var handle = File.OpenHandle(file.Target!, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (file.Expected > 0)
                    RandomAccess.Write(handle, file.Buffer.AsSpan(0, (int)file.Expected), 0);
                // 透過已開啟的 handle 設定時間，不用為了這件事再開一次檔案。
                File.SetLastWriteTimeUtc(handle, file.LastWriteUtc);
            }
            MarkVerified(file.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            OnWriteError(file, ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(file.Buffer!);
            file.Buffer = null;
            _writeSlots.Release();
        }
    }

    private async Task FinishLargeAsync(Incoming file, LargeFileWriter writer)
    {
        try
        {
            await writer.CompleteAsync(file.LastWriteUtc);
            MarkVerified(file.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            OnWriteError(file, ex);
        }
    }

    private void OnWriteError(Incoming file, Exception ex)
    {
        TryDelete(file.Target!);
        if (IsDiskFull(ex))
        {
            lock (_gate)
                _writeError ??= ex as DiskFullException ?? new DiskFullException(ex);
            return;
        }
        _log.Warning("Job {JobId}: could not write {Path}: {Error}", _job.JobId, file.Path, ex.Message);
        AddFailure(file.Path, FileIssue.WriteFailed);
    }

    /// <summary>丟棄未完成或驗證失敗的檔案：等背景寫入停止、檔案關閉並刪除後才返回。</summary>
    private static async Task DiscardAsync(Incoming file)
    {
        if (file.Buffer is { } buffer)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            file.Buffer = null;
        }
        if (file.Writer is { } writer)
        {
            file.Writer = null;
            await writer.AbortAsync();
        }
    }

    private void MarkVerified(string path)
    {
        lock (_gate)
        {
            VerifiedFiles.Add(path);
            FailedFiles.Remove(path);
        }
    }

    private void AddFailure(string path, FileIssue reason)
    {
        lock (_gate)
            FailedFiles[path] = reason;
    }

    private async Task WaitForWritesAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            pending = [.. _pendingWrites];
            _pendingWrites.Clear();
        }
        await Task.WhenAll(pending); // 每個寫入工作自己處理錯誤，不會丟例外
    }

    private void ThrowIfWriteFailed()
    {
        Exception? error;
        lock (_gate)
            error = _writeError;
        if (error is not null)
            ExceptionDispatchInfo.Throw(error);
    }

    /// <summary>ERROR_DISK_FULL (112)、ERROR_HANDLE_DISK_FULL (39)；非 Windows 為 ENOSPC。</summary>
    internal static bool IsDiskFull(Exception ex) =>
        ex is DiskFullException ||
        (ex is IOException io && ((io.HResult & 0xFFFF) is 112 or 39 || (!OperatingSystem.IsWindows() && io.HResult == 28)));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* 暫存資料夾最後會整個刪掉 */ }
    }

    /// <summary>
    /// 大檔案的背景寫入：讀網路的一方把區塊放進有上限的佇列，這裡依序寫到磁碟。
    /// 寫入失敗後佇列會關閉，下一次 <see cref="EnqueueAsync"/> 就會丟出錯誤。
    /// </summary>
    internal sealed class LargeFileWriter
    {
        private readonly string _path;
        private readonly SafeFileHandle _handle;
        private readonly Channel<(byte[] Buffer, int Length)> _chunks =
            Channel.CreateBounded<(byte[], int)>(new BoundedChannelOptions(LargeFileQueueDepth) { SingleReader = true, SingleWriter = true });
        private readonly Task _loop;
        private Exception? _error;
        private volatile bool _aborted;
        private int _closed;

        private LargeFileWriter(string path, SafeFileHandle handle)
        {
            _path = path;
            _handle = handle;
            _loop = Task.Run(WriteLoopAsync);
        }

        public static LargeFileWriter Open(string path, long size)
        {
            try
            {
                // 先保留空間：磁碟不夠時在這裡就會失敗，不必寫到一半才發現。
                var handle = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None,
                    FileOptions.Asynchronous | FileOptions.SequentialScan, preallocationSize: size);
                return new LargeFileWriter(path, handle);
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
                throw new DiskFullException(ex);
            }
        }

        public async Task EnqueueAsync(byte[] buffer, int length, CancellationToken ct)
        {
            try
            {
                if (await _chunks.Writer.WaitToWriteAsync(ct) && _chunks.Writer.TryWrite((buffer, length)))
                    return;
            }
            catch (ChannelClosedException)
            {
            }
            ArrayPool<byte>.Shared.Return(buffer);
            throw ToReported(_error) ?? new IOException("Writing has stopped");
        }

        /// <summary>等所有區塊寫完、設定修改時間並關閉。寫入失敗時丟出該錯誤。</summary>
        public async Task CompleteAsync(DateTime lastWriteUtc)
        {
            _chunks.Writer.TryComplete();
            await _loop;
            try
            {
                if (_error is not null)
                    ExceptionDispatchInfo.Throw(ToReported(_error)!);
                File.SetLastWriteTimeUtc(_handle, lastWriteUtc);
            }
            finally
            {
                Close();
            }
        }

        /// <summary>放棄這個檔案：佇列中剩下的區塊不再寫入，等寫入工作結束後關閉並刪除。</summary>
        public async Task AbortAsync()
        {
            _aborted = true;
            _chunks.Writer.TryComplete();
            try { await _loop; } catch { /* 寫入錯誤在放棄時不重要 */ }
            Close();
            TryDelete(_path);
        }

        private async Task WriteLoopAsync()
        {
            long offset = 0;
            try
            {
                await foreach (var (buffer, length) in _chunks.Reader.ReadAllAsync())
                {
                    try
                    {
                        if (_error is null && !_aborted)
                        {
                            await RandomAccess.WriteAsync(_handle, buffer.AsMemory(0, length), offset);
                            offset += length;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _error = ex;
                        _chunks.Writer.TryComplete(); // 讓讀網路的一方停下來
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
            catch (ChannelClosedException)
            {
            }
        }

        private static Exception? ToReported(Exception? ex) =>
            ex is null ? null : IsDiskFull(ex) ? ex as DiskFullException ?? new DiskFullException(ex) : ex;

        private void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0)
                _handle.Dispose();
        }
    }
}
