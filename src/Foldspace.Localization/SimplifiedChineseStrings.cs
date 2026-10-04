using Foldspace.Core;
using Foldspace.Core.Net;
using Foldspace.Core.Protocol;
using Foldspace.Core.Transfer;

namespace Foldspace.Localization;

/// <summary>简体中文（中国大陆用语）。</summary>
public sealed class SimplifiedChineseStrings : Strings
{
    public override string CultureName => "zh-Hans";

    // ================= 共用 =================

    public override string ThePeer => "对方";
    public override string Version(string version) => $"版本 {version}";
    public override string Items(string firstName, int count) => count <= 1 ? firstName : $"{firstName} 等 {count} 个项目";
    public override string FileCount(int count) => $"{count} 个文件";
    public override string Duration(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} 小时 {span.Minutes} 分"
        : span.TotalMinutes >= 1 ? $"{span.Minutes} 分 {span.Seconds} 秒"
        : $"{Math.Max(0, span.Seconds)} 秒";

    // ================= 系统托盘 =================

    public override string TrayOpenSettings => "打开设置(&S)";
    public override string TrayOpenReceiveFolder => "打开接收文件夹(&F)";
    public override string TrayTransfers => "传输进度(&P)";
    public override string TrayDisableService => "停用服务(&D)";
    public override string TrayEnableService => "启用服务(&E)";
    public override string TrayRecreateShortcut => "重新创建桌面快捷方式(&C)";
    public override string TrayOpenLogFolder => "打开日志文件夹(&L)";
    public override string TrayExit => "退出(&X)";

    // ================= 连接状态 =================

    public override string Status(PeerStatus status) => status.State switch
    {
        PeerState.Disabled => "已停用",
        PeerState.NotConfigured => "尚未配对（点击“配对”查找对方电脑）",
        PeerState.Searching => "正在查找",
        PeerState.Unpaired => "未配对",
        PeerState.Connected => $"已连接到 {status.PeerHostname}",
        PeerState.Offline => "离线",
        _ => PeerIssueText(status.Issue, status.Port, status.Detail, status.PeerAppVersion),
    };

    public override string IncomingBlockedWarning => "对方目前无法连接到本机，对方发来的文件会失败。请检查本机防火墙是否允许 Foldspace。";

    public override string PeerIssueText(PeerIssue issue, int port = 0, string? detail = null, string? peerVersion = null) => issue switch
    {
        PeerIssue.LocalAddressMissing => "本机 IP 已不存在（网卡已禁用、网线已拔出或 IP 已更改）",
        PeerIssue.PortInUse => $"端口 {port} 已被其他程序占用",
        PeerIssue.ListenFailed => $"无法监听端口 {port}：{detail}",
        PeerIssue.VersionIncompatible => $"版本不兼容（本机 {ProtocolConstants.AppVersion}，对方 {peerVersion ?? "?"}），请将两台电脑更新到相同版本",
        PeerIssue.PairingMismatch => "配对密钥不符：对方的设备证书已更改（可能重装过系统）。请解除配对后重新配对。",
        PeerIssue.PeerHasOtherPairing => "配对密钥不符：对方记录的是另一台设备。请在对方解除配对后重新配对。",
        PeerIssue.PeerRejectsAddress => "对方拒绝连接：对方设置的“对方 IP”不是本机",
        _ => "错误",
    };

    // ================= 设置窗口 =================

    public override string SettingsTitle => "Foldspace 设置";
    public override string Ok => "确定";
    public override string Cancel => "取消";
    public override string Apply => "应用(&A)";
    public override string TabGeneral => "常规";
    public override string TabConnection => "连接";
    public override string TabReceive => "接收";

    public override string GeneralDescription => "把文件或文件夹拖到桌面上的“Foldspace”，就会发送到对方电脑。";
    public override string GroupStatus => "连接状态";
    public override string LabelStatus => "状态:";
    public override string LabelPeerComputer => "对方电脑:";
    public override string ButtonTest => "测试连接(&T)";
    public override string ButtonPair => "配对(&P)…";
    public override string ButtonUnpair => "解除配对(&U)";
    public override string ButtonDisableService => "停用服务(&D)";
    public override string ButtonEnableService => "启用服务(&E)";
    public override string GroupSendFiles => "发送文件";
    public override string DropZoneText => "将文件或文件夹拖放到这里。\n一次要传很多项目时，建议拖放它们的上级文件夹。";
    public override string StartWithWindows => "开机时自动启动(&S)";

