using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Net;

/// <summary>測速結果：接收端量到的位元組數與時間。</summary>
public sealed record SpeedTestResult(bool Encrypted, long Bytes, TimeSpan Elapsed)
{
    public double Mbps => Elapsed.TotalSeconds <= 0 ? 0 : Bytes * 8 / Elapsed.TotalSeconds / 1_000_000;
    public double MegabytesPerSecond => Elapsed.TotalSeconds <= 0 ? 0 : Bytes / Elapsed.TotalSeconds / (1024 * 1024);
}

/// <summary>
/// 網路速度測試（診斷用）：從記憶體送資料給已配對的對方，對方直接丟掉，不讀寫任何磁碟，
/// 用來分辨傳輸慢是網路還是磁碟的問題。只測「本機 → 對方」這個方向。
/// </summary>
public sealed partial class PeerService
{
    private static readonly TimeSpan SpeedTestMaxDuration = TimeSpan.FromSeconds(20);

    private sealed record PendingSpeedTest(ControlConnection Connection, bool Encrypted, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, PendingSpeedTest> _speedTests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<SpeedTestResultMessage>> _speedTestWaiters = new(StringComparer.Ordinal);

    public async Task<SpeedTestResult> RunSpeedTestAsync(bool encrypted, TimeSpan duration, CancellationToken ct = default)
    {
        ControlConnection? connection;
        lock (_gate)
            connection = _outgoing;
        var endpoint = PeerEndpoint;
        if (connection is null || endpoint is null || !IsPairedWith(connection.RemoteFingerprint))
            throw new InvalidOperationException("Not connected to the paired device");

        var reply = await connection.RequestAsync(new SpeedTestRequestMessage { Encrypted = encrypted }, TimeSpan.FromSeconds(5), ct)
            .ConfigureAwait(false);
        if (reply is not SpeedTestReadyMessage ready)
            throw new ProtocolException("The other computer did not accept the speed test");

        var waiter = new TaskCompletionSource<SpeedTestResultMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _speedTestWaiters[ready.Token] = waiter;
        try
        {
            using var tcp = await Tcp.ConnectAsync(endpoint, ProtocolConstants.SpeedTestChannel, ct).ConfigureAwait(false);
            Stream stream = tcp.GetStream();
            await stream.WriteAsync(Convert.FromBase64String(ready.Token), ct).ConfigureAwait(false);
            SslStream? ssl = null;
            if (encrypted)
            {
                (ssl, var fingerprint) = await Tls.AuthenticateAsClientAsync(stream, _options.Identity, ct).ConfigureAwait(false);
                if (!IsPairedWith(fingerprint))
                    throw new ProtocolException("The speed test peer is not the paired device");
                stream = ssl;
            }

            // 亂數內容：避免任何一段路徑壓縮資料而量到不實的速度。
            var buffer = new byte[ProtocolConstants.TransferBufferSize];
            RandomNumberGenerator.Fill(buffer);
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < duration)
                await stream.WriteAsync(buffer, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            if (ssl is not null)
                await ssl.ShutdownAsync().ConfigureAwait(false);
            tcp.Client.Shutdown(SocketShutdown.Send);

            var result = await waiter.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            var measured = new SpeedTestResult(encrypted, result.Bytes, TimeSpan.FromMilliseconds(result.Milliseconds));
            _log.Information("Speed test to {Hostname} ({Mode}): {Mbps:F0} Mbps, {Bytes} bytes in {Ms} ms",
                connection.RemoteHello?.Hostname, encrypted ? "encrypted" : "plain", measured.Mbps, result.Bytes, result.Milliseconds);
            return measured;
        }
        finally
        {
            _speedTestWaiters.TryRemove(ready.Token, out _);
        }
    }

    /// <summary>同時開 <paramref name="streams"/> 條連線測速，結果是各條加總（分辨是單一連線的上限還是整條路徑的上限）。</summary>
    public async Task<SpeedTestResult> RunParallelSpeedTestAsync(int streams, bool encrypted, TimeSpan duration, CancellationToken ct = default)
    {
        var results = await Task.WhenAll(Enumerable.Range(0, streams).Select(_ => RunSpeedTestAsync(encrypted, duration, ct))).ConfigureAwait(false);
        // 各條是同時進行的：總量除以最長的那一段時間。
        var combined = new SpeedTestResult(encrypted, results.Sum(r => r.Bytes), results.Max(r => r.Elapsed));
        _log.Information("Parallel speed test ({Streams} streams, {Mode}): {Mbps:F0} Mbps", streams, encrypted ? "encrypted" : "plain", combined.Mbps);
        return combined;
    }

    /// <summary>對方要測速：發一個 30 秒內有效的一次性 token。只接受已配對的對方。</summary>
    private async Task HandleSpeedTestRequestAsync(ControlConnection connection, SpeedTestRequestMessage request)
    {
        if (!IsPairedWith(connection.RemoteFingerprint))
        {
            await TrySendAsync(connection, new ErrorMessage { ReplyTo = request.Id, Code = ErrorCode.NotAllowed });
            return;
        }
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(ProtocolConstants.DataTokenBytes));
        _speedTests[token] = new PendingSpeedTest(connection, request.Encrypted, DateTimeOffset.UtcNow.AddSeconds(30));
        await TrySendAsync(connection, new SpeedTestReadyMessage { ReplyTo = request.Id, Token = token });
    }

    private void HandleSpeedTestResult(SpeedTestResultMessage result)
    {
        if (_speedTestWaiters.TryGetValue(result.Token, out var waiter))
            waiter.TrySetResult(result);
    }

    /// <summary>測速通道：讀到對方關閉為止，資料直接丟掉，最後經控制通道回報量到的結果。</summary>
    private async Task HandleIncomingSpeedTestAsync(TcpClient client, CancellationToken ct)
    {
        Stream stream = client.GetStream();
        var tokenBytes = new byte[ProtocolConstants.DataTokenBytes];
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(ProtocolConstants.TlsTimeout);
            await stream.ReadExactlyAsync(tokenBytes, timeout.Token).ConfigureAwait(false);
        }
        var token = Convert.ToBase64String(tokenBytes);
        if (!_speedTests.TryRemove(token, out var pending) || pending.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _log.Warning("Rejecting speed test channel from {Remote}: invalid token", client.Client.RemoteEndPoint);
            return;
        }

        if (pending.Encrypted)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProtocolConstants.TlsTimeout);
            var (ssl, fingerprint) = await Tls.AuthenticateAsServerAsync(stream, _options.Identity, timeout.Token).ConfigureAwait(false);
            if (!IsPairedWith(fingerprint))
            {
                await ssl.DisposeAsync();
                return;
            }
            stream = ssl;
        }

        // 從收到第一批資料開始計時（不含連線與 TLS 握手的時間）。
        var buffer = new byte[ProtocolConstants.TransferBufferSize];
        long total = 0, first = 0, start = 0, last = 0;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(SpeedTestMaxDuration);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, limit.Token).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (total == 0)
                {
                    first = read;
                    start = Stopwatch.GetTimestamp();
                }
                total += read;
                last = Stopwatch.GetTimestamp();
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            await stream.DisposeAsync();
        }

        var elapsed = Stopwatch.GetElapsedTime(start, last);
        await TrySendAsync(pending.Connection, new SpeedTestResultMessage
        {
            Token = token,
            Bytes = Math.Max(0, total - first),
            Milliseconds = Math.Max(1, (long)elapsed.TotalMilliseconds),
        });
    }
}
