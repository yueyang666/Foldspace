using System.Text.Json.Serialization;

namespace Foldspace.Core.Protocol;

/// <summary>
/// 控制通道訊息。序列化後的 JSON 一定帶 <c>type</c>、<c>id</c>，回應訊息帶 <c>replyTo</c>。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelloMessage), MessageTypes.Hello)]
[JsonDerivedType(typeof(HelloAckMessage), MessageTypes.HelloAck)]
[JsonDerivedType(typeof(PingMessage), MessageTypes.Ping)]
[JsonDerivedType(typeof(PongMessage), MessageTypes.Pong)]
[JsonDerivedType(typeof(PairRequestMessage), MessageTypes.PairRequest)]
[JsonDerivedType(typeof(PairNonceMessage), MessageTypes.PairNonce)]
[JsonDerivedType(typeof(PairRevealMessage), MessageTypes.PairReveal)]
[JsonDerivedType(typeof(PairAcceptMessage), MessageTypes.PairAccept)]
[JsonDerivedType(typeof(PairRejectMessage), MessageTypes.PairReject)]
[JsonDerivedType(typeof(UnpairMessage), MessageTypes.Unpair)]
[JsonDerivedType(typeof(TransferOfferMessage), MessageTypes.TransferOffer)]
[JsonDerivedType(typeof(TransferAcceptMessage), MessageTypes.TransferAccept)]
[JsonDerivedType(typeof(TransferRejectMessage), MessageTypes.TransferReject)]
[JsonDerivedType(typeof(TransferCancelMessage), MessageTypes.TransferCancel)]
[JsonDerivedType(typeof(TransferResultMessage), MessageTypes.TransferResult)]
[JsonDerivedType(typeof(ErrorMessage), MessageTypes.Error)]
[JsonDerivedType(typeof(SpeedTestRequestMessage), MessageTypes.SpeedTestRequest)]
[JsonDerivedType(typeof(SpeedTestReadyMessage), MessageTypes.SpeedTestReady)]
[JsonDerivedType(typeof(SpeedTestResultMessage), MessageTypes.SpeedTestResult)]
public abstract record ControlMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? ReplyTo { get; init; }
}

public static class MessageTypes
{
    public const string Hello = "HELLO";
    public const string HelloAck = "HELLO_ACK";
    public const string Ping = "PING";
    public const string Pong = "PONG";
    public const string PairRequest = "PAIR_REQUEST";
    public const string PairNonce = "PAIR_NONCE";
    public const string PairReveal = "PAIR_REVEAL";
    public const string PairAccept = "PAIR_ACCEPT";
    public const string PairReject = "PAIR_REJECT";
    public const string Unpair = "UNPAIR";
    public const string TransferOffer = "TRANSFER_OFFER";
    public const string TransferAccept = "TRANSFER_ACCEPT";
    public const string TransferReject = "TRANSFER_REJECT";
    public const string TransferCancel = "TRANSFER_CANCEL";
    public const string TransferResult = "TRANSFER_RESULT";
    public const string Error = "ERROR";
    public const string SpeedTestRequest = "SPEED_TEST_REQUEST";
    public const string SpeedTestReady = "SPEED_TEST_READY";
    public const string SpeedTestResult = "SPEED_TEST_RESULT";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        Hello, HelloAck, Ping, Pong, PairRequest, PairNonce, PairReveal, PairAccept, PairReject, Unpair,
        TransferOffer, TransferAccept, TransferReject, TransferCancel, TransferResult, Error,
        SpeedTestRequest, SpeedTestReady, SpeedTestResult,
    };
}

/// <summary>收到不認得的 type（例如對方是較新的次版本）。不斷線，只記錄。</summary>
public sealed record UnknownMessage(string RawType) : ControlMessage;

/// <summary>HELLO 與 HELLO_ACK 共用的欄位。兩者刻意不互相繼承，避免 pattern matching 時 ACK 被當成 HELLO。</summary>
public abstract record HelloBase : ControlMessage
{
    public required string ProtocolVersion { get; init; }
    public required string AppVersion { get; init; }
    public required string DeviceId { get; init; }
    public required string Hostname { get; init; }
    public required bool Encryption { get; init; }
    /// <summary>完整的建置版本（例如 1.0.0+202610041530），僅供日誌除錯（選填）。</summary>
    public string? BuildVersion { get; init; }
    /// <summary>傳送端的 TCP 監聽 port。由對方發起配對時，本機靠它知道要往哪裡連回去。</summary>
    public int? ListenPort { get; init; }
}

public sealed record HelloMessage : HelloBase;