    public override string GroupLocal => "本机";
    public override string LabelComputerName => "计算机名:";
    public override string LabelNetworkAdapter => "网络适配器(&N):";
    public override string LabelLocalPort => "端口(&O):";
    public override string LabelFingerprint => "设备指纹:";
    public override string GroupPeer => "对方电脑";
    public override string LabelPeerIp => "IP 地址(&I):";
    public override string LabelPeerPort => "端口(&R):";
    public override string GroupSecurity => "安全";
    public override string Encryption => "加密传输的文件内容(&C)";
    public override string EncryptionNote => "身份验证始终加密。关闭此选项可提升速度，但文件内容将以明文传输。只要任一方开启，传输就会加密。";
    public override string PublicNetworkWarning => "当前网络配置文件为“公用”，Windows 防火墙可能会阻止对方连接。";
    public override string CannotConnectLink => "无法连接?";
    public override string FirewallSetupLink => "允许 Foldspace 通过 Windows 防火墙…";

    public override string GroupReceiveFolder => "接收文件夹";
    public override string ReceiveFolderNote => "从对方收到的文件将保存在此文件夹:";
    public override string Browse => "浏览(&B)...";
    public override string BrowseTitle => "选择接收文件夹";
    public override string GroupConflict => "接收文件夹中已有同名项目时";
    public override string PolicyRename => "自动重命名(&R)，例如 report (1).pdf";
    public override string PolicyOverwrite => "覆盖现有文件(&O)";
    public override string PolicySkip => "跳过，不接收同名文件(&S)";
    public override string ConflictNote => "收到的是文件夹时：自动重命名会另存整个文件夹；覆盖和跳过会合并到现有文件夹。";
    public override string AskBeforeReceive => "接收前先询问(&K)";
    public override string AskBeforeReceiveNote => "60 秒内未响应视为拒绝。";

    public override string InvalidLocalPort => "本机端口必须是 1024 到 65535 之间的数字。";
    public override string InvalidPeerIp => "对方 IP 地址格式不正确，例如 192.168.1.20。";
    public override string PeerIpSameAsLocal => "对方 IP 地址不能与本机相同。";
    public override string InvalidPeerPort => "对方端口必须是 1024 到 65535 之间的数字。";
    public override string InvalidReceiveFolder => "请输入接收文件夹的完整路径，例如 C:\\Users\\me\\Downloads\\Foldspace。";
    public override string LocalPortChanged(int port) =>
        $"本机端口已更改为 {port}。\n\n请在对方电脑的“连接 > 对方电脑 > 端口”中也改为 {port}，否则对方将无法连接。";

    public override string Testing => "正在测试连接...";

    public override string TestResult(TestConnectionResult r) => r.Outcome switch
    {
        TestOutcome.Success =>
            $"连接成功，延迟 {r.RoundTripMs:0.#}\u00A0ms\n{r.PeerHostname}（版本 {r.PeerAppVersion}），{(r.Paired ? "已配对" : "未配对")}",
        TestOutcome.NotConfigured => "尚未设置对方 IP",
        TestOutcome.Timeout => $"连接超时（{r.Endpoint}）：对方未运行 Foldspace 或服务已停用、IP 错误或不在同一网段，或被防火墙阻止",
        TestOutcome.Refused => $"无法连接（{r.Endpoint}）：对方未运行、服务已停用，或端口设置错误",
        TestOutcome.Unreachable => $"无法访问 {r.Endpoint}：请确认两台电脑在同一网段",
        TestOutcome.SocketError => $"无法连接（{r.Endpoint}）：{r.Detail}",
        TestOutcome.TlsFailed => $"安全连接失败（{r.Endpoint}）：该端口上的程序可能不是 Foldspace",
        TestOutcome.NoHandshakeResponse => "对方没有响应握手",
        TestOutcome.BadResponse => "对方的握手响应不正确",
        TestOutcome.VersionIncompatible => PeerIssueText(PeerIssue.VersionIncompatible, peerVersion: r.PeerAppVersion),
        TestOutcome.PairingMismatch => PeerIssueText(PeerIssue.PairingMismatch),
        TestOutcome.PeerHasOtherPairing => PeerIssueText(PeerIssue.PeerHasOtherPairing),
        TestOutcome.PeerRejectsAddress => PeerIssueText(PeerIssue.PeerRejectsAddress),
        TestOutcome.NoHeartbeat => "握手成功但心跳没有响应",
        _ => r.Outcome.ToString(),
    };

