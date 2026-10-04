using Foldspace.Core;
using Foldspace.Core.Net;
using Foldspace.Core.Protocol;
using Foldspace.Core.Transfer;

namespace Foldspace.Localization;

/// <summary>繁體中文（台灣用語）。</summary>
public sealed class TraditionalChineseStrings : Strings
{
    public override string CultureName => "zh-Hant";

    // ================= 共用 =================

    public override string ThePeer => "對方";
    public override string Version(string version) => $"版本 {version}";
    public override string Items(string firstName, int count) => count <= 1 ? firstName : $"{firstName} 等 {count} 個項目";
    public override string FileCount(int count) => $"{count} 個檔案";
    public override string Duration(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} 小時 {span.Minutes} 分"
        : span.TotalMinutes >= 1 ? $"{span.Minutes} 分 {span.Seconds} 秒"
        : $"{Math.Max(0, span.Seconds)} 秒";

    // ================= 系統匣 =================

    public override string TrayOpenSettings => "開啟設定(&S)";
    public override string TrayOpenReceiveFolder => "開啟接收資料夾(&F)";
    public override string TrayTransfers => "傳輸進度(&P)";
    public override string TrayDisableService => "停用服務(&D)";
    public override string TrayEnableService => "啟用服務(&E)";
    public override string TrayRecreateShortcut => "重新建立桌面捷徑(&C)";
    public override string TrayOpenLogFolder => "開啟日誌資料夾(&L)";
    public override string TrayExit => "結束(&X)";

    // ================= 連線狀態 =================

    public override string Status(PeerStatus status) => status.State switch
    {
        PeerState.Disabled => "已停用",
        PeerState.NotConfigured => "尚未配對（按「配對」尋找對方電腦）",
        PeerState.Searching => "尋找中",
        PeerState.Unpaired => "未配對",
        PeerState.Connected => $"已連線到 {status.PeerHostname}",
        PeerState.Offline => "離線",
        _ => PeerIssueText(status.Issue, status.Port, status.Detail, status.PeerAppVersion),
    };

    public override string IncomingBlockedWarning => "對方目前無法連入本機，對方傳來的檔案會失敗。請檢查本機防火牆是否允許 Foldspace。";

    public override string PeerIssueText(PeerIssue issue, int port = 0, string? detail = null, string? peerVersion = null) => issue switch
    {
        PeerIssue.LocalAddressMissing => "本機 IP 已不存在（網卡停用、拔線或 IP 改變）",
        PeerIssue.PortInUse => $"連接埠 {port} 已被其他程式使用",
        PeerIssue.ListenFailed => $"無法監聽連接埠 {port}：{detail}",
        PeerIssue.VersionIncompatible => $"版本不相容（本機 {AppVersion}，對方 {peerVersion ?? "?"}），請把兩台電腦更新到相同版本",
        PeerIssue.PairingMismatch => "配對金鑰不符：對方的設備憑證已改變（可能重灌過）。請解除配對後重新配對。",
        PeerIssue.PeerHasOtherPairing => "配對金鑰不符：對方記錄的是另一台設備。請在對方解除配對後重新配對。",
        PeerIssue.PeerRejectsAddress => "對方拒絕連線：對方設定的「對方 IP」不是本機",
        _ => "錯誤",
    };

    private static string AppVersion => ProtocolConstants.AppVersion;

    // ================= 設定視窗 =================

    public override string SettingsTitle => "Foldspace 設定";
    public override string Ok => "確定";
    public override string Cancel => "取消";
    public override string Apply => "套用(&A)";
    public override string TabGeneral => "一般";
    public override string TabConnection => "連線";
    public override string TabReceive => "接收";

    public override string GeneralDescription => "把檔案或資料夾拖到桌面上的「Foldspace」，就會傳送到對方電腦。";
    public override string GroupStatus => "連線狀態";
    public override string LabelStatus => "狀態:";
    public override string LabelPeerComputer => "對方電腦:";
    public override string ButtonTest => "測試連線(&T)";
    public override string ButtonPair => "配對(&P)…";
    public override string ButtonUnpair => "解除配對(&U)";
    public override string ButtonDisableService => "停用服務(&D)";
    public override string ButtonEnableService => "啟用服務(&E)";
    public override string GroupSendFiles => "傳送檔案";
    public override string DropZoneText => "將檔案或資料夾拖曳到這裡。\n一次要傳很多項目時，建議拖曳它們的上層資料夾。";
    public override string StartWithWindows => "開機時自動啟動(&S)";

