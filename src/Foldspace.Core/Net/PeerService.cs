using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Channels;
using Serilog;
using Foldspace.Core.Protocol;
using Foldspace.Core.Transfer;

namespace Foldspace.Core.Net;

/// <summary>
/// 常駐服務：監聽一個 port，主動維持一條往對方的控制通道（心跳、送出請求），
/// 並處理對方連進來的控制通道與資料通道。
/// </summary>
public sealed partial class PeerService : IAsyncDisposable
{
    private readonly PeerServiceOptions _options;
    private readonly IPairingStore _pairing;
    private readonly Func<ReceiveOptions> _receiveOptions;
    private readonly ILogger _log;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _lifetime;
    private CancellationToken _serviceToken = new(canceled: true);
    private readonly List<Task> _loops = [];
    private TaskCompletionSource _nudge = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _backoffIndex;
    /// <summary>對方剛連進來時，在這個時間點之前每秒重試一次（對方可能還沒開始監聽）。</summary>
    private long _fastRetryUntil;

    // ---- 狀態（受 _gate 保護）----
    private bool _running;
    private PeerIssue _listenerError;
    private string? _listenerErrorDetail;
    private PeerIssue _outgoingError;
    /// <summary>版本不相容時對方的版本（顯示用）。</summary>
    private string? _outgoingErrorVersion;
    private ControlConnection? _outgoing;
    private DateTimeOffset _outgoingSince;
    private bool _everConnected;
    private bool _remotePaired;
    private HelloBase? _peerHello;
    private readonly HashSet<ControlConnection> _incoming = [];
    /// <summary>對方的位址。由對方發起配對、或對方換了 IP 被重新搜尋到時會改變，不必重啟服務。</summary>
    private IPAddress? _peerAddress;
    private int _peerPort;
    private PeerStatus _status = PeerStatus.Disabled;

    public PeerService(PeerServiceOptions options, IPairingStore pairing, Func<ReceiveOptions> receiveOptions)
    {
        _options = options;
        _pairing = pairing;
        _receiveOptions = receiveOptions;
        _log = options.Logger.ForContext<PeerService>();
        _peerAddress = options.PeerAddress;
        _peerPort = options.PeerPort;
    }

    /// <summary>目前要連線的對方位址；還沒設定時是 null。</summary>
    public IPEndPoint? PeerEndpoint
    {
        get
        {
            lock (_gate)
                return _peerAddress is null ? null : new IPEndPoint(_peerAddress, _peerPort);
        }
    }

    /// <summary>對方位址改變了（由對方發起配對、或重新搜尋到對方的新 IP），App 要存回設定。</summary>
    public event Action<IPEndPoint>? PeerEndpointChanged;

    /// <summary>改連另一個位址：中斷目前往舊位址的出站通道，立即重連。</summary>
    public void SetPeerEndpoint(IPAddress address, int port)
    {
        ControlConnection? stale;
        lock (_gate)
        {
            if (address.Equals(_peerAddress) && port == _peerPort)
                return;
            _peerAddress = address;
            _peerPort = port;
            stale = _outgoing is { RemoteEndPoint: { } remote } outgoing && !(remote.Address.Equals(address) && remote.Port == port)
                ? outgoing : null;
        }
        _log.Information("Peer address set to {Address}:{Port}", address, port);
        if (stale is not null)
            _ = stale.DisposeAsync().AsTask();
        NudgeReconnect();
        Recompute();
        PeerEndpointChanged?.Invoke(new IPEndPoint(address, port));
    }

    public PeerStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public string LocalFingerprint => _options.Identity.Fingerprint;

    public event Action<PeerStatus>? StatusChanged;

    // ================= 生命週期 =================

    public Task StartAsync()
    {
        lock (_gate)
        {
            if (_running)
                return Task.CompletedTask;
            _running = true;
            _everConnected = false;
            _listenerError = PeerIssue.None;
            _listenerErrorDetail = null;
            _outgoingError = PeerIssue.None;
            _outgoingErrorVersion = null;
            _backoffIndex = 0;
            _lifetime = new CancellationTokenSource();
        }

        var ct = _serviceToken = _lifetime.Token;
        _sendQueue = Channel.CreateUnbounded<TransferJob>(new UnboundedChannelOptions { SingleReader = true });
        _loops.Add(Task.Run(() => ListenLoopAsync(ct)));
        _loops.Add(Task.Run(() => OutgoingLoopAsync(ct)));
        _loops.Add(Task.Run(() => SendWorkerAsync(ct)));
        _loops.Add(Task.Run(() => StatusTickerAsync(ct)));
        _loops.Add(Task.Run(() => DiscoveryResponderLoopAsync(ct)));
        _loops.Add(Task.Run(() => RediscoverLoopAsync(ct)));
        _log.Information("Service started: local port {Port}, peer {Endpoint}, encryption {Encryption}",
            _options.LocalPort, PeerEndpoint?.ToString() ?? "not set", _options.Encryption);
        Recompute();
        return Task.CompletedTask;
    }

