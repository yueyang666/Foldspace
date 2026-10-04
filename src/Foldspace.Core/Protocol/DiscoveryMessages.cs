using System.Text.Json;

namespace Foldspace.Core.Protocol;

/// <summary>
/// 同網段搜尋（UDP）：搜尋端廣播 <see cref="DiscoveryQuery"/>，每台開著 Foldspace 的電腦單播回 <see cref="DiscoveryReply"/>。
/// 內容只有公開資訊（電腦名稱、憑證指紋、版本、監聽 port），沒有任何秘密。
/// </summary>
public sealed record DiscoveryQuery
{
    public const string TypeName = "FOLDSPACE_DISCOVER";
    public string Type { get; init; } = TypeName;
    public required Guid Id { get; init; }
    public required string ProtocolVersion { get; init; }
    /// <summary>搜尋端的指紋：回應端用來說明「是不是已經跟你配對」，也讓自己忽略自己的廣播。</summary>
    public required string Fingerprint { get; init; }
}

public sealed record DiscoveryReply
{
    public const string TypeName = "FOLDSPACE_HERE";
    public string Type { get; init; } = TypeName;
    public required Guid ReplyTo { get; init; }
    public required string ProtocolVersion { get; init; }
    public required string AppVersion { get; init; }
    public required string Fingerprint { get; init; }
    public required string Hostname { get; init; }
    /// <summary>TCP 監聽 port。</summary>
    public required int Port { get; init; }
    /// <summary>已經和某台電腦配對（不一定是搜尋端）。</summary>
    public required bool Paired { get; init; }
    /// <summary>已經和搜尋端配對。</summary>
    public required bool PairedWithYou { get; init; }
}

public static class DiscoveryCodec
{
    public static byte[] Encode<T>(T message) => JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJson.Options);

    /// <summary>格式不對或類型不符時回傳 null（UDP 可能收到任何東西）。</summary>
    public static T? TryDecode<T>(ReadOnlySpan<byte> data, string typeName) where T : class
    {
        if (data.Length is 0 or > ProtocolConstants.MaxDiscoveryDatagramBytes)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(data.ToArray());
            if (!doc.RootElement.TryGetProperty("type", out var type) || type.GetString() != typeName)
                return null;
            return doc.RootElement.Deserialize<T>(ProtocolJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
