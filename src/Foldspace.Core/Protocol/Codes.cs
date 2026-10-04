namespace Foldspace.Core.Protocol;

// 透過網路傳送的狀態一律用這些代碼，不傳任何顯示用的文字；
// 收到的一方依自己的介面語言顯示。JSON 中以 camelCase 字串表示（例如 "insufficientSpace"）。

/// <summary>接收端拒絕傳輸的原因（TRANSFER_REJECT）。</summary>
public enum RejectReason
{
    NotPaired,
    Busy,
    Declined,
    /// <summary>「接收前先詢問」60 秒內沒有回應。</summary>
    DeclineTimeout,
    ReceiveFolderUnavailable,
    InsufficientSpace,
    InvalidOffer,
}

/// <summary>
/// 傳輸被取消或中斷的原因。網路上（TRANSFER_CANCEL、TRANSFER_RESULT）只會出現
/// <see cref="User"/>、<see cref="SecurityViolation"/>、<see cref="DiskFull"/>、<see cref="CleanupFailed"/>、<see cref="ServiceStopped"/>；
/// 其餘是本機用來描述任務結束原因的。
/// </summary>
public enum CancelReason
{
    /// <summary>本機使用者按了取消。</summary>
    User,
    /// <summary>對方取消。</summary>
    Peer,
    /// <summary>對方因為收到不安全的路徑而中斷。</summary>
    PeerSecurityViolation,
    /// <summary>對方磁碟寫滿而中斷。</summary>
    PeerDiskFull,
    /// <summary>服務停用、程式結束或電腦進入睡眠。</summary>
    ServiceStopped,
    /// <summary>對方離線（控制通道中斷）。</summary>
    PeerOffline,
    /// <summary>配對被解除。</summary>
    Unpaired,
    /// <summary>資料通道中斷。</summary>
    ConnectionLost,
    /// <summary>收到不安全的檔案路徑。</summary>
    SecurityViolation,
    /// <summary>本機磁碟寫滿。</summary>
    DiskFull,
    /// <summary>無法清理未完成的暫存檔。</summary>
    CleanupFailed,
}

/// <summary>單一檔案沒有成功的原因（TRANSFER_RESULT 的檔案清單、資料通道的放棄紀錄）。</summary>
public enum FileIssue
{
    NotFound,
    /// <summary>符號連結或 junction，不會傳送。</summary>
    ReparsePoint,
    FolderUnreadable,
    NoReadPermission,
    /// <summary>被其他程式鎖定或無法開啟。</summary>
    Locked,
    ReadFailed,
    /// <summary>傳送中來源檔案被修改（大小改變）。</summary>
    SourceModified,
    /// <summary>名稱在 Windows 上不合法（保留名稱、結尾為空白或句點）。</summary>
    InvalidName,
    HashMismatch,
    WriteFailed,
    /// <summary>「略過」策略：接收資料夾已有同名項目。</summary>
    SkippedExisting,
    /// <summary>接收資料夾已有同名資料夾，無法放入檔案。</summary>
    FolderExists,
    /// <summary>接收資料夾已有同名檔案，無法建立資料夾。</summary>
    FileExists,
    /// <summary>舊檔案唯讀或被鎖定，無法覆蓋。</summary>
    OverwriteFailed,
    MoveFailed,
}

/// <summary>控制通道的錯誤（ERROR）。</summary>
public enum ErrorCode
{
    VersionIncompatible,
    /// <summary>回應端記錄的配對設備不是發起端。</summary>
    PairingMismatch,
    /// <summary>回應端尚未配對，而發起端不是它設定的對方 IP。</summary>
    NotAllowed,
}

/// <summary>配對被拒絕的原因（PAIR_REJECT）。</summary>
public enum PairRejectReason
{
    Busy,
    UserRejected,
    InvalidRequest,
    VerificationFailed,
}

/// <summary>日誌用的英文說明（介面文字在 Foldspace.Localization）。</summary>
public static class CodeLogText
{
    /// <summary>從本機角度描述中斷原因；<c>null</c> = 沒有中斷。</summary>
    public static string ToLogText(this CancelReason? reason) => reason switch
    {
        null => "no",
        CancelReason.User => "cancelled by local user",
        CancelReason.Peer => "cancelled by peer",
        CancelReason.PeerSecurityViolation => "peer aborted after receiving an unsafe path",
        CancelReason.PeerDiskFull => "peer disk full",
        CancelReason.ServiceStopped => "local service stopped",
        CancelReason.PeerOffline => "peer went offline",
        CancelReason.Unpaired => "unpaired",
        CancelReason.ConnectionLost => "connection lost",
        CancelReason.SecurityViolation => "unsafe path received",
        CancelReason.DiskFull => "local disk full",
        CancelReason.CleanupFailed => "temp files could not be cleaned up",
        _ => reason.ToString()!,
    };

    public static string ToLogText(this CancelReason reason) => ((CancelReason?)reason).ToLogText();
}