    public override string UnpairConfirm(string host) => $"确定要解除与 {host} 的配对吗?\n\n解除后需要重新配对才能传输文件。";
    public override string UnpairDone => "已解除配对。";
    public override string ConfirmPairingCode => "请在两台电脑上确认配对码...";
    public override string FirewallHelpTitle => "无法连接?";
    public override string FirewallHelp =>
        "请在两台电脑上依次确认:\n\n" +
        "1. Foldspace 正在运行，且服务已启用。\n" +
        "2. 两台电脑在同一网段（例如都是 192.168.1.x）。\n" +
        "3. “对方电脑”的 IP 地址和端口，与对方“本机”的设置一致。\n" +
        "4. Windows 防火墙允许 Foldspace：在“连接”选项卡点击“允许 Foldspace 通过 Windows 防火墙”（需要管理员权限）。\n" +
        "    没有这个链接时，表示已经设置好了。";
    public override string ConfirmExitWhileBusy => "有传输正在进行，退出程序会中断传输。确定要退出吗?";
    public override string ConfirmDisableWhileBusy => "有传输正在进行，停用服务会中断传输。确定要停用吗?";

    // ================= 配对 =================

    public override string PairingHeading(string host, string code, bool initiator) =>
        initiator ? $"确认与 {host} 配对: {code}" : $"{host} 请求配对: {code}";
    public override string PairingText(string host, string code) =>
        $"请确认 {host} 上显示的配对码也是 {code}。\n数字相同时点击“确认”；不同时请点击“拒绝”，可能有其他设备介入。";
    public override string PairingConfirm => "确认(&Y)";
    public override string PairingReject => "拒绝(&N)";
    public override string PairingFingerprint(string fingerprint) => $"对方设备指纹: {fingerprint}";
    public override string PairingShowFingerprint => "显示设备指纹";
    public override string PairingHideFingerprint => "隐藏设备指纹";
    public override string PairingWaiting(string host) => $"正在等待 {host} 确认...";
    public override string SecondsLeft(int seconds) => $"剩余 {seconds} 秒";
    public override string TimedOut => "已超时";
    public override string PairingSucceededTitle => "配对成功";
    public override string PairingFailedTitle => "配对失败";

    public override string Pairing(PairingOutcome o)
    {
        var host = o.PeerHostname ?? ThePeer;
        return o.Result switch
        {
            PairingResult.Success => $"已与 {host} 配对",
            PairingResult.NotConnected => "无法连接到对方。请确认对方的 Foldspace 正在运行，且两台在同一网段",
            PairingResult.InProgress => "配对正在进行中",
            PairingResult.PeerCannotPair => $"{host} 无法配对：{(o.PeerReason is { } r ? PairRejectText(r) : "未知原因")}",
            PairingResult.BadResponse => "对方的配对响应不正确",
            PairingResult.Timeout => "配对超时（60 秒内未完成）",
            PairingResult.ConnectionLost => "连接中断，配对失败",
            PairingResult.Rejected => "已拒绝配对",
            PairingResult.RejectedByPeer => $"{host} 拒绝了配对",
            PairingResult.VerificationFailed => "配对验证失败，可能有其他设备介入，请重试",
            PairingResult.Superseded => "两端同时发起配对，改由对方发起",
            PairingResult.ServiceStopped => "服务已停止，配对已取消",
            _ => o.Result.ToString(),
        };
    }

    // ================= 传输窗口 =================

    public override string TransfersTitle => "Foldspace - 传输";
    public override string NoTransfers => "当前没有传输。";

    public override string JobTitle(TransferJob job)
    {
        var items = Items(job.ItemName, job.ItemCount);
        var host = job.PeerHostname ?? ThePeer;
        return job.Direction == TransferDirection.Send
            ? $"↑ {Verb(job, "已发送 ", "正在发送 ")}{items} 到 {host}"
            : $"↓ {Verb(job, "已接收 ", "正在接收 ")}{items}，来自 {host}";
    }

