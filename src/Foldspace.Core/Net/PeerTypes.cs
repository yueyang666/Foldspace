using System.Net;
using Serilog;
using Foldspace.Core.Identity;
using Foldspace.Core.Pairing;
using Foldspace.Core.Protocol;
using Foldspace.Core.Settings;

namespace Foldspace.Core.Net;

public enum PeerState
{
    /// <summary>灰：服務開關為 Disable。</summary>
    Disabled,
    /// <summary>灰：對方 IP 空白。</summary>
    NotConfigured,
    /// <summary>黃：已啟用但尚未握手成功。</summary>
    Searching,
    /// <summary>橙：握手成功但雙方尚未配對。</summary>
    Unpaired,
    /// <summary>綠：已配對且心跳正常。</summary>
    Connected,
    /// <summary>紅：心跳逾時或連線中斷，背景持續重連。</summary>
    Offline,
    /// <summary>紅：版本不相容、配對金鑰不符、port 被占用、本機 IP 不存在等。</summary>
    Error,
}

/// <summary><see cref="PeerState.Error"/> 的原因。</summary>
public enum PeerIssue
{
    None,
    /// <summary>綁定的網卡或 IP 已不存在（網卡停用、拔線、IP 改變）。</summary>
    LocalAddressMissing,
    /// <summary>本機連接埠已被其他程式使用（<see cref="PeerStatus.Port"/>）。</summary>
    PortInUse,
    /// <summary>其他監聽失敗（<see cref="PeerStatus.Detail"/>）。</summary>
    ListenFailed,
    /// <summary>雙方主版號不同（<see cref="PeerStatus.PeerAppVersion"/>）。</summary>
    VersionIncompatible,
    /// <summary>對方的設備憑證和配對時記錄的不同（可能重灌過）。</summary>
    PairingMismatch,
    /// <summary>對方記錄的配對設備不是本機。</summary>
    PeerHasOtherPairing,
    /// <summary>對方尚未配對，且它設定的「對方 IP」不是本機。</summary>
    PeerRejectsAddress,
}

/// <summary>連線狀態。只有代碼與參數，由介面組成當地語言的文字。</summary>
/// <param name="IncomingBlocked">本機能連到對方，但對方連不進來（通常是本機防火牆）。</param>
/// <param name="Port">本機監聽的連接埠（<see cref="PeerIssue.PortInUse"/> 時顯示）。</param>
/// <param name="Detail">技術細節（例如作業系統的錯誤訊息），沒有則為 null。</param>
public sealed record PeerStatus(
    PeerState State,
    PeerIssue Issue = PeerIssue.None,
    string? PeerHostname = null,
    string? PeerAppVersion = null,
    bool IncomingBlocked = false,
    int Port = 0,
    string? Detail = null)
{
    public static PeerStatus Disabled { get; } = new(PeerState.Disabled);
}

public sealed record PeerServiceOptions
{
    public required DeviceIdentity Identity { get; init; }
    public required string Hostname { get; init; }
    /// <summary>目前要綁定的本機 IPv4；回傳 null 表示該網卡或 IP 已不存在。</summary>
    public required Func<IPAddress?> ResolveLocalAddress { get; init; }
    public required int LocalPort { get; init; }
    public IPAddress? PeerAddress { get; init; }
    public required int PeerPort { get; init; }
    public required bool Encryption { get; init; }
    public required ILogger Logger { get; init; }
    /// <summary>回應同網段搜尋的 UDP port（測試時改用不同 port）。</summary>
    public int DiscoveryPort { get; init; } = ProtocolConstants.DiscoveryPort;
    /// <summary>搜尋要送往的位址；null = 綁定網卡所在網段的廣播位址（測試時改成本機的單播位址）。</summary>
    public Func<IReadOnlyList<IPEndPoint>>? DiscoveryTargets { get; init; }
    public TimeSpan RediscoverInterval { get; init; } = ProtocolConstants.RediscoverInterval;
}

/// <summary>同網段搜尋找到的一台電腦。</summary>
public sealed record DiscoveredPeer(
    string Hostname,
    IPAddress Address,
    int Port,
    string Fingerprint,
    string AppVersion,
    string ProtocolVersion,
    bool Paired,
    bool PairedWithMe)
{
    public bool IsCompatible => ProtocolConstants.MajorVersion(ProtocolVersion) == ProtocolConstants.MajorVersion(ProtocolConstants.ProtocolVersion);

    /// <summary>可以發起配對：版本相容，而且對方還沒有和其他電腦配對。</summary>
    public bool CanPair => IsCompatible && (!Paired || PairedWithMe);
}

/// <summary>接收相關的設定可以隨時修改，不需重啟服務。</summary>
public sealed record ReceiveOptions(string ReceiveFolder, ConflictPolicy ConflictPolicy, bool AskBeforeReceive);

/// <summary>配對結果的保存位置（App 存在 settings.json）。</summary>
public interface IPairingStore
{
    string? PeerFingerprint { get; }
    string? PeerHostname { get; }
    void SavePairing(string fingerprint, string hostname);
    void ClearPairing();
    void UpdatePeerHostname(string hostname);
}