    public override string GroupLocal => "本機";
    public override string LabelComputerName => "電腦名稱:";
    public override string LabelNetworkAdapter => "網路介面卡(&N):";
    public override string LabelLocalPort => "連接埠(&O):";
    public override string LabelFingerprint => "設備指紋:";
    public override string GroupPeer => "對方電腦";
    public override string LabelPeerIp => "IP 位址(&I):";
    public override string LabelPeerPort => "連接埠(&R):";
    public override string GroupSecurity => "安全性";
    public override string Encryption => "加密傳輸的檔案內容(&C)";
    public override string EncryptionNote => "身分驗證一律加密。關閉此選項可提升速度，但檔案內容會以明文傳送。只要任一方開啟，傳輸就會加密。";
    public override string PublicNetworkWarning => "目前的網路設定檔是「公用」，Windows 防火牆可能會阻擋對方連入。";
    public override string CannotConnectLink => "無法連線嗎?";
    public override string FirewallSetupLink => "允許 Foldspace 通過 Windows 防火牆…";

    public override string GroupReceiveFolder => "接收資料夾";
    public override string ReceiveFolderNote => "從對方收到的檔案會存放在這個資料夾:";
    public override string Browse => "瀏覽(&B)...";
    public override string BrowseTitle => "選擇接收資料夾";
    public override string GroupConflict => "接收資料夾中已有同名項目時";
    public override string PolicyRename => "自動改名(&R)，例如 report (1).pdf";
    public override string PolicyOverwrite => "覆蓋既有的檔案(&O)";
    public override string PolicySkip => "略過，不接收同名的檔案(&S)";
    public override string ConflictNote => "收到的是資料夾時：自動改名會另存整個資料夾；覆蓋與略過會合併到既有的資料夾。";
    public override string AskBeforeReceive => "接收前先詢問(&K)";
    public override string AskBeforeReceiveNote => "60 秒內未回應視為拒絕。";

    public override string InvalidLocalPort => "本機連接埠必須是 1024 到 65535 之間的數字。";
    public override string InvalidPeerIp => "對方 IP 位址的格式不正確，例如 192.168.1.20。";
    public override string PeerIpSameAsLocal => "對方 IP 位址不能和本機相同。";
    public override string InvalidPeerPort => "對方連接埠必須是 1024 到 65535 之間的數字。";
    public override string InvalidReceiveFolder => "請輸入接收資料夾的完整路徑，例如 C:\\Users\\me\\Downloads\\Foldspace。";
    public override string LocalPortChanged(int port) =>
        $"本機連接埠已變更為 {port}。\n\n請在對方電腦的「連線 > 對方電腦 > 連接埠」也改成 {port}，否則對方會無法連線。";

    public override string Testing => "正在測試連線...";

    public override string TestResult(TestConnectionResult r) => r.Outcome switch
    {
        TestOutcome.Success =>
            $"連線成功，延遲 {r.RoundTripMs:0.#}\u00A0ms\n{r.PeerHostname}（版本 {r.PeerAppVersion}），{(r.Paired ? "已配對" : "未配對")}",
        TestOutcome.NotConfigured => "尚未設定對方 IP",
        TestOutcome.Timeout => $"連線逾時（{r.Endpoint}）：對方未執行 Foldspace 或服務已停用、IP 錯誤或不在同一網段，或被防火牆阻擋",
        TestOutcome.Refused => $"無法連線（{r.Endpoint}）：對方未執行、服務已停用，或連接埠設定錯誤",
        TestOutcome.Unreachable => $"無法到達 {r.Endpoint}：請確認兩台電腦在同一網段",
        TestOutcome.SocketError => $"無法連線（{r.Endpoint}）：{r.Detail}",
        TestOutcome.TlsFailed => $"安全連線失敗（{r.Endpoint}）：該連接埠上的程式可能不是 Foldspace",
        TestOutcome.NoHandshakeResponse => "對方沒有回應握手",
        TestOutcome.BadResponse => "對方的握手回應不正確",
        TestOutcome.VersionIncompatible => PeerIssueText(PeerIssue.VersionIncompatible, peerVersion: r.PeerAppVersion),
        TestOutcome.PairingMismatch => PeerIssueText(PeerIssue.PairingMismatch),
        TestOutcome.PeerHasOtherPairing => PeerIssueText(PeerIssue.PeerHasOtherPairing),
        TestOutcome.PeerRejectsAddress => PeerIssueText(PeerIssue.PeerRejectsAddress),
        TestOutcome.NoHeartbeat => "握手成功但心跳沒有回應",
        _ => r.Outcome.ToString(),
    };