    public override string JobHeadline(TransferJobState state, double percent) => state switch
    {
        TransferJobState.Queued => "排队中",
        TransferJobState.Preparing => "正在准备...",
        TransferJobState.WaitingForPeer => "等待对方...",
        TransferJobState.Finalizing => "即将完成...",
        TransferJobState.Completed => "已完成",
        TransferJobState.Failed => "失败",
        TransferJobState.Cancelled => "已取消",
        _ => $"已完成 {percent:0}%",
    };

    public override string Speed(double megabytesPerSecond) => $"速度: {megabytesPerSecond:0.0} MB/s";
    public override string TimeLeft(TimeSpan? left) => left is { } t ? $"剩余时间: {Duration(t)}" : "剩余时间: 正在计算";

    public override string Job(JobNote note, TransferDirection direction)
    {
        var verb = direction == TransferDirection.Send ? "发送" : "接收";
        return note.Issue switch
        {
            JobIssue.NothingToSend => note.Path is { } path && note.FileIssue is { } fi
                ? $"没有可发送的项目（{path}：{FileIssueText(fi)}）"
                : "没有可发送的项目",
            JobIssue.DriveRoot => "无法发送整个磁盘，请拖入文件夹",
            JobIssue.ServiceDisabled => "服务已停用，请先在设置中启用服务",
            JobIssue.NotConfigured => "尚未设置对方 IP",
            JobIssue.NotPaired => "尚未与对方配对",
            JobIssue.PeerError => "当前无法连接：" + PeerIssueText(note.PeerIssue ?? PeerIssue.None),
            JobIssue.PeerOffline => "对方离线",
            JobIssue.Rejected => note.Rejection is { } rr ? RejectText(rr, note.RequiredBytes, note.AvailableBytes) : "对方拒绝接收",
            JobIssue.NoResponse => "对方没有响应",
            JobIssue.ConnectionLost => "连接中断",
            JobIssue.PairingMismatch => PeerIssueText(PeerIssue.PairingMismatch),
            JobIssue.DataChannelTimeout => "对方没有开始发送",
            JobIssue.Cancelled => CancelText(note.Cause ?? CancelReason.User),
            JobIssue.Interrupted => direction == TransferDirection.Send
                ? $"{CancelText(note.Cause ?? CancelReason.ConnectionLost)}，对方保留了 {note.Count} 个已完成的文件"
                : $"{CancelText(note.Cause ?? CancelReason.ConnectionLost)}，已保留 {note.Count} 个已完成的文件",
            JobIssue.Aborted => CancelText(note.Cause ?? CancelReason.ConnectionLost),
            JobIssue.AllFailed => $"所有文件都{verb}失败",
            JobIssue.SomeProblems => $"{note.Count} 个项目未{verb}",
            JobIssue.Unexpected => $"发生意外错误：{note.Detail}",
            _ => note.Issue.ToString(),
        };
    }

    // ================= 通知 =================

    public override string CannotSend => "无法发送";
    public override string PeerOnlineTitle => "对方已上线";
    public override string PeerOnlineText(string host) => $"已重新连接到 {host}";
    public override string PeerOfflineTitle => "对方已离线";
    public override string PeerOfflineText(string host) => $"与 {host} 的连接已中断，将在后台自动重连";
    public override string SentTitle(int files, string size, string host) => $"已发送 {files} 个文件（{size}）到 {host}";
    public override string ReceivedTitle(int files, string size, string host) => $"已收到 {files} 个文件（{size}），来自 {host}";
    public override string NoNewFilesTitle(bool send, string host) =>
        send ? $"已发送到 {host}，但没有新增任何文件" : $"没有收到新文件（来自 {host}）";
    public override string IncomingRejectedTitle(string host) => $"无法接收 {host} 发来的文件";
    public override string IncomingRejectedText(IncomingRejection rejection, string receiveFolder) => rejection.Reason switch
    {
        RejectReason.InsufficientSpace =>
            $"磁盘空间不足：需要 {Format.Bytes(rejection.RequiredBytes)}，剩余 {Format.Bytes(rejection.AvailableBytes)}。",
        _ => $"接收文件夹无法使用：{receiveFolder}。请在设置中确认文件夹可以写入。",
    };
    public override string SendFailed => "发送失败";
    public override string ReceiveFailed => "接收失败";
    public override string SendCancelled => "发送已取消";
    public override string ReceiveCancelled => "接收已取消";
    public override string SleepCancelledTitle => "传输已取消";
    public override string SleepCancelledText => "电脑进入睡眠，正在进行的传输已取消";
    public override string StillRunningTitle => "Foldspace 仍在后台运行";
    public override string StillRunningText => "可以从右下角的系统托盘图标打开设置，或从菜单退出程序。";
    public override string ShortcutCreatedTitle => "已创建桌面快捷方式";
    public override string ShortcutCreatedText => "把文件拖到桌面上的“Foldspace”就会发送到对方";
    public override string ShortcutFailedTitle => "无法创建桌面快捷方式";
    public override string ShortcutDescription => "把文件或文件夹拖到这里，就会发送到另一台电脑";

