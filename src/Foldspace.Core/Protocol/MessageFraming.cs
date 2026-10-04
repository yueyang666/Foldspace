using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foldspace.Core.Protocol;

public sealed class ProtocolException(string message, Exception? inner = null) : Exception(message, inner);

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

/// <summary>控制通道的訊息框：4 bytes big-endian 長度 + UTF-8 JSON。</summary>
public static class MessageFraming
{
    public static async Task WriteAsync(Stream stream, ControlMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes<ControlMessage>(message, ProtocolJson.Options);
        if (json.Length > ProtocolConstants.MaxControlMessageBytes)
            throw new ProtocolException($"Control message too large ({json.Length} bytes)");

        var frame = new byte[4 + json.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, json.Length);
        json.CopyTo(frame, 4);
        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>讀取一則訊息；對方正常關閉連線時回傳 null。</summary>
    public static async Task<ControlMessage?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        var read = await stream.ReadAtLeastAsync(header, 4, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        if (read == 0)
            return null;
        if (read < 4)
            throw new EndOfStreamException("Incomplete control message header");

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > ProtocolConstants.MaxControlMessageBytes)
            throw new ProtocolException($"Invalid control message length ({length})");

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
        return Parse(body);
    }

    internal static ControlMessage Parse(byte[] body)
    {
        string? type;
        try
        {
            using var doc = JsonDocument.Parse(body);
            type = doc.RootElement.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
        }
        catch (JsonException ex)
        {
            throw new ProtocolException("Control message is not valid JSON", ex);
        }

        if (type is null)
            throw new ProtocolException("Control message has no type");
        if (!MessageTypes.Known.Contains(type))
            return new UnknownMessage(type);

        try
        {
            return JsonSerializer.Deserialize<ControlMessage>(body, ProtocolJson.Options)
                ?? throw new ProtocolException("Control message is null");
        }
        catch (JsonException ex)
        {
            throw new ProtocolException($"Malformed {type} message", ex);
        }
    }
}
