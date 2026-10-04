using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Net;

/// <summary>
/// 同網段搜尋：每台 Foldspace 在固定的 UDP port 回應搜尋廣播，回報電腦名稱、指紋、版本與 TCP 監聽 port。
/// 已配對但連不上對方時定期重新搜尋，對方換了 IP（例如 DHCP）就自動改連新位址。
/// 搜尋結果沒有經過驗證，但真正連線時仍以 TLS 指紋確認對方身分，假冒的回應最多讓連線暫時失敗。
/// </summary>
public sealed partial class PeerService
{
    /// <summary>Windows：關掉 UDP 收到 ICMP「port 無法連線」時讓下一次讀取丟出例外的行為。</summary>
    private const int SioUdpConnReset = -1744830452;

    /// <summary>搜尋同網段開著 Foldspace 的電腦（不含本機）。</summary>
    public async Task<IReadOnlyList<DiscoveredPeer>> DiscoverAsync(TimeSpan? duration = null, CancellationToken ct = default)
    {
        var local = _options.ResolveLocalAddress();
        var targets = _options.DiscoveryTargets?.Invoke() ?? BroadcastTargets(local);
        using var socket = new UdpClient(new IPEndPoint(local ?? IPAddress.Any, 0)) { EnableBroadcast = true };
        IgnoreConnectionReset(socket);

        var query = new DiscoveryQuery
        {
            Id = Guid.NewGuid(),
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
            Fingerprint = LocalFingerprint,
        };
        var payload = DiscoveryCodec.Encode(query);
        var found = new Dictionary<string, DiscoveredPeer>(StringComparer.Ordinal);

        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(duration ?? ProtocolConstants.DiscoveryDuration);

        // UDP 可能掉封包：開始、0.3 秒、0.8 秒各送一次。
        var sending = Task.Run(async () =>
        {
            foreach (var delay in new[] { 0, 300, 500 })
            {
                await Task.Delay(delay, window.Token).ConfigureAwait(false);
                foreach (var target in targets)
                {
                    try
                    {
                        await socket.SendAsync(payload, target, window.Token).ConfigureAwait(false);
                    }
                    catch (SocketException ex)
                    {
                        _log.Debug("Discovery query to {Target} failed: {Error}", target, NetworkError.Describe(ex));
                    }
                }
            }
        }, CancellationToken.None);

        try
        {
            while (true)
            {
                var received = await socket.ReceiveAsync(window.Token).ConfigureAwait(false);
                var reply = DiscoveryCodec.TryDecode<DiscoveryReply>(received.Buffer, DiscoveryReply.TypeName);
                if (reply is null || reply.ReplyTo != query.Id || reply.Fingerprint == LocalFingerprint ||
                    reply.Port is < ProtocolConstants.MinPort or > ProtocolConstants.MaxPort)
                    continue;
                found.TryAdd(reply.Fingerprint, new DiscoveredPeer(
                    CleanHostname(reply.Hostname), received.RemoteEndPoint.Address, reply.Port, reply.Fingerprint,
                    reply.AppVersion, reply.ProtocolVersion, reply.Paired, reply.PairedWithYou));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 搜尋時間到
        }
        finally
        {
            try { await sending.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _log.Information("Discovery found {Count} device(s)", found.Count);
        return [.. found.Values.OrderBy(p => p.Hostname, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>回應別台電腦的搜尋。綁定所有網卡的固定 UDP port；port 被占用時每 30 秒重試。</summary>
    private async Task DiscoveryResponderLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpClient socket;
            try
            {
                socket = new UdpClient(AddressFamily.InterNetwork);
                // 同一台電腦上的其他 Foldspace（開發用的 --profile）也要收得到廣播。
                socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, _options.DiscoveryPort));
                IgnoreConnectionReset(socket);
            }
            catch (SocketException ex)
            {
                _log.Warning("Cannot answer discovery on UDP port {Port}: {Error}", _options.DiscoveryPort, NetworkError.Describe(ex));
                await WaitForNudgeAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                continue;
            }

            using (socket)
            {
                var windowStart = Environment.TickCount64;
                var answered = 0;
                while (!ct.IsCancellationRequested)
                {
                    UdpReceiveResult received;
                    try
                    {
                        received = await socket.ReceiveAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (SocketException ex)
                    {
                        _log.Debug("Discovery receive failed: {Error}", NetworkError.Describe(ex));
                        continue;
                    }

                    var query = DiscoveryCodec.TryDecode<DiscoveryQuery>(received.Buffer, DiscoveryQuery.TypeName);
                    if (query is null || query.Fingerprint == LocalFingerprint)
                        continue;

                    // 每秒最多回應 20 次，避免被當成放大攻擊的跳板。
                    if (Environment.TickCount64 - windowStart > 1000)
                    {
                        windowStart = Environment.TickCount64;
                        answered = 0;
                    }
                    if (++answered > 20)
                        continue;

                    bool listening;
                    lock (_gate)
                        listening = _listenerError == PeerIssue.None;
                    if (!listening)
                        continue; // 連不進來的話，回應了也沒用

                    var reply = new DiscoveryReply
                    {
                        ReplyTo = query.Id,
                        ProtocolVersion = ProtocolConstants.ProtocolVersion,
                        AppVersion = ProtocolConstants.AppVersion,
                        Fingerprint = LocalFingerprint,
                        Hostname = _options.Hostname,
                        Port = _options.LocalPort,
                        Paired = _pairing.PeerFingerprint is not null,
                        PairedWithYou = IsPairedWith(query.Fingerprint),
                    };
                    try
                    {
                        await socket.SendAsync(DiscoveryCodec.Encode(reply), received.RemoteEndPoint, ct).ConfigureAwait(false);
                    }
                    catch (SocketException ex)
                    {
                        _log.Debug("Discovery reply to {Remote} failed: {Error}", received.RemoteEndPoint, NetworkError.Describe(ex));
                    }
                }
            }
        }
    }

    /// <summary>已配對但連不上對方時，定期重新搜尋；找到同一個指紋在新的位址就改連過去。</summary>
    private async Task RediscoverLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.RediscoverInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var paired = _pairing.PeerFingerprint;
            bool connected;
            lock (_gate)
                connected = _outgoing is not null;
            if (paired is null || connected)
                continue;

            IReadOnlyList<DiscoveredPeer> peers;
            try
            {
                peers = await DiscoverAsync(ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                continue;
            }

            var match = peers.FirstOrDefault(p => p.Fingerprint == paired);
            var current = PeerEndpoint;
            if (match is not null && (current is null || !current.Address.Equals(match.Address) || current.Port != match.Port))
            {
                _log.Information("Paired device {Hostname} found at a new address {Address}:{Port}", match.Hostname, match.Address, match.Port);
                SetPeerEndpoint(match.Address, match.Port);
            }
        }
    }

    /// <summary>綁定的網卡所在網段的廣播位址（沒有綁定時是所有網卡），加上 255.255.255.255。</summary>
    private IReadOnlyList<IPEndPoint> BroadcastTargets(IPAddress? local)
    {
        var targets = new HashSet<IPEndPoint> { new(IPAddress.Broadcast, _options.DiscoveryPort) };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily != AddressFamily.InterNetwork || (local is not null && !address.Equals(local)))
                        continue;
                    var mask = unicast.IPv4Mask.GetAddressBytes();
                    var bytes = address.GetAddressBytes();
                    if (mask.All(b => b == 0))
                        continue;
                    for (var i = 0; i < 4; i++)
                        bytes[i] = (byte)(bytes[i] | ~mask[i]);
                    targets.Add(new IPEndPoint(new IPAddress(bytes), _options.DiscoveryPort));
                }
            }
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or NotImplementedException)
        {
            _log.Debug("Could not list network interfaces for discovery: {Error}", ex.Message);
        }
        return [.. targets];
    }

    private static void IgnoreConnectionReset(UdpClient socket)
    {
        if (OperatingSystem.IsWindows())
            socket.Client.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
    }

    /// <summary>對方回報的名稱只用來顯示：去掉控制字元並限制長度。</summary>
    private static string CleanHostname(string hostname)
    {
        var clean = new string(hostname.Where(c => !char.IsControl(c)).Take(64).ToArray()).Trim();
        return clean.Length == 0 ? "?" : clean;
    }
}