    /// <summary>停止監聽、停止心跳、中斷所有連線與進行中的傳輸。<paramref name="reason"/> 只寫進日誌。</summary>
    public async Task StopAsync(string reason = "service disabled")
    {
        CancellationTokenSource? lifetime;
        List<ControlConnection> connections;
        lock (_gate)
        {
            if (!_running)
                return;
            _running = false;
            lifetime = _lifetime;
            _lifetime = null;
            connections = [.. _incoming];
            if (_outgoing is not null)
                connections.Add(_outgoing);
        }

        CancelAllTransfers(CancelReason.ServiceStopped);
        _sendQueue?.Writer.TryComplete();
        _activePairing?.Prompt?.Close(PairingResult.ServiceStopped);
        lifetime?.Cancel();
        foreach (var connection in connections)
            await connection.DisposeAsync();

        try
        {
            await Task.WhenAll(_loops).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Background task failed while stopping the service");
        }
        _loops.Clear();
        lifetime?.Dispose();

        lock (_gate)
        {
            _outgoing = null;
            _incoming.Clear();
            _peerHello = null;
        }
        _log.Information("Service stopped: {Reason}", reason);
        Recompute();
    }

    public ValueTask DisposeAsync() => new(StopAsync("disposed"));

    /// <summary>
    /// 網路改變或睡眠喚醒：立即重試一次並把重連間隔歸零，
    /// 同時讓監聽立即檢查本機 IP（IP 回來時馬上重新綁定）。
    /// </summary>
    public void NudgeReconnect()
    {
        Interlocked.Exchange(ref _backoffIndex, 0);
        Interlocked.Exchange(ref _nudge, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    /// <summary>等到狀態變成「已連線」（程式由拖放啟動時最多等 10 秒）。</summary>
    public Task<bool> WaitForConnectedAsync(TimeSpan timeout, CancellationToken ct = default) =>
        WaitForStatusAsync(s => s.State == PeerState.Connected, timeout, ct);

    /// <summary>等到狀態符合 <paramref name="condition"/>；逾時回傳 false。</summary>
    public async Task<bool> WaitForStatusAsync(Func<PeerStatus, bool> condition, TimeSpan timeout, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(PeerStatus s)
        {
            if (condition(s))
                tcs.TrySetResult();
        }

        StatusChanged += OnChanged;
        try
        {
            if (condition(Status))
                return true;
            await tcs.Task.WaitAsync(timeout, ct);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            StatusChanged -= OnChanged;
        }
    }

    // ================= 狀態 =================

    private void Recompute()
    {
        PeerStatus next;
        lock (_gate)
        {
            next = ComputeStatus();
            if (next == _status)
                return;
            _status = next;
        }
        _log.Information("Peer status: {State} {Issue}{Blocked}", next.State, next.Issue,
            next.IncomingBlocked ? " (incoming blocked)" : "");
        StatusChanged?.Invoke(next);
    }

    private PeerStatus ComputeStatus()
    {
        var hostname = _peerHello?.Hostname ?? _pairing.PeerHostname;
        var version = _peerHello?.AppVersion;

        if (!_running)
            return PeerStatus.Disabled;
        if (_peerAddress is null)
            return new PeerStatus(PeerState.NotConfigured);
        if (_listenerError != PeerIssue.None)
            return new PeerStatus(PeerState.Error, _listenerError, hostname, version, Port: _options.LocalPort, Detail: _listenerErrorDetail);
        if (_outgoingError != PeerIssue.None)
            return new PeerStatus(PeerState.Error, _outgoingError, hostname, _outgoingErrorVersion ?? version);

        if (_outgoing is { } outgoing)
        {
            if (!IsPairedWith(outgoing.RemoteFingerprint) || !_remotePaired)
                return new PeerStatus(PeerState.Unpaired, PeerHostname: hostname, PeerAppVersion: version);

            var hasIncoming = _incoming.Any(c => c.RemoteFingerprint == outgoing.RemoteFingerprint);
            var blocked = !hasIncoming && DateTimeOffset.UtcNow - _outgoingSince > ProtocolConstants.IncomingIdleTimeout;
            return new PeerStatus(PeerState.Connected, PeerHostname: hostname, PeerAppVersion: version, IncomingBlocked: blocked);
        }

        // 沒有配對時談不上「離線」：曾經和對方握手成功（例如對方解除配對或解除安裝後離開）就顯示未配對。
        if (_pairing.PeerFingerprint is null)
            return new PeerStatus(_everConnected ? PeerState.Unpaired : PeerState.Searching, PeerHostname: hostname, PeerAppVersion: version);
        return new PeerStatus(_everConnected ? PeerState.Offline : PeerState.Searching, PeerHostname: hostname, PeerAppVersion: version);
    }

    /// <summary>單向連線的提醒需要隨時間更新。</summary>
    private async Task StatusTickerAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ProtocolConstants.HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            Recompute();
    }

    private bool IsPairedWith(string fingerprint) =>
        _pairing.PeerFingerprint is { } stored && string.Equals(stored, fingerprint, StringComparison.Ordinal);

    private HelloMessage CreateHello() => new()
    {
        ProtocolVersion = ProtocolConstants.ProtocolVersion,
        AppVersion = ProtocolConstants.AppVersion,
        BuildVersion = ProtocolConstants.BuildVersion,
        DeviceId = _options.Identity.Fingerprint,
        Hostname = _options.Hostname,
        Encryption = _options.Encryption,
        ListenPort = _options.LocalPort,
    };

    private static bool IsVersionCompatible(HelloBase hello) =>
        ProtocolConstants.MajorVersion(hello.ProtocolVersion) == ProtocolConstants.MajorVersion(ProtocolConstants.ProtocolVersion);



    // ================= 監聽 =================

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var address = _options.ResolveLocalAddress();
            if (address is null)
            {
                SetListenerError(PeerIssue.LocalAddressMissing);
                await WaitForNudgeAsync(ProtocolConstants.LocalAddressRecheckInterval, ct);
                continue;
            }

            var listener = new TcpListener(address, _options.LocalPort);
            if (OperatingSystem.IsWindows())
                listener.ExclusiveAddressUse = true; // 確實偵測 port 已被其他程式使用
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                SetListenerError(ex.SocketErrorCode switch
                {
                    SocketError.AddressAlreadyInUse or SocketError.AccessDenied => PeerIssue.PortInUse,
                    SocketError.AddressNotAvailable => PeerIssue.LocalAddressMissing,
                    _ => PeerIssue.ListenFailed,
                }, ex.Message);
                await WaitForNudgeAsync(ProtocolConstants.LocalAddressRecheckInterval, ct);
                continue;
            }

