using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Serilog;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Net;

public enum ConnectionRole
{
    /// <summary>本機主動連到對方（用來送自己的心跳與請求）。</summary>
    Outgoing,
    /// <summary>對方連進來。</summary>
    Incoming,
}

/// <summary>
/// 一條已完成 TLS 的控制通道。訊息是全雙工的：任何一端都能送訊息，
/// 帶 <c>replyTo</c> 的訊息交給等待中的 <see cref="RequestAsync"/>，其他交給 <see cref="MessageReceived"/>。
/// </summary>
internal sealed class ControlConnection : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ControlMessage>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _log;
    private Task? _readLoop;
    private int _disposed;

    public ControlConnection(TcpClient tcp, Stream stream, ConnectionRole role, string remoteFingerprint, ILogger log)
    {
        _tcp = tcp;
        _stream = stream;
        Role = role;
        RemoteFingerprint = remoteFingerprint;
        RemoteEndPoint = (IPEndPoint?)tcp.Client.RemoteEndPoint;
        _log = log.ForContext("Remote", RemoteEndPoint?.ToString()).ForContext("Role", role);
    }

    public ConnectionRole Role { get; }
    public string RemoteFingerprint { get; }
    public IPEndPoint? RemoteEndPoint { get; }

    /// <summary>對方的 HELLO（入站）或 HELLO_ACK（出站）。</summary>
    public HelloBase? RemoteHello { get; set; }

    public DateTimeOffset LastReceived { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>連線結束（對方關閉、錯誤或本機 Dispose）時完成。</summary>
    public Task Completion => _readLoop ?? Task.CompletedTask;

    public Func<ControlConnection, ControlMessage, Task>? MessageReceived { get; set; }

    public CancellationToken Closing => _cts.Token;

    /// <summary>握手階段（讀迴圈啟動前）直接讀一則訊息。</summary>
    public async Task<ControlMessage> ReadHandshakeAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        linked.CancelAfter(timeout);
        var message = await MessageFraming.ReadAsync(_stream, linked.Token).ConfigureAwait(false)
            ?? throw new IOException("Peer closed the connection during handshake");
        LastReceived = DateTimeOffset.UtcNow;
        return message;
    }

    public void Start() => _readLoop ??= Task.Run(ReadLoopAsync);

    public async Task SendAsync(ControlMessage message, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        await _writeLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await MessageFraming.WriteAsync(_stream, message, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>送出請求並等待帶有對應 <c>replyTo</c> 的回應。逾時丟 <see cref="TimeoutException"/>。</summary>
    public async Task<ControlMessage> RequestAsync(ControlMessage request, TimeSpan timeout, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<ControlMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = tcs;
        try
        {
            await SendAsync(request, ct).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            return await tcs.Task.WaitAsync(timeout, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new IOException("Control channel closed");
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var message = await MessageFraming.ReadAsync(_stream, _cts.Token).ConfigureAwait(false);
                if (message is null)
                    break;
                LastReceived = DateTimeOffset.UtcNow;

                if (message is UnknownMessage unknown)
                {
                    _log.Information("Ignoring unknown control message {Type}", unknown.RawType);
                    continue;
                }

                if (message.ReplyTo is { } replyTo && _pending.TryRemove(replyTo, out var waiter))
                {
                    waiter.TrySetResult(message);
                    continue;
                }

                if (MessageReceived is { } handler)
                {
                    try
                    {
                        await handler(this, message).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error while handling control message {Type}", message.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or ObjectDisposedException or SocketException)
        {
            _log.Information("Control channel ended: {Error}", NetworkError.Describe(ex));
        }
        finally
        {
            foreach (var waiter in _pending.Values)
                waiter.TrySetException(new IOException("Control channel closed"));
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _cts.Cancel();
        try { await _stream.DisposeAsync().ConfigureAwait(false); } catch { }
        _tcp.Dispose();
    }
}

internal static class Tcp
{
    /// <summary>TCP 連線（3 秒逾時）並送出第一個位元組：連線種類。</summary>
    public static async Task<TcpClient> ConnectAsync(IPEndPoint endpoint, byte channelType, CancellationToken ct)
    {
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProtocolConstants.ConnectTimeout);
            try
            {
                await client.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("TCP connect timed out");
            }

            ConfigureSocket(client);
            await client.GetStream().WriteAsync(new[] { channelType }, ct).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public static void ConfigureSocket(TcpClient client)
    {
        client.NoDelay = true;
        client.ReceiveBufferSize = 1024 * 1024;
        client.SendBufferSize = 1024 * 1024;
    }
}
