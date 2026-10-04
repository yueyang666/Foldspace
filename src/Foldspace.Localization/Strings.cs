using System.Globalization;
using Foldspace.Core.Net;
using Foldspace.Core.Protocol;
using Foldspace.Core.Transfer;

namespace Foldspace.Localization;

/// <summary>
/// 介面上的所有文字。每種語言實作這個類別：少翻任何一句都無法編譯。
/// 傳輸核心只提供狀態代碼（<see cref="PeerStatus"/>、<see cref="JobNote"/>…），由這裡組成當地語言的句子。
/// </summary>
public abstract class Strings
{
    /// <summary>目前使用的語言（程式啟動時由 <see cref="Initialize"/> 決定，之後不會改變）。</summary>
    public static Strings Current { get; private set; } = new EnglishStrings();

    /// <summary>
    /// 依設定檔的 language 決定介面語言：auto（預設）跟隨 Windows 顯示語言；
    /// 也可以指定 en、zh-Hant、zh-Hans。不認得的值當作 auto。
    /// </summary>
    public static void Initialize(string? setting)
    {
        Current = Create(setting, CultureInfo.CurrentUICulture);
        var culture = CultureInfo.GetCultureInfo(Current.CultureName);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    internal static Strings Create(string? setting, CultureInfo system) => (setting ?? "auto").Trim().ToLowerInvariant() switch
    {
        "en" => new EnglishStrings(),
        "zh-hant" or "zh-tw" => new TraditionalChineseStrings(),
        "zh-hans" or "zh-cn" => new SimplifiedChineseStrings(),
        _ => FromCulture(system),
    };

    private static Strings FromCulture(CultureInfo culture)
    {
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            switch (c.Name)
            {
                case "zh-Hant" or "zh-TW" or "zh-HK" or "zh-MO":
                    return new TraditionalChineseStrings();
                case "zh-Hans" or "zh-CN" or "zh-SG":
                    return new SimplifiedChineseStrings();
            }
        }
        return new EnglishStrings();
    }

    /// <summary>en、zh-Hant、zh-Hans。</summary>
    public abstract string CultureName { get; }

    // ================= 共用 =================

    /// <summary>不知道對方名稱時的稱呼。</summary>
    public abstract string ThePeer { get; }
    public abstract string Version(string version);
    /// <summary>「report.pdf」或「report.pdf 等 3 個項目」。</summary>
    public abstract string Items(string firstName, int count);
    public abstract string FileCount(int count);
    public abstract string Duration(TimeSpan span);

    // ================= 系統匣 =================

    public abstract string TrayOpenSettings { get; }
    public abstract string TrayOpenReceiveFolder { get; }
    public abstract string TrayTransfers { get; }
    public abstract string TrayDisableService { get; }
    public abstract string TrayEnableService { get; }
    public abstract string TrayRecreateShortcut { get; }
    public abstract string TrayOpenLogFolder { get; }
    public abstract string TrayExit { get; }

    // ================= 連線狀態 =================

    public abstract string Status(PeerStatus status);
    public abstract string IncomingBlockedWarning { get; }
    /// <summary><see cref="PeerState.Error"/> 的說明。</summary>
    public abstract string PeerIssueText(PeerIssue issue, int port = 0, string? detail = null, string? peerVersion = null);

    // ================= 設定視窗 =================

    public abstract string SettingsTitle { get; }
    public abstract string Ok { get; }
    public abstract string Cancel { get; }
    public abstract string Apply { get; }
    public abstract string TabGeneral { get; }
    public abstract string TabConnection { get; }
    public abstract string TabReceive { get; }

    public abstract string GeneralDescription { get; }
    public abstract string GroupStatus { get; }
    public abstract string LabelStatus { get; }
    public abstract string LabelPeerComputer { get; }
    public abstract string ButtonTest { get; }
    public abstract string ButtonPair { get; }
    public abstract string ButtonUnpair { get; }
    public abstract string ButtonDisableService { get; }
    public abstract string ButtonEnableService { get; }
    public abstract string GroupSendFiles { get; }
    public abstract string DropZoneText { get; }
    public abstract string StartWithWindows { get; }

    public abstract string GroupLocal { get; }
    public abstract string LabelComputerName { get; }
    public abstract string LabelNetworkAdapter { get; }
    public abstract string LabelLocalPort { get; }
    public abstract string LabelFingerprint { get; }
    public abstract string GroupPeer { get; }
    public abstract string LabelPeerIp { get; }
    public abstract string LabelPeerPort { get; }
    public abstract string GroupSecurity { get; }
    public abstract string Encryption { get; }
    public abstract string EncryptionNote { get; }
    public abstract string PublicNetworkWarning { get; }
    public abstract string FirewallSetupLink { get; }
    public abstract string CannotConnectLink { get; }

    public abstract string GroupReceiveFolder { get; }
    public abstract string ReceiveFolderNote { get; }
    public abstract string Browse { get; }
    public abstract string BrowseTitle { get; }
    public abstract string GroupConflict { get; }
    public abstract string PolicyRename { get; }
    public abstract string PolicyOverwrite { get; }
    public abstract string PolicySkip { get; }
    public abstract string ConflictNote { get; }
    public abstract string AskBeforeReceive { get; }
    public abstract string AskBeforeReceiveNote { get; }

    public abstract string InvalidLocalPort { get; }
    public abstract string InvalidPeerIp { get; }
    public abstract string PeerIpSameAsLocal { get; }
    public abstract string InvalidPeerPort { get; }
    public abstract string InvalidReceiveFolder { get; }
    public abstract string LocalPortChanged(int port);

