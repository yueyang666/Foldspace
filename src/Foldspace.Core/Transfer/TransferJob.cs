using Foldspace.Core.Net;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Transfer;

public enum TransferDirection
{
    Send,
    Receive,
}

public enum TransferJobState
{
    Queued,
    Preparing,
    WaitingForPeer,
    Transferring,
    Finalizing,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>任務沒有順利完成（或完成但有狀況）的原因。</summary>
public enum JobIssue
{
    // ---- 傳送前就無法進行 ----
    /// <summary>沒有可傳送的項目；<see cref="JobNote.FileIssue"/> 與 <see cref="JobNote.Path"/> 說明第一個被略過的項目。</summary>
    NothingToSend,
    /// <summary>拖入了整個磁碟。</summary>
    DriveRoot,
    ServiceDisabled,
    NotConfigured,
    NotPaired,
    /// <summary>連線狀態是錯誤（<see cref="JobNote.PeerIssue"/>）。</summary>
    PeerError,
    PeerOffline,

    // ---- 對方拒絕（<see cref="JobNote.Rejection"/>）----
    Rejected,

    // ---- 傳輸過程 ----
    NoResponse,
    ConnectionLost,
    PairingMismatch,
    /// <summary>對方接受後沒有開始傳送。</summary>
    DataChannelTimeout,
    /// <summary>任務被取消（<see cref="JobNote.Cause"/>）。</summary>
    Cancelled,
    /// <summary>中斷但保留了 <see cref="JobNote.Count"/> 個已完成的檔案（<see cref="JobNote.Cause"/>）。</summary>
    Interrupted,
    /// <summary>整個任務失敗（<see cref="JobNote.Cause"/>：不安全的路徑、磁碟寫滿、無法清理暫存）。</summary>
    Aborted,

    // ---- 完成但有狀況 ----
    AllFailed,
    /// <summary><see cref="JobNote.Count"/> 個項目沒有傳送或接收。</summary>
    SomeProblems,

    /// <summary>未預期的錯誤（<see cref="JobNote.Detail"/> 是技術細節）。</summary>
    Unexpected,
}

/// <summary>任務結果的說明。只有代碼與參數，由介面組成當地語言的文字。</summary>
public sealed record JobNote(JobIssue Issue)
{
    public CancelReason? Cause { get; init; }
    public RejectReason? Rejection { get; init; }
    public PeerIssue? PeerIssue { get; init; }
    public FileIssue? FileIssue { get; init; }
    public string? Path { get; init; }
    public int Count { get; init; }
    public long RequiredBytes { get; init; }
    public long AvailableBytes { get; init; }
    public string? Detail { get; init; }

    public override string ToString() => Issue + (Cause is { } c ? $"({c})" : "") + (Rejection is { } r ? $"({r})" : "")
        + (Count > 0 ? $" count={Count}" : "") + (Detail is { } d ? $" {d}" : "");
}

/// <summary>一次拖放 = 一個傳輸任務。UI 訂閱 <see cref="Changed"/> 更新進度。</summary>
public sealed class TransferJob
{
    private readonly CancellationTokenSource _cts = new();
    private long _bytesTransferred;

    internal TransferJob(Guid jobId, TransferDirection direction, string itemName, int itemCount)
    {
        JobId = jobId;
        Direction = direction;
        ItemName = itemName;
        ItemCount = itemCount;
    }

    public Guid JobId { get; }
    public TransferDirection Direction { get; }
    /// <summary>第一個拖入項目的名稱。</summary>
    public string ItemName { get; internal set; }
    /// <summary>拖入的頂層項目數（介面顯示「名稱 等 N 個項目」）。</summary>
    public int ItemCount { get; internal set; }
    public IReadOnlyList<string> SourcePaths { get; internal init; } = [];
    public int FileCount { get; internal set; }
    public long TotalBytes { get; internal set; }
    public long BytesTransferred => Interlocked.Read(ref _bytesTransferred);
    public TransferJobState State { get; private set; } = TransferJobState.Queued;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string? PeerHostname { get; internal set; }

    /// <summary>失敗、取消或完成但有狀況時的原因。</summary>
    public JobNote? Note { get; private set; }

    /// <summary>接收端的最終結果（傳送端由 TRANSFER_RESULT 取得）。</summary>
    public TransferResultMessage? Result { get; internal set; }

    /// <summary>傳送端掃描時就略過的項目。</summary>
    public IReadOnlyList<FileResult> LocalSkipped { get; internal set; } = [];

    /// <summary>接收完成後，第一個頂層項目在接收資料夾中的完整路徑（通知點擊後選取它）。</summary>
    public string? FirstFinalPath { get; internal set; }

    public bool IsFinished => State is TransferJobState.Completed or TransferJobState.Failed or TransferJobState.Cancelled;

    internal CancellationToken CancellationToken => _cts.Token;

    public event Action<TransferJob>? Changed;

    /// <summary>取消的原因（本機使用者、對方取消、服務停止…）。</summary>
    public CancelReason? CancelReason { get; private set; }

    /// <summary>使用者按取消。</summary>
    public void Cancel() => Cancel(Protocol.CancelReason.User);

    internal void Cancel(CancelReason reason)
    {
        if (IsFinished)
            return;
        CancelReason ??= reason;
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    internal bool IsCancellationRequested => _cts.IsCancellationRequested;

    internal void AddBytes(long count)
    {
        Interlocked.Add(ref _bytesTransferred, count);
        ProgressTick();
    }

    internal void SetState(TransferJobState state, JobNote? note = null)
    {
        if (IsFinished)
            return;
        State = state;
        if (note is not null)
            Note = note;
        if (state == TransferJobState.Transferring && StartedAt is null)
            StartedAt = DateTimeOffset.UtcNow;
        if (IsFinished)
            FinishedAt = DateTimeOffset.UtcNow;
        Changed?.Invoke(this);
    }

    private long _lastNotifyTicks;

    /// <summary>進度事件最多每 100 ms 觸發一次，避免小檔案時塞爆 UI。</summary>
    private void ProgressTick()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastNotifyTicks);
        if (now - last < 100 || Interlocked.CompareExchange(ref _lastNotifyTicks, now, last) != last)
            return;
        Changed?.Invoke(this);
    }
}