            var hadError = _listenerError != PeerIssue.None;
            SetListenerError(PeerIssue.None);
            if (hadError)
                NudgeReconnect();
            _log.Information("Listening on {Address}:{Port}", address, _options.LocalPort);

            using var bindingLost = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var watcher = WatchLocalAddressAsync(address, bindingLost);
            try
            {
                while (!bindingLost.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(bindingLost.Token).ConfigureAwait(false);
                    _ = Task.Run(() => HandleAcceptedAsync(client, ct), CancellationToken.None);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException ex)
            {
                _log.Warning(ex, "Listener stopped unexpectedly");
            }
            finally
            {
                bindingLost.Cancel();
                listener.Stop();
                await watcher.ConfigureAwait(false);
            }
        }
    }

    /// <summary>綁定的 IP 消失或改變時中斷監聽，讓外層迴圈重新綁定。</summary>
    private async Task WatchLocalAddressAsync(IPAddress bound, CancellationTokenSource bindingLost)
    {
        // 每 10 秒，或收到網路改變的通知時，檢查綁定的 IP 是否還在。
        while (!bindingLost.IsCancellationRequested)
        {
            await WaitForNudgeAsync(ProtocolConstants.LocalAddressRecheckInterval, bindingLost.Token).ConfigureAwait(false);
            if (bindingLost.IsCancellationRequested)
                return;
            if (!bound.Equals(_options.ResolveLocalAddress()))
            {
                _log.Warning("Local address {Address} is gone or changed; rebinding", bound);
                bindingLost.Cancel();
                return;
            }
        }
    }

    /// <summary>等待一段時間，或直到 <see cref="NudgeReconnect"/> 被呼叫。</summary>
    private async Task WaitForNudgeAsync(TimeSpan delay, CancellationToken ct)
    {
        var nudge = Volatile.Read(ref _nudge);
        try
        {
            await nudge.Task.WaitAsync(delay, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetListenerError(PeerIssue error, string? detail = null)
    {
        lock (_gate)
        {
            if (_listenerError == error)
                return;
            _listenerError = error;
            _listenerErrorDetail = detail;
        }
        if (error != PeerIssue.None)
            _log.Warning("Cannot listen: {Error} {Detail}", error, detail);
        Recompute();
    }

    private async Task HandleAcceptedAsync(TcpClient client, CancellationToken ct)
    {
        var owned = true;
        try
        {
            Tcp.ConfigureSocket(client);
            var stream = client.GetStream();
            var first = new byte[1];
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(ProtocolConstants.TlsTimeout);
                await stream.ReadExactlyAsync(first, timeout.Token).ConfigureAwait(false);
            }

            switch (first[0])
            {
                case ProtocolConstants.ControlChannel:
                    owned = false;
                    await HandleIncomingControlAsync(client, ct).ConfigureAwait(false);
                    break;
                case ProtocolConstants.DataChannelTls:
                case ProtocolConstants.DataChannelPlain:
                    owned = !await HandleIncomingDataAsync(client, first[0], ct).ConfigureAwait(false);
                    break;
                case ProtocolConstants.SpeedTestChannel:
                    await HandleIncomingSpeedTestAsync(client, ct).ConfigureAwait(false);
                    break;
                default:
                    _log.Warning("Unknown connection type 0x{Type:X2} from {Remote}", first[0], client.Client.RemoteEndPoint);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException
                                       or AuthenticationException or ProtocolException or ObjectDisposedException)
        {
            _log.Debug("Incoming connection ended: {Error}", NetworkError.Describe(ex));
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error while handling an incoming connection");
        }
        finally
        {
            if (owned)
                client.Dispose();
        }
    }

    // ================= 出站控制通道 =================

    private async Task OutgoingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var nudge = Volatile.Read(ref _nudge);
            if (PeerEndpoint is { } endpoint)
            {
                var connection = await TryConnectControlAsync(endpoint, ct).ConfigureAwait(false);
                if (connection is not null)
                {
                    Interlocked.Exchange(ref _backoffIndex, 0);
                    await RunOutgoingAsync(connection, ct).ConfigureAwait(false);
                    nudge = Volatile.Read(ref _nudge);
                }
            }

            var index = Interlocked.Increment(ref _backoffIndex) - 1;
            var delay = Environment.TickCount64 < Interlocked.Read(ref _fastRetryUntil)
                ? TimeSpan.FromSeconds(1)
                : ProtocolConstants.ReconnectBackoff[Math.Min(index, ProtocolConstants.ReconnectBackoff.Length - 1)];
            try
            {
                await nudge.Task.WaitAsync(delay, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>建立出站控制通道並完成握手；失敗回傳 null 並更新狀態。</summary>
    private async Task<ControlConnection?> TryConnectControlAsync(IPEndPoint endpoint, CancellationToken ct)
    {
        ControlConnection? connection = null;
        try
        {
            var (conn, ack) = await HandshakeAsync(endpoint, ct).ConfigureAwait(false);
            connection = conn;

            if (!IsVersionCompatible(ack))
            {
                SetOutgoingError(PeerIssue.VersionIncompatible, ack.AppVersion);
                await conn.DisposeAsync();
                return null;
            }

            if (_pairing.PeerFingerprint is not null && !IsPairedWith(conn.RemoteFingerprint))
            {
                SetOutgoingError(PeerIssue.PairingMismatch);
                await conn.DisposeAsync();
                return null;
            }

            if (IsPairedWith(conn.RemoteFingerprint) && ack.Hostname != _pairing.PeerHostname)
                _pairing.UpdatePeerHostname(ack.Hostname);

            return conn;
        }
        catch (HandshakeRejectedException ex)
        {
            SetOutgoingError(ex.Issue);
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException
                                       or AuthenticationException or ProtocolException or OperationCanceledException)
        {
            if (!ct.IsCancellationRequested)
                _log.Debug("Connecting to {Endpoint} failed: {Error}", endpoint, NetworkError.Describe(ex));
            SetOutgoingError(PeerIssue.None); // 連不上時顯示離線/尋找中，而不是上一次的錯誤
        }

        if (connection is not null)
            await connection.DisposeAsync();
        return null;
    }

    private sealed class HandshakeRejectedException(PeerIssue issue) : Exception(issue.ToString())
    {
        public PeerIssue Issue { get; } = issue;
    }

    private async Task<(ControlConnection Connection, HelloAckMessage Ack)> HandshakeAsync(IPEndPoint endpoint, CancellationToken ct)
    {
        var tcp = await Tcp.ConnectAsync(endpoint, ProtocolConstants.ControlChannel, ct).ConfigureAwait(false);
        ControlConnection? connection = null;
        try
        {
            var (ssl, fingerprint) = await Tls.AuthenticateAsClientAsync(tcp.GetStream(), _options.Identity, ct).ConfigureAwait(false);
            connection = new ControlConnection(tcp, ssl, ConnectionRole.Outgoing, fingerprint, _log);
            var hello = CreateHello();
            await connection.SendAsync(hello, ct).ConfigureAwait(false);
            var reply = await connection.ReadHandshakeAsync(ProtocolConstants.HelloTimeout, ct).ConfigureAwait(false);

            switch (reply)
            {
                case HelloAckMessage ack:
                    connection.RemoteHello = ack;
                    return (connection, ack);
                case ErrorMessage error:
                    throw new HandshakeRejectedException(error.Code switch
                    {
                        ErrorCode.PairingMismatch => PeerIssue.PeerHasOtherPairing,
                        ErrorCode.NotAllowed => PeerIssue.PeerRejectsAddress,
                        _ => PeerIssue.VersionIncompatible,
                    });
                default:
                    throw new ProtocolException($"Unexpected handshake reply {reply.GetType().Name}");
            }
        }
        catch
        {
            if (connection is not null)
                await connection.DisposeAsync();
            else
                tcp.Dispose();
            throw;
        }
    }

    private void SetOutgoingError(PeerIssue error, string? peerVersion = null)
    {
        lock (_gate)
        {
            if (_outgoingError == error && _outgoingErrorVersion == peerVersion)
                return;
            _outgoingError = error;
            _outgoingErrorVersion = peerVersion;
        }
        if (error != PeerIssue.None)
            _log.Warning("Connection error: {Error} {PeerVersion}", error, peerVersion);
        Recompute();
    }

    /// <summary>持有出站通道直到心跳逾時或斷線。</summary>
    private async Task RunOutgoingAsync(ControlConnection connection, CancellationToken ct)
    {
        connection.MessageReceived = HandleMessageAsync;
        lock (_gate)
        {
            _outgoing = connection;
            _outgoingSince = DateTimeOffset.UtcNow;
            _outgoingError = PeerIssue.None;
            _outgoingErrorVersion = null;
            _everConnected = true;
            _peerHello = connection.RemoteHello;
            _remotePaired = connection.RemoteHello is HelloAckMessage { Paired: true };
        }
        connection.Start();
        _log.Information("Connected to {Hostname} ({Endpoint}), version {Version}",
            connection.RemoteHello?.Hostname, connection.RemoteEndPoint, connection.RemoteHello?.BuildVersion ?? connection.RemoteHello?.AppVersion);
        Recompute();

        var missed = 0;
        try
        {
            while (!ct.IsCancellationRequested && !connection.Completion.IsCompleted)
            {
                var started = DateTimeOffset.UtcNow;
                try
                {
                    await connection.RequestAsync(new PingMessage(), ProtocolConstants.HeartbeatInterval, ct).ConfigureAwait(false);
                    missed = 0;
                }
                catch (TimeoutException)
                {
                    missed++;
                    _log.Information("Heartbeat not answered ({Missed} in a row)", missed);
                    if (missed >= ProtocolConstants.MissedHeartbeatLimit)
                        break;
                    continue; // 逾時本身已經等了 5 秒
                }

                var remaining = ProtocolConstants.HeartbeatInterval - (DateTimeOffset.UtcNow - started);
                if (remaining > TimeSpan.Zero)
                    await Task.WhenAny(Task.Delay(remaining, ct), connection.Completion).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            await connection.DisposeAsync();
            lock (_gate)
            {
                if (_outgoing == connection)
                    _outgoing = null;
            }
            if (!ct.IsCancellationRequested)
            {
                _log.Warning("Connection to {Hostname} lost", connection.RemoteHello?.Hostname);
                CancelTransfers(TransferDirection.Send, CancelReason.PeerOffline);
            }
            _activePairing?.AbortIfOn(connection, PairingResult.ConnectionLost);
            Recompute();
        }
    }
}