    public abstract string Testing { get; }
    public abstract string TestResult(TestConnectionResult result);
    public abstract string UnpairConfirm(string host);
    public abstract string UnpairDone { get; }
    public abstract string ConfirmPairingCode { get; }
    public abstract string FirewallHelpTitle { get; }
    public abstract string FirewallHelp { get; }
    public abstract string ConfirmExitWhileBusy { get; }
    public abstract string ConfirmDisableWhileBusy { get; }

    // ================= 配對 =================

    public abstract string PairingHeading(string host, string code, bool initiator);
    public abstract string PairingText(string host, string code);
    public abstract string PairingConfirm { get; }
    public abstract string PairingReject { get; }
    public abstract string PairingFingerprint(string fingerprint);
    public abstract string PairingShowFingerprint { get; }
    public abstract string PairingHideFingerprint { get; }
    public abstract string PairingWaiting(string host);
    public abstract string SecondsLeft(int seconds);
    public abstract string TimedOut { get; }
    public abstract string PairingSucceededTitle { get; }
    public abstract string PairingFailedTitle { get; }
    public abstract string Pairing(PairingOutcome outcome);

    // ================= 傳輸視窗 =================

    public abstract string TransfersTitle { get; }
    public abstract string NoTransfers { get; }
    public abstract string JobTitle(TransferJob job);

    /// <summary>標題的動詞：完成時「已傳送」、進行中「正在傳送」；失敗或取消時不寫，避免看起來像已完成。</summary>
    protected static string Verb(TransferJob job, string done, string ongoing) =>
        job.State == TransferJobState.Completed ? done : job.IsFinished ? "" : ongoing;
    public abstract string JobHeadline(TransferJobState state, double percent);
    public abstract string Speed(double megabytesPerSecond);
    public abstract string TimeLeft(TimeSpan? left);
    public abstract string Job(JobNote note, TransferDirection direction);

    // ================= 通知 =================

    public abstract string CannotSend { get; }
    public abstract string PeerOnlineTitle { get; }
    public abstract string PeerOnlineText(string host);
    public abstract string PeerOfflineTitle { get; }
    public abstract string PeerOfflineText(string host);
    public abstract string SentTitle(int files, string size, string host);
    public abstract string ReceivedTitle(int files, string size, string host);
    /// <summary>完成但沒有新增任何檔案（例如同名項目全部略過）。</summary>
    public abstract string NoNewFilesTitle(bool send, string host);
    /// <summary>本機拒收對方的傳輸（接收資料夾不能用、空間不足）。</summary>
    public abstract string IncomingRejectedTitle(string host);
    public abstract string IncomingRejectedText(IncomingRejection rejection, string receiveFolder);
    public abstract string SendFailed { get; }
    public abstract string ReceiveFailed { get; }
    public abstract string SendCancelled { get; }
    public abstract string ReceiveCancelled { get; }
    public abstract string SleepCancelledTitle { get; }
    public abstract string SleepCancelledText { get; }
    public abstract string StillRunningTitle { get; }
    public abstract string StillRunningText { get; }
    public abstract string ShortcutCreatedTitle { get; }
    public abstract string ShortcutCreatedText { get; }
    public abstract string ShortcutFailedTitle { get; }
    public abstract string ShortcutDescription { get; }

    // ---- 解除安裝（訊息裡第一個空行之前是對話框標題）----
    public abstract string UninstallShortcutName { get; }

    // ---- 同網段搜尋 ----
    public abstract string DiscoveryTitle { get; }

    // ---- 網路速度測試（診斷用）----
    public abstract string TraySpeedTest { get; }
    public abstract string SpeedTestTitle { get; }
    public abstract string SpeedTestRunning(string host);
    public abstract string SpeedTestNotConnected { get; }
    /// <summary>第一個空行之前是標題。</summary>
    public abstract string SpeedTestResultText(string host, SpeedTestResult plain, SpeedTestResult parallel, SpeedTestResult encrypted);
    public abstract string SpeedTestFailed(string detail);
    public abstract string DiscoveryDescription { get; }
    public abstract string ColumnComputer { get; }
    public abstract string ColumnAddress { get; }
    public abstract string ColumnStatus { get; }
    public abstract string DiscoverySearching { get; }
    public abstract string DiscoveryFound(int count);
    public abstract string DiscoveryNone { get; }
    public abstract string DiscoveryStatus(DiscoveredPeer peer);
    public abstract string ButtonSearchAgain { get; }
    public abstract string ButtonPairSelected { get; }
    public abstract string ConnectingTo(string host);
    public abstract string UninstallButton { get; }
    public abstract string UninstallConfirm(string receiveFolder, bool removesFirewallRules, bool transfersActive);
    public abstract string UninstallDone(string receiveFolder, bool firewallRulesLeft);
    public abstract string IncomingOfferTitle(string host);
    public abstract string IncomingOfferText(string items, int files, string size);
    public abstract string Accept { get; }
    public abstract string Reject { get; }

    // ================= 錯誤 =================

    public abstract string UnexpectedError(string message);
    public abstract string StartupFailed(string message, string logDir);

    // ================= 原因代碼 =================

    public abstract string FileIssueText(FileIssue issue);
    public abstract string RejectText(RejectReason reason, long requiredBytes, long availableBytes);
    public abstract string CancelText(CancelReason reason);
    public abstract string PairRejectText(PairRejectReason reason);
}
