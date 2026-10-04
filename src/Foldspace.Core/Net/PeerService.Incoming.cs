using System.Net;
using System.Net.Sockets;
using Foldspace.Core.Protocol;
using Foldspace.Core.Transfer;

namespace Foldspace.Core.Net;

public sealed partial class PeerService
{
    private async Task HandleIncomingControlAsync(TcpClient client, CancellationToken ct)
    {
        var remoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
        ControlConnection connection;
        try
        {
            var (ssl, fingerprint) = await Tls.AuthenticateAsServerAsync(client.GetStream(), _options.Identity, ct).ConfigureAwait(false);
            connection = new ControlConnection(client, ssl, ConnectionRole.Incoming, fingerprint, _log);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        await using var _ = connection;
        var message = await connection.ReadHandshakeAsync(ProtocolConstants.HelloTimeout, ct).ConfigureAwait(false);
        if (message is not HelloMessage hello)
        {
            _log.Warning("First message on an incoming control channel from {Remote} was not HELLO", remoteAddress);
            return;
        }

        // 已配對：只認指紋。未配對：接受同網段任何電腦（讓對方可以從搜尋結果發起配對）；
        // 配對視窗同時只有一個，被拒絕或逾時的電腦 30 秒內不能再發起（見 RespondToPairingAsync）。
        if (_pairing.PeerFingerprint is not null && !IsPairedWith(connection.RemoteFingerprint))
        {
            _log.Warning("Rejecting {Hostname} ({Remote}): fingerprint {Fingerprint} does not match the paired device",
                hello.Hostname, remoteAddress, connection.RemoteFingerprint);
            await connection.SendAsync(new ErrorMessage
            {
                ReplyTo = hello.Id,
                Code = ErrorCode.PairingMismatch,
            }, ct).ConfigureAwait(false);
            return;
        }

        await connection.SendAsync(new HelloAckMessage
        {
            ReplyTo = hello.Id,
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
            AppVersion = ProtocolConstants.AppVersion,
            BuildVersion = ProtocolConstants.BuildVersion,
            DeviceId = _options.Identity.Fingerprint,
            Hostname = _options.Hostname,
            Encryption = _options.Encryption,
            ListenPort = _options.LocalPort,
            Paired = IsPairedWith(connection.RemoteFingerprint),
        }, ct).ConfigureAwait(false);

        if (!IsVersionCompatible(hello))
        {
            // 對方會從 HELLO_ACK 看到版本並顯示錯誤；本機的出站通道也會偵測到。
            _log.Warning("{Hostname} runs an incompatible version: {Version}", hello.Hostname, hello.AppVersion);
            return;
        }

        connection.RemoteHello = hello;
        connection.MessageReceived = HandleMessageAsync;
        lock (_gate)
        {
            _incoming.Add(connection);
            _peerHello = hello;
        }
        connection.Start();
        Recompute();

        // 對方能連進來，表示網路已經恢復：立即重試本機的出站通道，不必等重試間隔（最長 30 秒）。
        bool needOutgoing;
        lock (_gate)
            needOutgoing = _outgoing is null;
        if (needOutgoing)
        {
            // 對方可能是網路剛恢復，還沒重新開始監聽；接下來 10 秒內改為每秒重試。
            _log.Information("Peer connected in; retrying the outgoing connection now");
            Interlocked.Exchange(ref _fastRetryUntil, Environment.TickCount64 + 10_000);
            NudgeReconnect();
        }

        try
        {
            // 對方每 5 秒會送 PING；太久沒有任何訊息就當作連線已失效。
            using var timer = new PeriodicTimer(ProtocolConstants.HeartbeatInterval);
            while (!connection.Completion.IsCompleted)
            {
                var tick = timer.WaitForNextTickAsync(ct).AsTask();
                await Task.WhenAny(tick, connection.Completion).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                    break;
                if (DateTimeOffset.UtcNow - connection.LastReceived > ProtocolConstants.IncomingIdleTimeout)
                {
                    _log.Information("Incoming control channel idle for too long; closing");
                    break;
                }
            }
        }
        finally
        {
            lock (_gate)
                _incoming.Remove(connection);
            _activePairing?.AbortIfOn(connection, PairingResult.ConnectionLost);
            Recompute();
        }
    }

    /// <summary>兩個方向的控制通道共用的訊息處理。不可以在這裡長時間等待（會卡住讀取迴圈）。</summary>
    private async Task HandleMessageAsync(ControlConnection connection, ControlMessage message)
    {
        switch (message)
        {
            case PingMessage ping:
                await connection.SendAsync(new PongMessage { ReplyTo = ping.Id }).ConfigureAwait(false);
                break;

            case PairRequestMessage request:
                _ = Task.Run(() => RespondToPairingAsync(connection, request));
                break;

            case PairRevealMessage reveal:
                _activePairing?.OnReveal(connection, reveal);
                break;

            case PairAcceptMessage:
                _activePairing?.OnRemoteDecision(connection, true, null);
                break;

            case PairRejectMessage reject:
                _activePairing?.OnRemoteDecision(connection, false, reject.Reason);
                break;

            case UnpairMessage:
                if (IsPairedWith(connection.RemoteFingerprint))
                {
                    _log.Information("Peer removed the pairing");
                    ClearPairingLocally();
                }
                break;

            case TransferOfferMessage offer:
                _ = Task.Run(() => HandleOfferAsync(connection, offer));
                break;

            case TransferCancelMessage cancel:
                // 對方送來的是「它那邊」的原因，換成本機觀點的原因。
                FindJob(cancel.JobId)?.Cancel(FromPeerPerspective(cancel.Reason));
                break;

            case TransferResultMessage result:
                if (_resultWaiters.TryGetValue(result.JobId, out var waiter))
                    waiter.TrySetResult(result);
                break;

            case SpeedTestRequestMessage speedTest:
                _ = Task.Run(() => HandleSpeedTestRequestAsync(connection, speedTest));
                break;

            case SpeedTestResultMessage speedTestResult:
                HandleSpeedTestResult(speedTestResult);
                break;

            case ErrorMessage error:
                _log.Warning("Peer reported error {Code}", error.Code);
                break;

            default:
                _log.Information("Ignoring control message {Type}", message.GetType().Name);
                break;
        }
    }

    private void ClearPairingLocally()
    {
        _pairing.ClearPairing();
        lock (_gate)
            _remotePaired = false;
        CancelAllTransfers(CancelReason.Unpaired);
        Recompute();
    }

    // ================= 資料通道（入站）=================

    /// <summary>回傳 true 表示連線的所有權已交給接收任務。</summary>
    private async Task<bool> HandleIncomingDataAsync(TcpClient client, byte channelType, CancellationToken ct)
    {
        Stream stream = client.GetStream();
        PendingReceive? pending;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ProtocolConstants.TlsTimeout * 2);

        if (channelType == ProtocolConstants.DataChannelTls)
        {
            var (ssl, fingerprint) = await Tls.AuthenticateAsServerAsync(stream, _options.Identity, timeout.Token).ConfigureAwait(false);
            stream = ssl;
            if (!IsPairedWith(fingerprint))
            {
                _log.Warning("Rejecting data channel: fingerprint {Fingerprint} is not paired", fingerprint);
                await ssl.DisposeAsync();
                return false;
            }

            var jobIdBytes = new byte[16];
            await ssl.ReadExactlyAsync(jobIdBytes, timeout.Token).ConfigureAwait(false);
            pending = TakePendingReceive(new Guid(jobIdBytes), token: null);
        }
        else
        {
            var tokenBytes = new byte[ProtocolConstants.DataTokenBytes];
            await stream.ReadExactlyAsync(tokenBytes, timeout.Token).ConfigureAwait(false);
            pending = TakePendingReceive(jobId: null, Convert.ToBase64String(tokenBytes));
        }

        if (pending is null)
        {
            _log.Warning("Rejecting data channel from {Remote}: no matching job or invalid token", client.Client.RemoteEndPoint);
            await stream.DisposeAsync();
            return false;
        }

        return pending.DataArrived.TrySetResult((client, stream));
    }
}