    public override string UnpairConfirm(string host) => $"確定要解除與 {host} 的配對嗎?\n\n解除後需要重新配對才能傳輸檔案。";
    public override string UnpairDone => "已解除配對。";
    public override string ConfirmPairingCode => "請在兩台電腦上確認配對碼...";
    public override string FirewallHelpTitle => "無法連線嗎?";
    public override string FirewallHelp =>
        "請在兩台電腦上依序確認:\n\n" +
        "1. Foldspace 正在執行，且服務已啟用。\n" +
        "2. 兩台電腦在同一個網段（例如都是 192.168.1.x）。\n" +
        "3. 「對方電腦」的 IP 位址與連接埠，和對方「本機」的設定一致。\n" +
        "4. Windows 防火牆允許 Foldspace：在「連線」分頁按「允許 Foldspace 通過 Windows 防火牆」（需要系統管理員權限）。\n" +
        "    沒有這個連結時，表示已經設定好了。";
    public override string ConfirmExitWhileBusy => "有傳輸正在進行，結束程式會中斷傳輸。確定要結束嗎?";
    public override string ConfirmDisableWhileBusy => "有傳輸正在進行，停用服務會中斷傳輸。確定要停用嗎?";

    // ================= 配對 =================

    public override string PairingHeading(string host, string code, bool initiator) =>
        initiator ? $"確認與 {host} 配對: {code}" : $"{host} 要求配對: {code}";
    public override string PairingText(string host, string code) =>
        $"請確認 {host} 上顯示的配對碼也是 {code}。\n數字相同時按「確認」；不同時請按「拒絕」，可能有其他設備介入。";
    public override string PairingConfirm => "確認(&Y)";
    public override string PairingReject => "拒絕(&N)";
    public override string PairingFingerprint(string fingerprint) => $"對方設備指紋: {fingerprint}";
    public override string PairingShowFingerprint => "顯示設備指紋";
    public override string PairingHideFingerprint => "隱藏設備指紋";
    public override string PairingWaiting(string host) => $"正在等待 {host} 確認...";
    public override string SecondsLeft(int seconds) => $"剩餘 {seconds} 秒";
    public override string TimedOut => "已逾時";
    public override string PairingSucceededTitle => "配對成功";
    public override string PairingFailedTitle => "配對失敗";

    public override string Pairing(PairingOutcome o)
    {
        var host = o.PeerHostname ?? ThePeer;
        return o.Result switch
        {
            PairingResult.Success => $"已與 {host} 配對",
            PairingResult.NotConnected => "無法連線到對方。請確認對方的 Foldspace 正在執行，且兩台在同一個網段",
            PairingResult.InProgress => "配對正在進行中",
            PairingResult.PeerCannotPair => $"{host} 無法配對：{(o.PeerReason is { } r ? PairRejectText(r) : "未知原因")}",
            PairingResult.BadResponse => "對方的配對回應不正確",
            PairingResult.Timeout => "配對逾時（60 秒內未完成）",
            PairingResult.ConnectionLost => "連線中斷，配對失敗",
            PairingResult.Rejected => "已拒絕配對",
            PairingResult.RejectedByPeer => $"{host} 拒絕了配對",
            PairingResult.VerificationFailed => "配對驗證失敗，可能有其他設備介入，請重試",
            PairingResult.Superseded => "兩端同時發起配對，改由對方發起",
            PairingResult.ServiceStopped => "服務已停止，配對取消",
            _ => o.Result.ToString(),
        };
    }

    // ================= 傳輸視窗 =================

    public override string TransfersTitle => "Foldspace - 傳輸";
    public override string NoTransfers => "目前沒有傳輸。";

    public override string JobTitle(TransferJob job)
    {
        var items = Items(job.ItemName, job.ItemCount);
        var host = job.PeerHostname ?? ThePeer;
        return job.Direction == TransferDirection.Send
            ? $"↑ {Verb(job, "已傳送 ", "正在傳送 ")}{items} 到 {host}"
            : $"↓ {Verb(job, "已接收 ", "正在接收 ")}{items}，來自 {host}";
    }

