using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Net;

public sealed partial class PeerService
{
    /// <summary>
    /// 設定視窗的 Test Connection：另外開一條連線做完整握手並量來回延遲，
    /// 失敗時依失敗的步驟給出具體原因。服務停用時也能使用。
    /// </summary>
    public Task<TestConnectionResult> TestConnectionAsync(CancellationToken ct = default) =>
        PeerEndpoint is { } endpoint
            ? TestConnectionAsync(endpoint, ct)
            : Task.FromResult(Fail(TestOutcome.NotConfigured, null));

    internal async Task<TestConnectionResult> TestConnectionAsync(IPEndPoint endpoint, CancellationToken ct)
    {
        var target = $"{endpoint.Address}:{endpoint.Port}";

        // 1. TCP
        TcpClient tcp;
        try
        {
            tcp = await Tcp.ConnectAsync(endpoint, ProtocolConstants.ControlChannel, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return Fail(TestOutcome.Timeout, target);
        }
        catch (SocketException ex)
        {
            return Fail(ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => TestOutcome.Refused,
                SocketError.HostUnreachable or SocketError.NetworkUnreachable => TestOutcome.Unreachable,
                SocketError.TimedOut => TestOutcome.Timeout,
                _ => TestOutcome.SocketError,
            }, target, detail: ex.Message);
        }

        ControlConnection? connection = null;
        try
        {
            // 2. TLS
            string fingerprint;
            try
            {
                var (ssl, fp) = await Tls.AuthenticateAsClientAsync(tcp.GetStream(), _options.Identity, ct).ConfigureAwait(false);
                fingerprint = fp;
                connection = new ControlConnection(tcp, ssl, ConnectionRole.Outgoing, fingerprint, _log);
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException or OperationCanceledException)
            {
                tcp.Dispose();
                return Fail(TestOutcome.TlsFailed, target);
            }

            // 3. HELLO
            ControlMessage reply;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await connection.SendAsync(CreateHello(), ct).ConfigureAwait(false);
                reply = await connection.ReadHandshakeAsync(ProtocolConstants.HelloTimeout, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ProtocolException)
            {
                return Fail(TestOutcome.NoHandshakeResponse, target);
            }

            if (reply is ErrorMessage error)
            {
                return Fail(error.Code switch
                {
                    ErrorCode.PairingMismatch => TestOutcome.PeerHasOtherPairing,
                    ErrorCode.NotAllowed => TestOutcome.PeerRejectsAddress,
                    _ => TestOutcome.VersionIncompatible,
                }, target);
            }
            if (reply is not HelloAckMessage ack)
                return Fail(TestOutcome.BadResponse, target);

            // 4. 版本、配對
            if (!IsVersionCompatible(ack))
                return Fail(TestOutcome.VersionIncompatible, target, ack);
            if (_pairing.PeerFingerprint is not null && !IsPairedWith(fingerprint))
                return Fail(TestOutcome.PairingMismatch, target, ack);

            // 5. 來回延遲：用一次 PING / PONG 量，比握手更準。
            connection.Start();
            double rtt;
            try
            {
                stopwatch.Restart();
                await connection.RequestAsync(new PingMessage(), ProtocolConstants.HeartbeatInterval, ct).ConfigureAwait(false);
                rtt = stopwatch.Elapsed.TotalMilliseconds;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                return Fail(TestOutcome.NoHeartbeat, target, ack);
            }

            var paired = IsPairedWith(fingerprint) && ack.Paired;
            return new TestConnectionResult
            {
                Outcome = TestOutcome.Success,
                Endpoint = target,
                PeerHostname = ack.Hostname,
                PeerAppVersion = ack.AppVersion,
                Paired = paired,
                RoundTripMs = rtt,
            };
        }
        finally
        {
            if (connection is not null)
                await connection.DisposeAsync();
        }
    }

    private static TestConnectionResult Fail(TestOutcome outcome, string? endpoint, HelloAckMessage? ack = null, string? detail = null) => new()
    {
        Outcome = outcome,
        Endpoint = endpoint,
        Detail = detail,
        PeerHostname = ack?.Hostname,
        PeerAppVersion = ack?.AppVersion,
    };
}

public sealed partial class PeerService
{
    /// <summary>測試用：在出站控制通道上直接送出請求（繞過本機的狀態檢查，用來驗證對方的防護）。</summary>
    internal async Task<ControlMessage> RequestOnOutgoingAsync(ControlMessage request, TimeSpan timeout)
    {
        ControlConnection connection;
        lock (_gate)
            connection = _outgoing ?? throw new InvalidOperationException("No outgoing control channel");
        return await connection.RequestAsync(request, timeout).ConfigureAwait(false);
    }

    internal bool HasOutgoingConnection
    {
        get { lock (_gate) return _outgoing is not null; }
    }
}
