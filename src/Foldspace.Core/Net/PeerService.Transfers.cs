using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using Foldspace.Core.Protocol;
using Foldspace.Core.Transfer;

namespace Foldspace.Core.Net;

public sealed partial class PeerService
{
    private Channel<TransferJob>? _sendQueue;
    private readonly ConcurrentDictionary<Guid, TransferJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<TransferResultMessage>> _resultWaiters = new();
    private readonly ConcurrentDictionary<Guid, PendingReceive> _pendingReceives = new();
    private readonly ConcurrentDictionary<string, Guid> _dataTokens = new(StringComparer.Ordinal);
    private int _receiving;

    /// <summary>新的傳送或接收任務（UI 用來顯示進度視窗）。</summary>
    public event Action<TransferJob>? JobAdded;

    /// <summary>本機因為自己的狀況拒絕了對方的傳輸（傳送端會收到原因，但本機使用者也要知道）。</summary>
    public event Action<IncomingRejection>? IncomingRejected;

    /// <summary>「接收時詢問」：回傳 true 表示接收。由 UI 設定。</summary>
    public Func<IncomingOffer, CancellationToken, Task<bool>>? AskBeforeReceive { get; set; }

    public IReadOnlyCollection<TransferJob> Jobs => _jobs.Values.ToList();

    // ================= 傳送 =================