    public override string JobHeadline(TransferJobState state, double percent) => state switch
    {
        TransferJobState.Queued => "排隊中",
        TransferJobState.Preparing => "準備中...",
        TransferJobState.WaitingForPeer => "等待對方...",
        TransferJobState.Finalizing => "即將完成...",
        TransferJobState.Completed => "已完成",
        TransferJobState.Failed => "失敗",
        TransferJobState.Cancelled => "已取消",
        _ => $"已完成 {percent:0}%",
    };

    public override string Speed(double megabytesPerSecond) => $"速度: {megabytesPerSecond:0.0} MB/s";
    public override string TimeLeft(TimeSpan? left) => left is { } t ? $"剩餘時間: {Duration(t)}" : "剩餘時間: 計算中";

    public override string Job(JobNote note, TransferDirection direction)
    {
        var verb = direction == TransferDirection.Send ? "傳送" : "接收";
        return note.Issue switch
        {
            JobIssue.NothingToSend => note.Path is { } path && note.FileIssue is { } fi
                ? $"沒有可傳送的項目（{path}：{FileIssueText(fi)}）"
                : "沒有可傳送的項目",
            JobIssue.DriveRoot => "無法傳送整個磁碟，請拖入資料夾",
            JobIssue.ServiceDisabled => "服務已停用，請先在設定中啟用服務",
            JobIssue.NotConfigured => "尚未設定對方 IP",
            JobIssue.NotPaired => "尚未與對方配對",
            JobIssue.PeerError => "目前無法連線：" + PeerIssueText(note.PeerIssue ?? PeerIssue.None),
            JobIssue.PeerOffline => "對方離線",
            JobIssue.Rejected => note.Rejection is { } rr ? RejectText(rr, note.RequiredBytes, note.AvailableBytes) : "對方拒絕接收",
            JobIssue.NoResponse => "對方沒有回應",
            JobIssue.ConnectionLost => "連線中斷",
            JobIssue.PairingMismatch => PeerIssueText(PeerIssue.PairingMismatch),
            JobIssue.DataChannelTimeout => "對方沒有開始傳送",
            JobIssue.Cancelled => CancelText(note.Cause ?? CancelReason.User),
            JobIssue.Interrupted => direction == TransferDirection.Send
                ? $"{CancelText(note.Cause ?? CancelReason.ConnectionLost)}，對方保留了 {note.Count} 個完成的檔案"
                : $"{CancelText(note.Cause ?? CancelReason.ConnectionLost)}，已保留 {note.Count} 個完成的檔案",
            JobIssue.Aborted => CancelText(note.Cause ?? CancelReason.ConnectionLost),
            JobIssue.AllFailed => $"所有檔案都{verb}失敗",
            JobIssue.SomeProblems => $"{note.Count} 個項目未{verb}",
            JobIssue.Unexpected => $"發生未預期的錯誤：{note.Detail}",
            _ => note.Issue.ToString(),
        };
    }

    // ================= 通知 =================

    public override string CannotSend => "無法傳送";
    public override string PeerOnlineTitle => "對方已上線";
    public override string PeerOnlineText(string host) => $"已重新連線到 {host}";
    public override string PeerOfflineTitle => "對方已離線";
    public override string PeerOfflineText(string host) => $"與 {host} 的連線中斷，會在背景自動重連";
    public override string SentTitle(int files, string size, string host) => $"已傳送 {files} 個檔案（{size}）到 {host}";
    public override string ReceivedTitle(int files, string size, string host) => $"已收到 {files} 個檔案（{size}）來自 {host}";
    public override string NoNewFilesTitle(bool send, string host) =>
        send ? $"已傳送到 {host}，但沒有新增任何檔案" : $"沒有收到新檔案（來自 {host}）";
    public override string IncomingRejectedTitle(string host) => $"無法接收 {host} 傳來的檔案";
    public override string IncomingRejectedText(IncomingRejection rejection, string receiveFolder) => rejection.Reason switch
    {
        RejectReason.InsufficientSpace =>
            $"磁碟空間不足：需要 {Format.Bytes(rejection.RequiredBytes)}，剩餘 {Format.Bytes(rejection.AvailableBytes)}。",
        _ => $"接收資料夾無法使用：{receiveFolder}。請在設定中確認資料夾可以寫入。",
    };
    public override string SendFailed => "傳送失敗";
    public override string ReceiveFailed => "接收失敗";
    public override string SendCancelled => "傳送已取消";
    public override string ReceiveCancelled => "接收已取消";
    public override string SleepCancelledTitle => "傳輸已取消";
    public override string SleepCancelledText => "電腦進入睡眠，進行中的傳輸已取消";
    public override string StillRunningTitle => "Foldspace 仍在背景執行";
    public override string StillRunningText => "可以從右下角的系統匣圖示開啟設定，或從選單結束程式。";
    public override string ShortcutCreatedTitle => "已建立桌面捷徑";
    public override string ShortcutCreatedText => "把檔案拖到桌面上的「Foldspace」就會傳送到對方";
    public override string ShortcutFailedTitle => "無法建立桌面捷徑";
    public override string ShortcutDescription => "把檔案或資料夾拖到這裡，就會傳到另一台電腦";

