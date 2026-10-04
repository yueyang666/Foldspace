using System.Collections.Concurrent;
using Foldspace.Core.Pairing;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Net;

public sealed partial class PeerService
{
    private PairingSession? _activePairing;
    /// <summary>本機拒絕或逾時的電腦（指紋）→ 這個時間點之前不能再發起配對。</summary>
    private readonly ConcurrentDictionary<string, long> _pairingCooldowns = new(StringComparer.Ordinal);

    /// <summary>需要顯示配對對話框（兩端都會觸發）。</summary>
    public event Action<PairingPrompt>? PairingPromptRequested;

    /// <summary>配對結束（成功或失敗）。由對方發起的配對也會觸發。</summary>
    public event Action<PairingOutcome>? PairingFinished;

    /// <summary>本機按下「配對」。</summary>
    public async Task<PairingOutcome> PairAsync(CancellationToken ct = default)
    {
        ControlConnection? connection;
        lock (_gate)
            connection = _outgoing;
        if (connection is null)
            return Finish(new PairingOutcome(PairingResult.NotConnected));

        var session = new PairingSession(connection, isInitiator: true);
        if (Interlocked.CompareExchange(ref _activePairing, session, null) is not null)
            return new PairingOutcome(PairingResult.InProgress);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Aborted);
            timeout.CancelAfter(ProtocolConstants.PairingTimeout);

            var myNonce = PairingCode.NewNonce();
            var reply = await connection.RequestAsync(
                new PairRequestMessage { Commitment = Convert.ToBase64String(PairingCode.Commit(myNonce)) },
                TimeSpan.FromSeconds(10), timeout.Token).ConfigureAwait(false);

            if (reply is PairRejectMessage reject)
                return Finish(new PairingOutcome(PairingResult.PeerCannotPair, connection.RemoteHello?.Hostname, reject.Reason));
            if (reply is not PairNonceMessage nonceMessage || !TryDecodeNonce(nonceMessage.Nonce, out var theirNonce))
                return Finish(new PairingOutcome(PairingResult.BadResponse, connection.RemoteHello?.Hostname));