public sealed class InMemoryPairingStore : IPairingStore
{
    public string? PeerFingerprint { get; private set; }
    public string? PeerHostname { get; private set; }

    public void SavePairing(string fingerprint, string hostname)
    {
        PeerFingerprint = fingerprint;
        PeerHostname = hostname;
    }

    public void ClearPairing()
    {
        PeerFingerprint = null;
        PeerHostname = null;
    }

    public void UpdatePeerHostname(string hostname) => PeerHostname = hostname;
}

/// <summary>Test Connection 的結果。</summary>
public enum TestOutcome
{
    Success,
    NotConfigured,
    /// <summary>
    /// 連線逾時：IP 不存在、不在同一網段，或被防火牆阻擋。
    /// 對方未執行或服務已停用也常是逾時：Windows 防火牆對沒有程式在監聽的連接埠直接丟棄，不回應「拒絕」。
    /// </summary>
    Timeout,
    /// <summary>連線被拒：對方未執行、服務已停用，或連接埠錯誤。</summary>
    Refused,
    /// <summary>無法到達對方 IP。</summary>
    Unreachable,
    /// <summary>其他網路錯誤（<see cref="TestConnectionResult.Detail"/>）。</summary>
    SocketError,
    /// <summary>安全連線失敗：該連接埠上的程式可能不是 Foldspace。</summary>
    TlsFailed,
    NoHandshakeResponse,
    BadResponse,
    VersionIncompatible,
    /// <summary>對方的設備憑證和配對時記錄的不同。</summary>
    PairingMismatch,
    /// <summary>對方記錄的配對設備不是本機。</summary>
    PeerHasOtherPairing,
    /// <summary>對方設定的「對方 IP」不是本機。</summary>
    PeerRejectsAddress,
    /// <summary>握手成功但心跳沒有回應。</summary>
    NoHeartbeat,
}

public sealed record TestConnectionResult
{
    public required TestOutcome Outcome { get; init; }
    public bool Success => Outcome == TestOutcome.Success;
    /// <summary>測試的對象，例如 192.168.0.205:52500。</summary>
    public string? Endpoint { get; init; }
    public string? PeerHostname { get; init; }
    public string? PeerAppVersion { get; init; }
    public bool Paired { get; init; }
    public double? RoundTripMs { get; init; }
    /// <summary>技術細節（例如作業系統的錯誤訊息）。</summary>
    public string? Detail { get; init; }
}

/// <summary>「接收時詢問」開啟時，交給 UI 顯示的資訊。</summary>
/// <summary>本機因為自己的狀況（接收資料夾不能用、空間不足）拒絕了對方的傳輸，要讓本機使用者知道。</summary>
public sealed record IncomingRejection(string PeerHostname, RejectReason Reason, long RequiredBytes = 0, long AvailableBytes = 0);

public sealed record IncomingOffer(string PeerHostname, string ItemName, int ItemCount, int FileCount, long TotalBytes);

public enum PairingResult
{
    Success,
    NotConnected,
    InProgress,
    /// <summary>對方無法配對（<see cref="PairingOutcome.PeerReason"/>）。</summary>
    PeerCannotPair,
    BadResponse,
    Timeout,
    ConnectionLost,
    /// <summary>本機使用者拒絕。</summary>
    Rejected,
    /// <summary>對方使用者拒絕。</summary>
    RejectedByPeer,
    /// <summary>對方公開的亂數與承諾不符，可能有其他設備介入。</summary>
    VerificationFailed,
    /// <summary>兩端同時發起配對，改由對方發起。</summary>
    Superseded,
    ServiceStopped,
}

public sealed record PairingOutcome(PairingResult Result, string? PeerHostname = null, PairRejectReason? PeerReason = null)
{
    public bool Success => Result == PairingResult.Success;
}

/// <summary>
/// 配對時給 UI 的對話框內容。UI 呼叫 <see cref="Confirm"/> 或 <see cref="Reject"/>；
/// 對方拒絕或逾時時 <see cref="Closed"/> 會被取消，UI 應關閉對話框。
/// </summary>
public sealed class PairingPrompt
{
    private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _closed = new();

    internal PairingPrompt(string code, string peerHostname, string peerFingerprint, bool isInitiator)
    {
        Code = PairingCode.Format(code);
        PeerHostname = peerHostname;
        PeerFingerprint = DeviceIdentity.ShortFingerprint(peerFingerprint);
        IsInitiator = isInitiator;
    }

    public string Code { get; }
    public string PeerHostname { get; }
    public string PeerFingerprint { get; }
    public bool IsInitiator { get; }
    public CancellationToken Closed => _closed.Token;
    /// <summary>對話框被關閉的原因（配對結束時是 <see cref="PairingResult.Success"/>）。</summary>
    public PairingResult? CloseReason { get; private set; }

    internal Task<bool> Decision => _decision.Task;

    public void Confirm() => _decision.TrySetResult(true);
    public void Reject() => _decision.TrySetResult(false);

    internal void Close(PairingResult reason)
    {
        CloseReason ??= reason;
        _decision.TrySetResult(false);
        try { _closed.Cancel(); } catch (ObjectDisposedException) { }
    }
}