    public override string UninstallShortcutName => "解除安裝 Foldspace";

    public override string DiscoveryTitle => "選擇要配對的電腦";

    public override string TraySpeedTest => "網路速度測試(&N)";
    public override string SpeedTestTitle => "網路速度測試";
    public override string SpeedTestRunning(string host) => $"正在測試本機到 {host} 的網路速度，約 15 秒…";
    public override string SpeedTestNotConnected => "要先和已配對的電腦連線（綠燈）才能測速。";
    public override string SpeedTestResultText(string host, SpeedTestResult plain, SpeedTestResult parallel, SpeedTestResult encrypted) =>
        $"本機 → {host}\n\n不加密，1 條連線：{plain.Mbps:F0} Mbps（{plain.MegabytesPerSecond:F1} MB/s）\n" +
        $"不加密，4 條連線：{parallel.Mbps:F0} Mbps（{parallel.MegabytesPerSecond:F1} MB/s）\n" +
        $"加密，1 條連線：{encrypted.Mbps:F0} Mbps（{encrypted.MegabytesPerSecond:F1} MB/s）\n\n" +
        "測試方式：從記憶體送出資料 5 秒，對方收到後直接丟棄，不讀寫磁碟。\n" +
        "4 條：同時開 4 條連線，速度為總和。\n" +
        "加密：使用與傳檔相同的 TLS 加密。";
    public override string SpeedTestFailed(string detail) => $"測速失敗：{detail}";
    public override string DiscoveryDescription => "下面是同一個網段裡開著 Foldspace 的電腦。選擇要配對的那一台，兩邊會顯示同一組 6 位數字讓你確認。";
    public override string ColumnComputer => "電腦名稱";
    public override string ColumnAddress => "IP 位址";
    public override string ColumnStatus => "狀態";
    public override string DiscoverySearching => "正在搜尋…";
    public override string DiscoveryFound(int count) => $"找到 {count} 台電腦。";
    public override string DiscoveryNone =>
        "找不到其他開著 Foldspace 的電腦。請確認對方已開啟 Foldspace、兩台在同一個網段；也可以在「連線」分頁手動輸入對方 IP。";
    public override string DiscoveryStatus(DiscoveredPeer peer) =>
        !peer.IsCompatible ? $"版本不相容（{peer.AppVersion}）"
        : peer.PairedWithMe ? "已和本機配對"
        : peer.Paired ? "已和其他電腦配對"
        : "可以配對";
    public override string ButtonSearchAgain => "重新搜尋(&R)";
    public override string ButtonPairSelected => "配對(&P)";
    public override string ConnectingTo(string host) => $"正在連線到 {host}…";
    public override string UninstallButton => "解除安裝(&U)";
    public override string UninstallConfirm(string receiveFolder, bool removesFirewallRules, bool transfersActive) =>
        $"要解除安裝 Foldspace 嗎?\n\n會移除程式、設定與配對、日誌、桌面捷徑和開始功能表項目。接收資料夾裡的檔案會保留：\n{receiveFolder}"
        + (removesFirewallRules ? "\n\n也會移除 Windows 防火牆中的 Foldspace 規則，需要系統管理員權限。" : "")
        + (transfersActive ? "\n\n有傳輸正在進行，解除安裝會中斷傳輸。" : "");
    public override string UninstallDone(string receiveFolder, bool firewallRulesLeft) =>
        $"Foldspace 已解除安裝\n\n接收資料夾裡的檔案已保留：\n{receiveFolder}"
        + (firewallRulesLeft ? "\n\n沒有取得系統管理員權限，Windows 防火牆中的 Foldspace 規則沒有移除。可以在「Windows Defender 防火牆 > 進階設定 > 輸入規則」中手動刪除。" : "");
    public override string IncomingOfferTitle(string host) => $"{host} 想傳送檔案給你";
    public override string IncomingOfferText(string items, int files, string size) => $"{items}：{files} 個檔案（{size}）";
    public override string Accept => "接收";
    public override string Reject => "拒絕";

