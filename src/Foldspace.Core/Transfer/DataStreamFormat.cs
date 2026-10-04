using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Transfer;

/// <summary>
/// 資料通道的串流格式。每筆紀錄 = 1 byte 種類 + 內容：
/// <list type="bullet">
/// <item><c>0x10</c> 項目標頭：4 bytes 長度 + JSON（<see cref="EntryHeader"/>）</item>
/// <item><c>0x20</c> 檔案區塊：4 bytes 長度 + 內容（≤ 1 MB）</item>
/// <item><c>0x21</c> 檔案結束：8 bytes XxHash3</item>
/// <item><c>0x22</c> 檔案放棄：4 bytes 長度 + UTF-8 的 <see cref="FileIssue"/> 名稱（來源被修改、讀取失敗…）</item>
/// <item><c>0x30</c> 一輪結束：接收端回 4 bytes 長度 + JSON（<see cref="PassResponse"/>）</item>
/// </list>
/// 檔案內容分段送，傳送端才能在中途發現來源被修改時放棄該檔，而不破壞後面的串流。
/// </summary>
public enum RecordKind : byte
{
    EntryHeader = 0x10,
    Chunk = 0x20,
    FileEnd = 0x21,
    FileAbort = 0x22,
    EndOfPass = 0x30,
}

public sealed record EntryHeader
{
    public required string Path { get; init; }
    public required bool IsDirectory { get; init; }
    public required long Size { get; init; }
    public required long LastWriteUtcTicks { get; init; }
}

/// <summary>每輪結束時接收端的回覆：需要重傳的檔案（雜湊不符）。</summary>
public sealed record PassResponse
{
    public required IReadOnlyList<string> Retransmit { get; init; }
}

internal static class DataStreamFormat
{
    public const int MaxHeaderBytes = 64 * 1024;
}

internal sealed class DataStreamWriter(Stream stream)
{
    private readonly byte[] _prefix = new byte[9];

    public async Task WriteHeaderAsync(EntryHeader header, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(header, ProtocolJson.Options);
        await WriteFramedAsync(RecordKind.EntryHeader, json, ct);
    }

    public Task WriteChunkAsync(ReadOnlyMemory<byte> data, CancellationToken ct) =>
        WriteFramedAsync(RecordKind.Chunk, data, ct);

    public async Task WriteFileEndAsync(ulong hash, CancellationToken ct)
    {
        _prefix[0] = (byte)RecordKind.FileEnd;
        BinaryPrimitives.WriteUInt64BigEndian(_prefix.AsSpan(1), hash);
        await stream.WriteAsync(_prefix.AsMemory(0, 9), ct);
    }

    /// <summary>放棄這個檔案；內容是 <see cref="FileIssue"/> 的名稱（不傳顯示用的文字）。</summary>
    public Task WriteFileAbortAsync(FileIssue reason, CancellationToken ct) =>
        WriteFramedAsync(RecordKind.FileAbort, Encoding.UTF8.GetBytes(reason.ToString()), ct);

    public async Task WriteEndOfPassAsync(CancellationToken ct)
    {
        _prefix[0] = (byte)RecordKind.EndOfPass;
        await stream.WriteAsync(_prefix.AsMemory(0, 1), ct);
        await stream.FlushAsync(ct);
    }

    private async Task WriteFramedAsync(RecordKind kind, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        _prefix[0] = (byte)kind;
        BinaryPrimitives.WriteInt32BigEndian(_prefix.AsSpan(1), payload.Length);
        await stream.WriteAsync(_prefix.AsMemory(0, 5), ct);
        await stream.WriteAsync(payload, ct);
    }
}

internal readonly record struct DataRecord(RecordKind Kind, int Length, ulong Hash = 0);

internal sealed class DataStreamReader(Stream stream)
{
    private readonly byte[] _prefix = new byte[8];

    /// <summary>讀取下一筆紀錄的種類與長度；內容由呼叫端用 <see cref="ReadPayloadAsync"/> 讀取。</summary>
    public async Task<DataRecord> ReadRecordAsync(CancellationToken ct)
    {
        await stream.ReadExactlyAsync(_prefix.AsMemory(0, 1), ct);
        var kind = (RecordKind)_prefix[0];
        switch (kind)
        {
            case RecordKind.EntryHeader:
            case RecordKind.Chunk:
            case RecordKind.FileAbort:
                await stream.ReadExactlyAsync(_prefix.AsMemory(0, 4), ct);
                var length = BinaryPrimitives.ReadInt32BigEndian(_prefix);
                var max = kind == RecordKind.Chunk ? ProtocolConstants.TransferBufferSize : DataStreamFormat.MaxHeaderBytes;
                if (length < 0 || length > max)
                    throw new ProtocolException($"Invalid data record length ({kind} {length})");
                return new DataRecord(kind, length);
            case RecordKind.FileEnd:
                await stream.ReadExactlyAsync(_prefix.AsMemory(0, 8), ct);
                return new DataRecord(kind, 0, BinaryPrimitives.ReadUInt64BigEndian(_prefix));
            case RecordKind.EndOfPass:
                return new DataRecord(kind, 0);
            default:
                throw new ProtocolException($"Unknown data record kind 0x{(byte)kind:X2}");
        }
    }

    public async Task ReadPayloadAsync(Memory<byte> buffer, CancellationToken ct) =>
        await stream.ReadExactlyAsync(buffer, ct);

    public async Task<EntryHeader> ReadHeaderPayloadAsync(int length, CancellationToken ct)
    {
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, ct);
        try
        {
            return JsonSerializer.Deserialize<EntryHeader>(buffer, ProtocolJson.Options)
                ?? throw new ProtocolException("Entry header is null");
        }
        catch (JsonException ex)
        {
            throw new ProtocolException("Malformed entry header", ex);
        }
    }

    public async Task<string> ReadStringPayloadAsync(int length, CancellationToken ct)
    {
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, ct);
        return Encoding.UTF8.GetString(buffer);
    }
}

/// <summary>每輪結束時的回覆框：4 bytes 長度 + JSON。</summary>
internal static class PassResponseFraming
{
    public static async Task WriteAsync(Stream stream, PassResponse response, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response, ProtocolJson.Options);
        var frame = new byte[4 + json.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, json.Length);
        json.CopyTo(frame, 4);
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<PassResponse> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > ProtocolConstants.MaxControlMessageBytes)
            throw new ProtocolException($"Invalid pass response length ({length})");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        return JsonSerializer.Deserialize<PassResponse>(body, ProtocolJson.Options)
            ?? throw new ProtocolException("Pass response is null");
    }
}