    public override string UninstallShortcutName => "卸载 Foldspace";

    public override string DiscoveryTitle => "选择要配对的电脑";

    public override string TraySpeedTest => "网络速度测试(&N)";
    public override string SpeedTestTitle => "网络速度测试";
    public override string SpeedTestRunning(string host) => $"正在测试本机到 {host} 的网络速度，约 15 秒…";
    public override string SpeedTestNotConnected => "需要先与已配对的电脑连接（绿灯）才能测速。";
    public override string SpeedTestResultText(string host, SpeedTestResult plain, SpeedTestResult parallel, SpeedTestResult encrypted) =>
        $"本机 → {host}\n\n不加密，1 条连接：{plain.Mbps:F0} Mbps（{plain.MegabytesPerSecond:F1} MB/s）\n" +
        $"不加密，4 条连接：{parallel.Mbps:F0} Mbps（{parallel.MegabytesPerSecond:F1} MB/s）\n" +
        $"加密，1 条连接：{encrypted.Mbps:F0} Mbps（{encrypted.MegabytesPerSecond:F1} MB/s）\n\n" +
        "测试方式：从内存发送数据 5 秒，对方收到后直接丢弃，不读写磁盘。\n" +
        "4 条：同时打开 4 条连接，速度为总和。\n" +
        "加密：使用与传文件相同的 TLS 加密。";
    public override string SpeedTestFailed(string detail) => $"测速失败：{detail}";
    public override string DiscoveryDescription => "下面是同一网段中正在运行 Foldspace 的电脑。选择要配对的那一台，两边会显示同一组 6 位数字让你确认。";
    public override string ColumnComputer => "电脑名称";
    public override string ColumnAddress => "IP 地址";
    public override string ColumnStatus => "状态";
    public override string DiscoverySearching => "正在搜索…";
    public override string DiscoveryFound(int count) => $"找到 {count} 台电脑。";
    public override string DiscoveryNone =>
        "找不到其他正在运行 Foldspace 的电脑。请确认对方已打开 Foldspace、两台在同一网段；也可以在“连接”选项卡手动输入对方 IP。";
    public override string DiscoveryStatus(DiscoveredPeer peer) =>
        !peer.IsCompatible ? $"版本不兼容（{peer.AppVersion}）"
        : peer.PairedWithMe ? "已与本机配对"
        : peer.Paired ? "已与其他电脑配对"
        : "可以配对";
    public override string ButtonSearchAgain => "重新搜索(&R)";
    public override string ButtonPairSelected => "配对(&P)";
    public override string ConnectingTo(string host) => $"正在连接到 {host}…";
    public override string UninstallButton => "卸载(&U)";
    public override string UninstallConfirm(string receiveFolder, bool removesFirewallRules, bool transfersActive) =>
        $"要卸载 Foldspace 吗?\n\n将删除程序、设置与配对、日志、桌面快捷方式和开始菜单项。接收文件夹中的文件会保留：\n{receiveFolder}"
        + (removesFirewallRules ? "\n\n还会删除 Windows 防火墙中的 Foldspace 规则，需要管理员权限。" : "")
        + (transfersActive ? "\n\n有传输正在进行，卸载会中断传输。" : "");
    public override string UninstallDone(string receiveFolder, bool firewallRulesLeft) =>
        $"Foldspace 已卸载\n\n接收文件夹中的文件已保留：\n{receiveFolder}"
        + (firewallRulesLeft ? "\n\n没有获得管理员权限，Windows 防火墙中的 Foldspace 规则没有删除。可以在“Windows Defender 防火墙 > 高级设置 > 入站规则”中手动删除。" : "");
    public override string IncomingOfferTitle(string host) => $"{host} 想给你发送文件";
    public override string IncomingOfferText(string items, int files, string size) => $"{items}：{files} 个文件（{size}）";
    public override string Accept => "接收";
    public override string Reject => "拒绝";

