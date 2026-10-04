using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Foldspace.Core.Settings;

namespace Foldspace.App.Platform;

public static class NetworkAdapters
{
    /// <summary>所有啟用中的 IPv4 網卡（不含 loopback 與通道介面），有預設閘道的排前面。</summary>
    public static List<AdapterInfo> List()
    {
        var result = new List<AdapterInfo>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            IPInterfaceProperties props;
            try { props = ni.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }

            var hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            foreach (var unicast in props.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address))
                    result.Add(new AdapterInfo(ni.Id, ni.Name, unicast.Address, hasGateway));
            }
        }
        return result.OrderByDescending(a => a.HasGateway).ThenBy(a => a.Name).ToList();
    }

    /// <summary>依設定找出目前要綁定的網卡；規則見 <see cref="LocalAdapterResolver"/>。</summary>
    public static AdapterInfo? Resolve(AppSettings settings) =>
        LocalAdapterResolver.Resolve(List(), settings.LocalInterfaceId, settings.LocalIp);
}