            await connection.SendAsync(new PairRevealMessage { Nonce = Convert.ToBase64String(myNonce) }, timeout.Token).ConfigureAwait(false);
            var code = PairingCode.Compute(_options.Identity.Fingerprint, connection.RemoteFingerprint, myNonce, theirNonce);
            return Finish(await DecideAsync(session, code, timeout.Token).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException)
        {
            return Finish(new PairingOutcome(session.AbortReason ?? (ex is IOException ? PairingResult.ConnectionLost : PairingResult.Timeout),
                connection.RemoteHello?.Hostname));
        }
        finally
        {
            session.Prompt?.Close(session.AbortReason ?? PairingResult.Success);
            Interlocked.CompareExchange(ref _activePairing, null, session);
        }
    }

    /// <summary>對方按下「配對」。</summary>
    private async Task RespondToPairingAsync(ControlConnection connection, PairRequestMessage request)
    {
        // 未配對時同網段任何電腦都能發起配對：剛被拒絕或逾時的電腦，冷卻期內一律回「忙碌」，不再跳出視窗。
        if (_pairingCooldowns.TryGetValue(connection.RemoteFingerprint, out var until) && Environment.TickCount64 < until)
        {
            _log.Information("Ignoring a pairing request from {Hostname}: cooling down after a rejected attempt", connection.RemoteHello?.Hostname);
            await TrySendAsync(connection, new PairRejectMessage { ReplyTo = request.Id, Reason = PairRejectReason.Busy });
            return;
        }

        var session = new PairingSession(connection, isInitiator: false);
        var existing = Interlocked.CompareExchange(ref _activePairing, session, null);
        if (existing is not null)
        {
            // 兩端同時按下配對：指紋較小的一端當發起端，另一端放棄自己的請求改為回應。
            var iWin = string.CompareOrdinal(_options.Identity.Fingerprint, connection.RemoteFingerprint) < 0;
            if (!existing.IsInitiator || iWin)
            {
                await TrySendAsync(connection, new PairRejectMessage { ReplyTo = request.Id, Reason = PairRejectReason.Busy });
                return;
            }
            existing.Abort(PairingResult.Superseded);
            // 被放棄的那一方可能已經自行清掉 _activePairing，所以兩種情況都要試。
            if (Interlocked.CompareExchange(ref _activePairing, session, existing) != existing &&
                Interlocked.CompareExchange(ref _activePairing, session, null) is not null)
            {
                await TrySendAsync(connection, new PairRejectMessage { ReplyTo = request.Id, Reason = PairRejectReason.Busy });
                return;
            }
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(session.Aborted, connection.Closing);
            timeout.CancelAfter(ProtocolConstants.PairingTimeout);

            byte[] commitment;
            try
            {
                commitment = Convert.FromBase64String(request.Commitment);
            }
            catch (FormatException)
            {
                await TrySendAsync(connection, new PairRejectMessage { ReplyTo = request.Id, Reason = PairRejectReason.InvalidRequest });
                Finish(new PairingOutcome(PairingResult.BadResponse, connection.RemoteHello?.Hostname));
                return;
            }

            var myNonce = PairingCode.NewNonce();
            await connection.SendAsync(new PairNonceMessage { ReplyTo = request.Id, Nonce = Convert.ToBase64String(myNonce) }, timeout.Token).ConfigureAwait(false);

            var reveal = await session.Reveal.Task.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token).ConfigureAwait(false);
            if (!TryDecodeNonce(reveal.Nonce, out var theirNonce) || !PairingCode.VerifyCommitment(commitment, theirNonce))
            {
                _log.Warning("Pairing verification failed: the revealed nonce does not match the commitment (possible man-in-the-middle)");
                await TrySendAsync(connection, new PairRejectMessage { Reason = PairRejectReason.VerificationFailed });
                Finish(new PairingOutcome(PairingResult.VerificationFailed, connection.RemoteHello?.Hostname));
                return;
            }

            var code = PairingCode.Compute(_options.Identity.Fingerprint, connection.RemoteFingerprint, theirNonce, myNonce);
            var outcome = Finish(await DecideAsync(session, code, timeout.Token).ConfigureAwait(false));
            if (outcome.Result is PairingResult.Rejected or PairingResult.Timeout)
                StartPairingCooldown(connection.RemoteFingerprint);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException)
        {
            var outcome = Finish(new PairingOutcome(session.AbortReason ?? (ex is IOException ? PairingResult.ConnectionLost : PairingResult.Timeout),
                connection.RemoteHello?.Hostname));
            if (outcome.Result is PairingResult.Rejected or PairingResult.Timeout)
                StartPairingCooldown(connection.RemoteFingerprint);
        }
        finally
        {
            session.Prompt?.Close(session.AbortReason ?? PairingResult.Success);
            Interlocked.CompareExchange(ref _activePairing, null, session);
        }
    }

    /// <summary>顯示數字，等兩邊的使用者都按下確認。</summary>
    private async Task<PairingOutcome> DecideAsync(PairingSession session, string code, CancellationToken ct)
    {
        var connection = session.Connection;
        var hostname = connection.RemoteHello?.Hostname ?? connection.RemoteEndPoint?.Address.ToString() ?? "?";
        var prompt = new PairingPrompt(code, hostname, connection.RemoteFingerprint, session.IsInitiator);
        session.Prompt = prompt;
        _log.Information("Pairing code generated; waiting for both users to confirm (peer {Hostname})", hostname);
        PairingPromptRequested?.Invoke(prompt);

        var accepted = await prompt.Decision.WaitAsync(ct).ConfigureAwait(false);
        if (!accepted)
        {
            await TrySendAsync(connection, new PairRejectMessage { Reason = PairRejectReason.UserRejected });
            return new PairingOutcome(prompt.CloseReason ?? PairingResult.Rejected, hostname);
        }

        await connection.SendAsync(new PairAcceptMessage(), ct).ConfigureAwait(false);
        var (remoteAccepted, reason) = await session.RemoteDecision.Task.WaitAsync(ct).ConfigureAwait(false);
        if (!remoteAccepted)
            return new PairingOutcome(PairingResult.RejectedByPeer, hostname, reason);

        _pairing.SavePairing(connection.RemoteFingerprint, hostname);
        _pairingCooldowns.Clear();
        lock (_gate)
            _remotePaired = true;
        _log.Information("Paired with {Hostname}, fingerprint {Fingerprint}", hostname, connection.RemoteFingerprint);

        // 由對方發起時，本機可能還不知道對方在哪（或設定的是別台）：改成連回發起配對的那台。
        if (!session.IsInitiator && connection.RemoteEndPoint is { } remote && connection.RemoteHello?.ListenPort is { } port &&
            port is >= ProtocolConstants.MinPort and <= ProtocolConstants.MaxPort)
            SetPeerEndpoint(remote.Address, port);
        Recompute();
        return new PairingOutcome(PairingResult.Success, hostname);
    }

    /// <summary>解除配對：通知對方並清除本機紀錄。</summary>
    public async Task UnpairAsync()
    {
        List<ControlConnection> connections;
        lock (_gate)
        {
            connections = [.. _incoming];
            if (_outgoing is not null)
                connections.Add(_outgoing);
        }

        var paired = connections.Where(c => IsPairedWith(c.RemoteFingerprint)).ToList();
        foreach (var connection in paired.Take(1))
            await TrySendAsync(connection, new UnpairMessage());

        _log.Information("Pairing removed");
        ClearPairingLocally();
    }

    private void StartPairingCooldown(string fingerprint) =>
        _pairingCooldowns[fingerprint] = Environment.TickCount64 + (long)ProtocolConstants.PairingCooldown.TotalMilliseconds;

    private PairingOutcome Finish(PairingOutcome outcome)
    {
        _log.Information("Pairing result: {Result} {Hostname} {PeerReason}", outcome.Result, outcome.PeerHostname, outcome.PeerReason);
        PairingFinished?.Invoke(outcome);
        return outcome;
    }

    private static bool TryDecodeNonce(string base64, out byte[] nonce)
    {
        try
        {
            nonce = Convert.FromBase64String(base64);
            return nonce.Length == PairingCode.NonceBytes;
        }
        catch (FormatException)
        {
            nonce = [];
            return false;
        }
    }

    private async Task TrySendAsync(ControlConnection connection, ControlMessage message)
    {
        try
        {
            await connection.SendAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            _log.Debug("Sending {Type} failed: {Error}", message.GetType().Name, NetworkError.Describe(ex));
        }
    }

    private sealed class PairingSession(ControlConnection connection, bool isInitiator)
    {
        private readonly CancellationTokenSource _aborted = new();

        public ControlConnection Connection { get; } = connection;
        public bool IsInitiator { get; } = isInitiator;
        public PairingPrompt? Prompt { get; set; }
        public PairingResult? AbortReason { get; private set; }
        public CancellationToken Aborted => _aborted.Token;

        public TaskCompletionSource<PairRevealMessage> Reveal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<(bool Accepted, PairRejectReason? Reason)> RemoteDecision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnReveal(ControlConnection from, PairRevealMessage reveal)
        {
            if (from == Connection)
                Reveal.TrySetResult(reveal);
        }

        public void OnRemoteDecision(ControlConnection from, bool accepted, PairRejectReason? reason)
        {
            if (from != Connection)
                return;
            RemoteDecision.TrySetResult((accepted, reason));
            if (!accepted)
                Prompt?.Close(PairingResult.RejectedByPeer);
        }

        public void AbortIfOn(ControlConnection connection, PairingResult reason)
        {
            if (connection == Connection)
                Abort(reason);
        }

        public void Abort(PairingResult reason)
        {
            AbortReason ??= reason;
            Prompt?.Close(reason);
            try { _aborted.Cancel(); } catch (ObjectDisposedException) { }
        }
    }
}