    /// <summary>
    /// 把拖入的項目排入傳送佇列。對方離線、未配對或服務停用時不排隊，直接丟
    /// <see cref="TransferRejectedException"/>，<see cref="TransferRejectedException.Note"/> 說明原因。
    /// </summary>
    public TransferJob Send(IReadOnlyList<string> paths)
    {
        var status = Status;
        if (status.State != PeerState.Connected)
        {
            throw new TransferRejectedException(status.State switch
            {
                PeerState.Disabled => new JobNote(JobIssue.ServiceDisabled),
                PeerState.NotConfigured => new JobNote(JobIssue.NotConfigured),
                PeerState.Unpaired => new JobNote(JobIssue.NotPaired),
                PeerState.Error => new JobNote(JobIssue.PeerError) { PeerIssue = status.Issue },
                _ => new JobNote(JobIssue.PeerOffline),
            });
        }

        if (paths.Count == 0)
            throw new TransferRejectedException(new JobNote(JobIssue.NothingToSend));
        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            if (string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase))
                throw new TransferRejectedException(new JobNote(JobIssue.DriveRoot));
        }

        var firstName = Path.GetFileName(Path.TrimEndingDirectorySeparator(paths[0]));
        var job = new TransferJob(Guid.NewGuid(), TransferDirection.Send, firstName, paths.Count)
        {
            SourcePaths = paths.ToList(),
            PeerHostname = status.PeerHostname,
        };

        _jobs[job.JobId] = job;
        JobAdded?.Invoke(job);
        if (_sendQueue is null || !_sendQueue.Writer.TryWrite(job))
            job.SetState(TransferJobState.Failed, new JobNote(JobIssue.Cancelled) { Cause = CancelReason.ServiceStopped });
        return job;
    }

    /// <summary>同一方向一次只跑一個任務，後拖入的排隊。</summary>
    private async Task SendWorkerAsync(CancellationToken ct)
    {
        var queue = _sendQueue!;
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                await RunSendJobAsync(job, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        // 停止時把還在排隊的任務標成取消。
        while (queue.Reader.TryRead(out var leftover))
            leftover.SetState(TransferJobState.Cancelled, new JobNote(JobIssue.Cancelled) { Cause = leftover.CancelReason ?? CancelReason.ServiceStopped });
    }

    private async Task RunSendJobAsync(TransferJob job, CancellationToken serviceCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(job.CancellationToken, serviceCt);
        var ct = linked.Token;
        var resultWaiter = new TaskCompletionSource<TransferResultMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _resultWaiters[job.JobId] = resultWaiter;
        ControlConnection? connection = null;
        var cancelSent = false;

        try
        {
            ct.ThrowIfCancellationRequested();
            job.SetState(TransferJobState.Preparing);
            var scan = await Task.Run(() => TransferScanner.Scan(job.SourcePaths, ct), ct).ConfigureAwait(false);
            job.FileCount = scan.FileCount;
            job.TotalBytes = scan.TotalBytes;
            job.LocalSkipped = scan.Skipped;
            job.ItemName = scan.Items[0].Name;
            job.ItemCount = scan.Items.Count;

            lock (_gate)
                connection = _status.State == PeerState.Connected ? _outgoing : null;
            if (connection is null)
                throw new TransferRejectedException(new JobNote(JobIssue.PeerOffline));

            job.SetState(TransferJobState.WaitingForPeer);
            var reply = await connection.RequestAsync(new TransferOfferMessage
            {
                JobId = job.JobId,
                Items = scan.Items,
                FileCount = scan.FileCount,
                DirectoryCount = scan.DirectoryCount,
                TotalBytes = scan.TotalBytes,
            }, ProtocolConstants.AskBeforeReceiveTimeout + TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);

            var accept = reply switch
            {
                TransferAcceptMessage a => a,
                TransferRejectMessage r => throw new TransferRejectedException(new JobNote(JobIssue.Rejected)
                {
                    Rejection = r.Reason,
                    RequiredBytes = r.RequiredBytes ?? 0,
                    AvailableBytes = r.AvailableBytes ?? 0,
                }),
                _ => throw new ProtocolException("Unexpected reply to TRANSFER_OFFER"),
            };

            _log.Information("Job {JobId} sending: {Name} ({Items} item(s)), {Files} file(s), {Bytes} bytes, encrypted {Encrypted}",
                job.JobId, job.ItemName, job.ItemCount, scan.FileCount, scan.TotalBytes, accept.Encrypted);
            job.SetState(TransferJobState.Transferring);

            var (tcp, stream) = await OpenDataChannelAsync(job.JobId, accept, ct).ConfigureAwait(false);
            using (tcp)
            await using (stream)
            {
                try
                {
                    await new TransferSender(job, scan, _log).SendAsync(stream, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (job.IsCancellationRequested && !IsFromPeer(job.CancelReason))
                {
                    // 先在控制通道通知取消，再關閉資料通道：接收端才會把它當成「取消」而不是「斷線」。
                    await TrySendAsync(connection, new TransferCancelMessage { JobId = job.JobId, Reason = ToWireReason(job.CancelReason) });
                    cancelSent = true;
                    throw;
                }
            }

            job.SetState(TransferJobState.Finalizing);
            var result = await resultWaiter.Task.WaitAsync(ProtocolConstants.TransferResultTimeout, ct).ConfigureAwait(false);
            job.Result = result;
            CompleteSendJob(job, result);
        }
        catch (OperationCanceledException) when (job.IsCancellationRequested || serviceCt.IsCancellationRequested)
        {
            var reason = job.CancelReason ?? CancelReason.ServiceStopped;
            if (connection is not null && !cancelSent && !IsFromPeer(reason))
                await TrySendAsync(connection, new TransferCancelMessage { JobId = job.JobId, Reason = ToWireReason(reason) });
            FinishStoppedSend(job, reason);
        }
        catch (TransferRejectedException ex)
        {
            _log.Information("Job {JobId} not started: {Reason}", job.JobId, ex.Note);
            job.SetState(TransferJobState.Failed, ex.Note);
        }
        catch (TimeoutException)
        {
            _log.Warning("Job {JobId}: peer did not respond", job.JobId);
            job.SetState(TransferJobState.Failed, new JobNote(JobIssue.NoResponse));
        }
        catch (Exception ex) when (ex is IOException or SocketException or ProtocolException or ObjectDisposedException
                                       or System.Security.Authentication.AuthenticationException)
        {
            // 對方取消時，資料通道可能比控制通道上的取消訊息早一點斷開。
            if (job.CancelReason is null)
                await WaitForCancelAsync(job, TimeSpan.FromMilliseconds(500));
            if (job.CancelReason is { } cause)
            {
                FinishStoppedSend(job, cause); // 預期內的情況：一行就好，不記堆疊。
            }
            else
            {
                _log.Warning("Job {JobId} interrupted: {Error}", job.JobId, NetworkError.Describe(ex));
                job.SetState(TransferJobState.Failed, new JobNote(JobIssue.ConnectionLost));
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Job {JobId} failed unexpectedly", job.JobId);
            job.SetState(TransferJobState.Failed, new JobNote(JobIssue.Unexpected) { Detail = ex.Message });
        }
        finally
        {
            _resultWaiters.TryRemove(job.JobId, out _);
        }
    }

    /// <summary>傳送被取消或中斷：有人主動取消顯示「已取消」，對方離線等原因顯示「失敗」。</summary>
    private void FinishStoppedSend(TransferJob job, CancelReason reason)
    {
        var note = new JobNote(JobIssue.Cancelled) { Cause = reason };
        if (IsDeliberate(reason))
        {
            _log.Information("Job {JobId} cancelled: {Reason}", job.JobId, reason.ToLogText());
            job.SetState(TransferJobState.Cancelled, note);
        }
        else
        {
            _log.Warning("Job {JobId} interrupted: {Reason}", job.JobId, reason.ToLogText());
            job.SetState(TransferJobState.Failed, note);
        }
    }

    private void CompleteSendJob(TransferJob job, TransferResultMessage result)
    {
        var problems = result.ProblemCount + job.LocalSkipped.Count;
        _log.Information("Job {JobId} finished: {Ok}/{Expected} succeeded, {Problems} problem(s), interrupted {Interrupted}",
            job.JobId, result.SucceededFileCount, result.ExpectedFileCount, problems,
            result.InterruptReason is { } r ? FromPeerPerspective(r).ToLogText() : "no");

        // 「略過」策略造成的未傳送不算失敗。
        var anySkipped = result.Problems.Any(p => p.Outcome == FileOutcome.Skipped);
        if (result.Interrupted)
            job.SetState(TransferJobState.Failed, new JobNote(JobIssue.Interrupted)
            {
                Cause = FromPeerPerspective(result.InterruptReason ?? CancelReason.ConnectionLost),
                Count = result.SucceededFileCount,
            });
        else if (result.SucceededFileCount == 0 && result.ExpectedFileCount > 0 && !anySkipped)
            job.SetState(TransferJobState.Failed, new JobNote(JobIssue.AllFailed));
        else
            job.SetState(TransferJobState.Completed, problems == 0 ? null : new JobNote(JobIssue.SomeProblems) { Count = problems });
    }

    private async Task<(TcpClient Tcp, Stream Stream)> OpenDataChannelAsync(Guid jobId, TransferAcceptMessage accept, CancellationToken ct)
    {
        var endpoint = PeerEndpoint ?? throw new IOException("The peer address is not set");
        if (accept.Encrypted)
        {
            var tcp = await Tcp.ConnectAsync(endpoint, ProtocolConstants.DataChannelTls, ct).ConfigureAwait(false);
            try
            {
                var (ssl, fingerprint) = await Tls.AuthenticateAsClientAsync(tcp.GetStream(), _options.Identity, ct).ConfigureAwait(false);
                if (!IsPairedWith(fingerprint))
                {
                    await ssl.DisposeAsync();
                    throw new TransferRejectedException(new JobNote(JobIssue.PairingMismatch));
                }
                await ssl.WriteAsync(jobId.ToByteArray(), ct).ConfigureAwait(false);
                return (tcp, ssl);
            }
            catch
            {
                tcp.Dispose();
                throw;
            }
        }
        else
        {
            byte[] token;
            try
            {
                token = Convert.FromBase64String(accept.DataToken ?? "");
            }
            catch (FormatException)
            {
                throw new ProtocolException("Malformed data channel token");
            }
            if (token.Length != ProtocolConstants.DataTokenBytes)
                throw new ProtocolException("Data channel token has the wrong length");

            var tcp = await Tcp.ConnectAsync(endpoint, ProtocolConstants.DataChannelPlain, ct).ConfigureAwait(false);
            try
            {
                var stream = tcp.GetStream();
                await stream.WriteAsync(token, ct).ConfigureAwait(false);
                return (tcp, stream);
            }
            catch
            {
                tcp.Dispose();
                throw;
            }
        }
    }

    // ================= 停止與取消 =================

    public void CancelAllTransfers(CancelReason reason)
    {
        foreach (var job in _jobs.Values)
            job.Cancel(reason);
        foreach (var pending in _pendingReceives.Values)
            pending.DataArrived.TrySetCanceled();
    }

    private void CancelTransfers(TransferDirection direction, CancelReason reason)
    {
        foreach (var job in _jobs.Values.Where(j => j.Direction == direction))
            job.Cancel(reason);
    }

    private TransferJob? FindJob(Guid jobId) => _jobs.TryGetValue(jobId, out var job) ? job : null;

    /// <summary>等對方的取消訊息到達（最多 <paramref name="timeout"/>），已取消就立即返回。</summary>
    private static async Task WaitForCancelAsync(TransferJob job, TimeSpan timeout)
    {
        try { await Task.Delay(timeout, job.CancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    /// <summary>接收中 <see cref="ProtocolConstants.DataStallTimeout"/> 完全沒有資料就中斷任務。</summary>
    private async Task WatchForStallAsync(TransferJob job, CancellationToken ct)
    {
        var lastBytes = job.BytesTransferred;
        var lastProgress = Environment.TickCount64;
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                var bytes = job.BytesTransferred;
                if (bytes != lastBytes)
                {
                    lastBytes = bytes;
                    lastProgress = Environment.TickCount64;
                }
                else if (Environment.TickCount64 - lastProgress >= ProtocolConstants.DataStallTimeout.TotalMilliseconds)
                {
                    _log.Warning("Job {JobId}: no data for {Seconds} s", job.JobId, ProtocolConstants.DataStallTimeout.TotalSeconds);
                    job.Cancel(CancelReason.ConnectionLost);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>不是本機主動取消（對方造成或連線中斷），不需要再通知對方。</summary>
    private static bool IsFromPeer(CancelReason? reason) =>
        reason is CancelReason.Peer or CancelReason.PeerSecurityViolation or CancelReason.PeerDiskFull or CancelReason.PeerOffline
            or CancelReason.ConnectionLost;

    /// <summary>有人主動取消（介面顯示「已取消」）；其他原因都是中斷（顯示「失敗」）。</summary>
    internal static bool IsDeliberate(CancelReason reason) =>
        reason is CancelReason.User or CancelReason.Peer or CancelReason.ServiceStopped or CancelReason.Unpaired;

    /// <summary>送給對方的取消原因：網路上只用「從送出者角度」的幾個代碼。</summary>
    private static CancelReason ToWireReason(CancelReason? reason) => reason switch
    {
        CancelReason.SecurityViolation or CancelReason.DiskFull or CancelReason.CleanupFailed => reason.Value,
        CancelReason.User => CancelReason.User,
        _ => CancelReason.ServiceStopped,
    };

    /// <summary>對方送來的原因是「對方的角度」，換成本機的角度（對方的使用者取消 → 對方取消）。</summary>
    internal static CancelReason FromPeerPerspective(CancelReason reason) => reason switch
    {
        CancelReason.User or CancelReason.ServiceStopped or CancelReason.Unpaired => CancelReason.Peer,
        CancelReason.Peer => CancelReason.User,
        CancelReason.SecurityViolation => CancelReason.PeerSecurityViolation,
        CancelReason.DiskFull => CancelReason.PeerDiskFull,
        CancelReason.PeerSecurityViolation => CancelReason.SecurityViolation,
        CancelReason.PeerDiskFull => CancelReason.DiskFull,
        CancelReason.PeerOffline => CancelReason.ConnectionLost,
        _ => reason,
    };

    /// <summary>移除已結束的任務（進度視窗清空時呼叫）。</summary>
    public void ForgetFinishedJobs()
    {
        foreach (var job in _jobs.Values.Where(j => j.IsFinished))
            _jobs.TryRemove(job.JobId, out _);
    }

    // ================= 接收 =================

    private sealed class PendingReceive(TransferJob job, TransferOfferMessage offer, ControlConnection connection, string? token)
    {
        public TransferJob Job { get; } = job;
        public TransferOfferMessage Offer { get; } = offer;
        public ControlConnection Connection { get; } = connection;
        public string? Token { get; } = token;
        public DateTimeOffset ExpiresAt { get; } = DateTimeOffset.UtcNow + ProtocolConstants.DataTokenLifetime;
        public TaskCompletionSource<(TcpClient Tcp, Stream Stream)> DataArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>依 jobId（加密）或 token（未加密）取出等待中的接收任務；token 用過即失效。</summary>
    private PendingReceive? TakePendingReceive(Guid? jobId, string? token)
    {
        if (token is not null)
        {
            if (!_dataTokens.TryRemove(token, out var id))
                return null;
            jobId = id;
        }

        if (jobId is not { } key || !_pendingReceives.TryRemove(key, out var pending))
            return null;
        if (pending.Token is not null && token is null)
            _dataTokens.TryRemove(pending.Token, out _);
        return DateTimeOffset.UtcNow <= pending.ExpiresAt ? pending : null;
    }

    private async Task HandleOfferAsync(ControlConnection connection, TransferOfferMessage offer)
    {
        Task Reject(RejectReason reason, long? required = null, long? available = null)
        {
            _log.Information("Rejecting job {JobId}: {Reason}", offer.JobId, reason);
            return TrySendAsync(connection, new TransferRejectMessage
            {
                ReplyTo = offer.Id,
                JobId = offer.JobId,
                Reason = reason,
                RequiredBytes = required,
                AvailableBytes = available,
            });
        }

        if (!IsPairedWith(connection.RemoteFingerprint))
        {
            await Reject(RejectReason.NotPaired);
            return;
        }

        if (!IsValidOffer(offer))
        {
            _log.Warning("Security warning: job {JobId} has invalid top-level names", offer.JobId);
            await Reject(RejectReason.InvalidOffer);
            return;
        }

        if (Interlocked.CompareExchange(ref _receiving, 1, 0) != 0)
        {
            await Reject(RejectReason.Busy);
            return;
        }

        try
        {
            var options = _receiveOptions();
            var hostname = connection.RemoteHello?.Hostname ?? connection.RemoteEndPoint?.Address.ToString() ?? "?";
            if (!PrepareReceiveFolder(options.ReceiveFolder))
            {
                await Reject(RejectReason.ReceiveFolderUnavailable);
                IncomingRejected?.Invoke(new IncomingRejection(hostname, RejectReason.ReceiveFolderUnavailable));
                return;
            }

            var available = TryGetFreeSpace(options.ReceiveFolder);
            var required = offer.TotalBytes + ProtocolConstants.FreeSpaceMarginBytes;
            if (available is { } free && free < required)
            {
                await Reject(RejectReason.InsufficientSpace, required, free);
                IncomingRejected?.Invoke(new IncomingRejection(hostname, RejectReason.InsufficientSpace, required, free));
                return;
            }

            if (options.AskBeforeReceive && AskBeforeReceive is { } ask)
            {
                using var timeout = new CancellationTokenSource(ProtocolConstants.AskBeforeReceiveTimeout);
                bool accepted;
                try
                {
                    accepted = await ask(new IncomingOffer(hostname, offer.Items[0].Name, offer.Items.Count, offer.FileCount, offer.TotalBytes), timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    accepted = false;
                }
                if (!accepted)
                {
                    await Reject(timeout.IsCancellationRequested ? RejectReason.DeclineTimeout : RejectReason.Declined);
                    return;
                }
            }

            var job = new TransferJob(offer.JobId, TransferDirection.Receive, offer.Items[0].Name, offer.Items.Count)
            {
                FileCount = offer.FileCount,
                TotalBytes = offer.TotalBytes,
                PeerHostname = hostname,
            };

            // 只要任一端開啟加密就加密。
            var encrypted = _options.Encryption || connection.RemoteHello?.Encryption != false;
            string? token = encrypted ? null : Convert.ToBase64String(RandomNumberGenerator.GetBytes(ProtocolConstants.DataTokenBytes));
            var pending = new PendingReceive(job, offer, connection, token);
            _pendingReceives[offer.JobId] = pending;
            if (token is not null)
                _dataTokens[token] = offer.JobId;

            _jobs[job.JobId] = job;
            JobAdded?.Invoke(job);
            job.SetState(TransferJobState.WaitingForPeer);

            await connection.SendAsync(new TransferAcceptMessage
            {
                ReplyTo = offer.Id,
                JobId = offer.JobId,
                Encrypted = encrypted,
                DataToken = token,
            }).ConfigureAwait(false);

            (TcpClient Tcp, Stream Stream) data;
            try
            {
                data = await pending.DataArrived.Task.WaitAsync(ProtocolConstants.DataTokenLifetime, job.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                _pendingReceives.TryRemove(offer.JobId, out _);
                if (token is not null)
                    _dataTokens.TryRemove(token, out _);
                job.SetState(ex is TimeoutException ? TransferJobState.Failed : TransferJobState.Cancelled,
                    ex is TimeoutException
                        ? new JobNote(JobIssue.DataChannelTimeout)
                        : new JobNote(JobIssue.Cancelled) { Cause = job.CancelReason ?? CancelReason.ServiceStopped });
                return;
            }

            await RunReceiveAsync(pending, data.Tcp, data.Stream, options).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            _log.Warning(ex, "Connection lost while handling job {JobId}", offer.JobId);
            _pendingReceives.TryRemove(offer.JobId, out _);
        }
        finally
        {
            Interlocked.Exchange(ref _receiving, 0);
        }
    }

    private static bool IsValidOffer(TransferOfferMessage offer) =>
        offer.Items.Count > 0 &&
        offer.TotalBytes >= 0 && offer.FileCount >= 0 &&
        offer.Items.All(i => PathSafety.IsValidTopLevelName(i.Name) && i.Bytes >= 0 && i.FileCount >= 0) &&
        offer.Items.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == offer.Items.Count;

    /// <summary>接收資料夾不存在就建立，並確認可寫入。</summary>
    private bool PrepareReceiveFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probeDir = TempArea.Root(folder);
            Directory.CreateDirectory(probeDir);
            var probe = Path.Combine(probeDir, $"probe-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            _log.Error(ex, "Receive folder is not usable: {Folder}", folder);
            return false;
        }
    }

    private static long? TryGetFreeSpace(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task RunReceiveAsync(PendingReceive pending, TcpClient tcp, Stream stream, ReceiveOptions options)
    {
        var job = pending.Job;
        var offer = pending.Offer;
        var serviceCt = _serviceToken;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(job.CancellationToken, serviceCt);

        var tempDir = TempArea.CreateJobDir(options.ReceiveFolder, offer.JobId);
        var receiver = new TransferReceiver(job, offer, tempDir, _log);
        CancelReason? interruptReason = null;
        job.SetState(TransferJobState.Transferring);
        _log.Information("Job {JobId} receiving: {Name} ({Items} item(s)), {Files} file(s), {Bytes} bytes",
            offer.JobId, job.ItemName, job.ItemCount, offer.FileCount, offer.TotalBytes);

        // 取消時：畫面立即顯示「已取消」，並直接關閉資料通道讓進行中的讀寫立刻失敗。
        // 關閉檔案、刪除暫存在背景完成（慢的磁碟上可能要好幾秒，不能讓使用者等）。
        await using var closeOnCancel = linked.Token.Register(() =>
        {
            var cause = job.CancelReason ?? CancelReason.ServiceStopped;
            job.SetState(IsDeliberate(cause) ? TransferJobState.Cancelled : TransferJobState.Failed,
                new JobNote(JobIssue.Cancelled) { Cause = cause });
            try { stream.Dispose(); } catch { /* 已經關閉 */ }
        });

        // 送出這個任務的控制連線斷了 = 對方離線。資料連線可能是半開的（對方關閉時 FIN 沒送到），
        // 只讀不寫的接收端永遠等不到資料，所以直接中斷。另外資料停太久也中斷。
        using var receiving = new CancellationTokenSource();
        _ = pending.Connection.Completion.ContinueWith(_ => job.Cancel(CancelReason.PeerOffline),
            receiving.Token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _ = WatchForStallAsync(job, receiving.Token);
        try
        {
            await receiver.ReceiveAsync(stream, linked.Token).ConfigureAwait(false);
        }
        catch (SecurityViolationException ex)
        {
            _log.Warning("Security warning: job {JobId} received an unsafe path; aborting the whole job: {Detail}", offer.JobId, ex.Message);
            await AbortReceiveAsync(pending, tempDir, CancelReason.SecurityViolation);
            return;
        }
        catch (DiskFullException)
        {
            _log.Warning("Job {JobId}: disk full", offer.JobId);
            await AbortReceiveAsync(pending, tempDir, CancelReason.DiskFull);
            return;
        }
        catch (OperationCanceledException)
        {
            interruptReason = job.CancelReason ?? CancelReason.ServiceStopped;
            if (!IsFromPeer(job.CancelReason)) // 本機取消，通知傳送端
                await TrySendAsync(pending.Connection, new TransferCancelMessage { JobId = offer.JobId, Reason = ToWireReason(job.CancelReason) });
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or ObjectDisposedException or SocketException)
        {
            // 傳送端取消時，控制通道上的取消訊息可能比資料通道的關閉晚一點到。
            if (job.CancelReason is null)
                await WaitForCancelAsync(job, TimeSpan.FromMilliseconds(500));
            interruptReason = job.CancelReason ?? CancelReason.ConnectionLost;
            if (job.CancelReason is { } local && !IsFromPeer(local)) // 本機取消，通知傳送端
                await TrySendAsync(pending.Connection, new TransferCancelMessage { JobId = offer.JobId, Reason = ToWireReason(local) });
            // 取消時關閉資料通道是預期的，不另外記錄；真正的網路錯誤記下原因。
            if (job.CancelReason is null)
                _log.Warning("Job {JobId} receive interrupted: {Error}", offer.JobId, NetworkError.Describe(ex));
        }
        finally
        {
            receiving.Cancel();
            await stream.DisposeAsync();
            tcp.Dispose();
        }

        // 有人取消是預期內的（INF）；對方離線、連線中斷是中斷（WRN）。
        if (interruptReason is { } stopped)
            _log.Write(IsDeliberate(stopped) ? Serilog.Events.LogEventLevel.Information : Serilog.Events.LogEventLevel.Warning,
                "Job {JobId} stopped ({Reason}); cleaning up temp files", offer.JobId, stopped.ToLogText());

        // 已驗證的檔案仍移到正式位置。整理前先刪掉所有沒通過驗證的檔案，
        // 接收資料夾裡永遠不會出現不完整的檔案；刪不掉就整個不整理。
        job.SetState(TransferJobState.Finalizing);
        try
        {
            TempArea.RemoveUnverified(tempDir, receiver.VerifiedFiles, _log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TempArea.DeleteJobDir(tempDir, _log);
            await TrySendAsync(pending.Connection, new TransferCancelMessage { JobId = offer.JobId, Reason = CancelReason.CleanupFailed });
            job.SetState(TransferJobState.Failed, new JobNote(JobIssue.Aborted) { Cause = CancelReason.CleanupFailed });
            return;
        }

        FinalizeResult finalized;
        try
        {
            finalized = TransferFinalizer.Finalize(tempDir, options.ReceiveFolder, offer.Items, options.ConflictPolicy, receiver.Directories, _log);
        }
        finally
        {
            TempArea.DeleteJobDir(tempDir, _log);
        }

        var problems = receiver.FailedFiles
            .Select(kv => new FileResult(kv.Key, FileOutcome.Failed, Issue: kv.Value))
            .Concat(finalized.Problems)
            .ToList();
        var result = new TransferResultMessage
        {
            JobId = offer.JobId,
            ExpectedFileCount = offer.FileCount,
            SucceededFileCount = finalized.MovedFileCount,
            ReceivedBytes = receiver.ReceivedBytes,
            Interrupted = interruptReason is not null,
            InterruptReason = interruptReason,
            TopLevel = finalized.TopLevel,
            Problems = problems.Take(TransferResultMessage.MaxProblems).ToList(),
            ProblemCount = problems.Count,
        };
        await TrySendAsync(pending.Connection, result);

        job.Result = result;
        job.FirstFinalPath = finalized.FirstFinalPath;
        _log.Information("Job {JobId} receive finished: {Ok}/{Expected} succeeded, {Problems} problem(s), interrupted {Reason}",
            offer.JobId, result.SucceededFileCount, result.ExpectedFileCount, result.ProblemCount, interruptReason.ToLogText());
        foreach (var problem in problems.Take(TransferResultMessage.MaxProblems))
            _log.Information("  {Outcome} {Path}: {Issue}", problem.Outcome, problem.Path, problem.Issue);

        if (interruptReason is not null)
            job.SetState(IsDeliberate(interruptReason.Value) ? TransferJobState.Cancelled : TransferJobState.Failed,
                new JobNote(JobIssue.Interrupted) { Cause = interruptReason, Count = result.SucceededFileCount });
        else
            job.SetState(TransferJobState.Completed, problems.Count == 0 ? null : new JobNote(JobIssue.SomeProblems) { Count = problems.Count });
    }

    private async Task AbortReceiveAsync(PendingReceive pending, string tempDir, CancelReason reason)
    {
        TempArea.DeleteJobDir(tempDir, _log);
        await TrySendAsync(pending.Connection, new TransferCancelMessage { JobId = pending.Offer.JobId, Reason = reason });
        pending.Job.SetState(TransferJobState.Failed, new JobNote(JobIssue.Aborted) { Cause = reason });
    }
}