    // ================= 錯誤 =================

    public override string UnexpectedError(string message) => $"發生錯誤：{message}\n\n程式會繼續執行，詳細資訊已寫入日誌。";
    public override string StartupFailed(string message, string logDir) => $"Foldspace 無法啟動：{message}\n\n詳細資訊請看日誌：{logDir}";

    // ================= 原因代碼 =================

    public override string FileIssueText(FileIssue issue) => issue switch
    {
        FileIssue.NotFound => "找不到項目",
        FileIssue.ReparsePoint => "符號連結或 junction 不會傳送",
        FileIssue.FolderUnreadable => "無法讀取資料夾內容（權限不足）",
        FileIssue.NoReadPermission => "沒有讀取權限",
        FileIssue.Locked => "檔案被其他程式鎖定或無法開啟",
        FileIssue.ReadFailed => "讀取來源檔案失敗",
        FileIssue.SourceModified => "傳送中來源檔案被修改",
        FileIssue.InvalidName => "名稱在 Windows 上不合法",
        FileIssue.HashMismatch => "完整性檢查失敗（雜湊不符）",
        FileIssue.WriteFailed => "無法寫入接收資料夾",
        FileIssue.SkippedExisting => "接收資料夾已有同名項目",
        FileIssue.FolderExists => "接收資料夾已有同名資料夾",
        FileIssue.FileExists => "接收資料夾已有同名檔案，無法建立資料夾",
        FileIssue.OverwriteFailed => "舊檔案唯讀或被其他程式鎖定，無法覆蓋",
        FileIssue.MoveFailed => "無法移到接收資料夾",
        _ => issue.ToString(),
    };

    public override string RejectText(RejectReason reason, long requiredBytes, long availableBytes) => reason switch
    {
        RejectReason.NotPaired => "對方尚未與你配對，拒絕接收",
        RejectReason.Busy => "對方正在接收其他檔案",
        RejectReason.Declined => "對方拒絕接收",
        RejectReason.DeclineTimeout => "對方未在 60 秒內回應，視為拒絕",
        RejectReason.ReceiveFolderUnavailable => "對方的接收資料夾無法寫入",
        RejectReason.InsufficientSpace => $"對方磁碟空間不足：需要 {Format.Bytes(requiredBytes)}，剩餘 {Format.Bytes(availableBytes)}",
        RejectReason.InvalidOffer => "傳輸請求內容不合法",
        _ => reason.ToString(),
    };

    public override string CancelText(CancelReason reason) => reason switch
    {
        CancelReason.User => "已取消",
        CancelReason.Peer => "對方取消了傳輸",
        CancelReason.PeerSecurityViolation => "對方因安全問題中斷了傳輸",
        CancelReason.PeerDiskFull => "對方磁碟空間不足，傳輸已取消",
        CancelReason.ServiceStopped => "服務已停止",
        CancelReason.PeerOffline => "對方離線，傳輸中斷",
        CancelReason.Unpaired => "配對已解除",
        CancelReason.ConnectionLost => "連線中斷",
        CancelReason.SecurityViolation => "對方送來不安全的檔案路徑，已中斷傳輸",
        CancelReason.DiskFull => "磁碟空間不足，傳輸已取消",
        CancelReason.CleanupFailed => "無法清理未完成的暫存檔，已放棄這次接收的所有檔案",
        _ => reason.ToString(),
    };

    public override string PairRejectText(PairRejectReason reason) => reason switch
    {
        PairRejectReason.Busy => "對方正在進行其他配對",
        PairRejectReason.UserRejected => "對方拒絕了配對",
        PairRejectReason.InvalidRequest => "配對請求格式錯誤",
        PairRejectReason.VerificationFailed => "配對驗證失敗",
        _ => reason.ToString(),
    };
}