    // ================= 错误 =================

    public override string UnexpectedError(string message) => $"发生错误：{message}\n\n程序将继续运行，详细信息已写入日志。";
    public override string StartupFailed(string message, string logDir) => $"Foldspace 无法启动：{message}\n\n详细信息请查看日志：{logDir}";

    // ================= 原因代码 =================

    public override string FileIssueText(FileIssue issue) => issue switch
    {
        FileIssue.NotFound => "找不到项目",
        FileIssue.ReparsePoint => "符号链接或 junction 不会发送",
        FileIssue.FolderUnreadable => "无法读取文件夹内容（权限不足）",
        FileIssue.NoReadPermission => "没有读取权限",
        FileIssue.Locked => "文件被其他程序锁定或无法打开",
        FileIssue.ReadFailed => "读取源文件失败",
        FileIssue.SourceModified => "发送过程中源文件被修改",
        FileIssue.InvalidName => "名称在 Windows 上不合法",
        FileIssue.HashMismatch => "完整性校验失败（哈希不符）",
        FileIssue.WriteFailed => "无法写入接收文件夹",
        FileIssue.SkippedExisting => "接收文件夹中已有同名项目",
        FileIssue.FolderExists => "接收文件夹中已有同名文件夹",
        FileIssue.FileExists => "接收文件夹中已有同名文件，无法创建文件夹",
        FileIssue.OverwriteFailed => "旧文件为只读或被其他程序锁定，无法覆盖",
        FileIssue.MoveFailed => "无法移动到接收文件夹",
        _ => issue.ToString(),
    };

    public override string RejectText(RejectReason reason, long requiredBytes, long availableBytes) => reason switch
    {
        RejectReason.NotPaired => "对方尚未与你配对，拒绝接收",
        RejectReason.Busy => "对方正在接收其他文件",
        RejectReason.Declined => "对方拒绝接收",
        RejectReason.DeclineTimeout => "对方未在 60 秒内响应，视为拒绝",
        RejectReason.ReceiveFolderUnavailable => "对方的接收文件夹无法写入",
        RejectReason.InsufficientSpace => $"对方磁盘空间不足：需要 {Format.Bytes(requiredBytes)}，剩余 {Format.Bytes(availableBytes)}",
        RejectReason.InvalidOffer => "传输请求内容不合法",
        _ => reason.ToString(),
    };

    public override string CancelText(CancelReason reason) => reason switch
    {
        CancelReason.User => "已取消",
        CancelReason.Peer => "对方取消了传输",
        CancelReason.PeerSecurityViolation => "对方因安全问题中断了传输",
        CancelReason.PeerDiskFull => "对方磁盘空间不足，传输已取消",
        CancelReason.ServiceStopped => "服务已停止",
        CancelReason.PeerOffline => "对方离线，传输中断",
        CancelReason.Unpaired => "配对已解除",
        CancelReason.ConnectionLost => "连接中断",
        CancelReason.SecurityViolation => "对方发来了不安全的文件路径，已中断传输",
        CancelReason.DiskFull => "磁盘空间不足，传输已取消",
        CancelReason.CleanupFailed => "无法清理未完成的临时文件，已放弃本次接收的所有文件",
        _ => reason.ToString(),
    };

    public override string PairRejectText(PairRejectReason reason) => reason switch
    {
        PairRejectReason.Busy => "对方正在进行其他配对",
        PairRejectReason.UserRejected => "对方拒绝了配对",
        PairRejectReason.InvalidRequest => "配对请求格式错误",
        PairRejectReason.VerificationFailed => "配对验证失败",
        _ => reason.ToString(),
    };
}
