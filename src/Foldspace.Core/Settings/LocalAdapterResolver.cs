using System.Net;

namespace Foldspace.Core.Settings;

/// <summary>一張啟用中的 IPv4 網卡。</summary>
public sealed record AdapterInfo(string Id, string Name, IPAddress Address, bool HasGateway)
{
    public string Display => $"{Name} ({Address})";
}

/// <summary>
/// 決定要綁定哪一張網卡。設定同時記下網卡 ID 與當時的 IP：
/// <list type="number">
/// <item>網卡 ID 還在 → 用它目前的 IP（DHCP 換了 IP 也跟著走）。</item>
/// <item>網卡 ID 不見了，但有網卡用同一個 IP → 視為同一個網路（例如換成 virtio、USB 網卡或換主機板，Windows 會給新的 ID）。</item>
/// <item>兩者都找不到 → 沒有可綁定的 IP（拔線、網卡停用）。不會自動改用其他網路，避免在使用者不知情時換到別的網段。</item>
/// </list>
/// 設定是「自動」（沒有網卡 ID）時，選有預設閘道的網卡。
/// </summary>
public static class LocalAdapterResolver
{
    public static AdapterInfo? Resolve(IReadOnlyList<AdapterInfo> adapters, string? interfaceId, string? lastIp)
    {
        if (string.IsNullOrEmpty(interfaceId))
            return Default(adapters);

        return adapters.FirstOrDefault(a => a.Id == interfaceId)
            ?? (IPAddress.TryParse(lastIp, out var ip) ? adapters.FirstOrDefault(a => a.Address.Equals(ip)) : null);
    }

    public static AdapterInfo? Default(IReadOnlyList<AdapterInfo> adapters) =>
        adapters.FirstOrDefault(a => a.HasGateway) ?? adapters.FirstOrDefault();
}