public sealed record HelloAckMessage : HelloBase
{
    /// <summary>回應端是否已信任發起端（已儲存其指紋）。</summary>
    public required bool Paired { get; init; }
}

public sealed record PingMessage : ControlMessage
{
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed record PongMessage : ControlMessage
{
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

// ---- 配對：發起端承諾 → 回應端公開亂數 → 發起端公開亂數 → 雙方各自確認 ----

public sealed record PairRequestMessage : ControlMessage
{
    /// <summary>SHA-256(發起端亂數)，Base64。</summary>
    public required string Commitment { get; init; }
}

public sealed record PairNonceMessage : ControlMessage
{
    public required string Nonce { get; init; }
}

public sealed record PairRevealMessage : ControlMessage
{
    public required string Nonce { get; init; }
}

public sealed record PairAcceptMessage : ControlMessage;

public sealed record PairRejectMessage : ControlMessage
{
    public required PairRejectReason Reason { get; init; }
}

public sealed record UnpairMessage : ControlMessage;

// ---- 傳輸 ----

public sealed record OfferItem
{
    public required string Name { get; init; }
    public required bool IsDirectory { get; init; }
    public required long Bytes { get; init; }
    public required int FileCount { get; init; }
}

public sealed record TransferOfferMessage : ControlMessage
{
    public required Guid JobId { get; init; }
    /// <summary>拖入的頂層項目（只有名稱與統計，不含完整檔案清單）。</summary>
    public required IReadOnlyList<OfferItem> Items { get; init; }
    public required int FileCount { get; init; }
    public required int DirectoryCount { get; init; }
    public required long TotalBytes { get; init; }
}

public sealed record TransferAcceptMessage : ControlMessage
{
    public required Guid JobId { get; init; }
    public required bool Encrypted { get; init; }
    /// <summary>未加密時才有，Base64 的 32 bytes 一次性 token。</summary>
    public string? DataToken { get; init; }
}

public sealed record TransferRejectMessage : ControlMessage
{
    public required Guid JobId { get; init; }
    public required RejectReason Reason { get; init; }
    /// <summary><see cref="RejectReason.InsufficientSpace"/> 時：需要的空間（含保留的 100 MB）。</summary>
    public long? RequiredBytes { get; init; }
    /// <summary><see cref="RejectReason.InsufficientSpace"/> 時：接收端剩餘的空間。</summary>
    public long? AvailableBytes { get; init; }
}

public sealed record TransferCancelMessage : ControlMessage
{
    public required Guid JobId { get; init; }
    public required CancelReason Reason { get; init; }
}

public enum FileOutcome
{
    Succeeded,
    Renamed,
    Overwritten,
    Merged,
    Skipped,
    Failed,
}

public sealed record FileResult(string Path, FileOutcome Outcome, string? FinalName = null, FileIssue? Issue = null);

public sealed record TransferResultMessage : ControlMessage
{
    public required Guid JobId { get; init; }
    public required int ExpectedFileCount { get; init; }
    public required int SucceededFileCount { get; init; }
    public required long ReceivedBytes { get; init; }
    /// <summary>傳輸被取消或中斷，只有已驗證的檔案被保留。</summary>
    public required bool Interrupted { get; init; }
    public CancelReason? InterruptReason { get; init; }
    /// <summary>每個頂層項目的最終結果（例如被改名成什麼）。</summary>
    public required IReadOnlyList<FileResult> TopLevel { get; init; }
    /// <summary>失敗或略過的檔案；最多 <see cref="MaxProblems"/> 筆以免超過訊息上限。</summary>
    public required IReadOnlyList<FileResult> Problems { get; init; }
    public required int ProblemCount { get; init; }

    public const int MaxProblems = 500;
}

public sealed record ErrorMessage : ControlMessage
{
    public required ErrorCode Code { get; init; }
}

// ---- 網路速度測試（診斷用）：從記憶體送資料、接收端直接丟掉，不讀寫磁碟 ----

/// <summary>請對方準備接收測速資料。只接受已配對的對方。</summary>
public sealed record SpeedTestRequestMessage : ControlMessage
{
    public required bool Encrypted { get; init; }
}

/// <summary>對方準備好了：用這個一次性 token 開測速通道。</summary>
public sealed record SpeedTestReadyMessage : ControlMessage
{
    public required string Token { get; init; }
}

/// <summary>接收端量到的結果（從第一批資料到最後一批資料的時間）。</summary>
public sealed record SpeedTestResultMessage : ControlMessage
{
    public required string Token { get; init; }
    public required long Bytes { get; init; }
    public required long Milliseconds { get; init; }
}
