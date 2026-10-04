using Foldspace.Core;
using Foldspace.Core.Net;
using Foldspace.Core.Protocol;
using Foldspace.Core.Transfer;

namespace Foldspace.Localization;

/// <summary>English (default for every language other than Chinese).</summary>
public sealed class EnglishStrings : Strings
{
    public override string CultureName => "en";

    // ================= Common =================

    public override string ThePeer => "the other computer";
    public override string Version(string version) => $"Version {version}";
    public override string Items(string firstName, int count) => count <= 1 ? firstName : $"{firstName} and {count - 1} more";
    public override string FileCount(int count) => count == 1 ? "1 file" : $"{count} files";
    public override string Duration(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min"
        : span.TotalMinutes >= 1 ? $"{span.Minutes} min {span.Seconds} s"
        : $"{Math.Max(0, span.Seconds)} s";

    // ================= Tray =================

    public override string TrayOpenSettings => "&Settings";
    public override string TrayOpenReceiveFolder => "Open receive &folder";
    public override string TrayTransfers => "Transfer &progress";
    public override string TrayDisableService => "&Disable service";
    public override string TrayEnableService => "&Enable service";
    public override string TrayRecreateShortcut => "Re&create desktop shortcut";
    public override string TrayOpenLogFolder => "Open &log folder";
    public override string TrayExit => "E&xit";

    // ================= Status =================

    public override string Status(PeerStatus status) => status.State switch
    {
        PeerState.Disabled => "Disabled",
        PeerState.NotConfigured => "Not paired yet (click Pair to find the other computer)",
        PeerState.Searching => "Searching",
        PeerState.Unpaired => "Not paired",
        PeerState.Connected => $"Connected to {status.PeerHostname}",
        PeerState.Offline => "Offline",
        _ => PeerIssueText(status.Issue, status.Port, status.Detail, status.PeerAppVersion),
    };

    public override string IncomingBlockedWarning =>
        "The other computer can't connect to this one, so files it sends will fail. Check that the firewall on this computer allows Foldspace.";

    public override string PeerIssueText(PeerIssue issue, int port = 0, string? detail = null, string? peerVersion = null) => issue switch
    {
        PeerIssue.LocalAddressMissing => "This computer's IP address is gone (adapter disabled, cable unplugged, or IP changed)",
        PeerIssue.PortInUse => $"Port {port} is already in use by another program",
        PeerIssue.ListenFailed => $"Can't listen on port {port}: {detail}",
        PeerIssue.VersionIncompatible =>
            $"Incompatible versions (this computer {ProtocolConstants.AppVersion}, other computer {peerVersion ?? "?"}). Update both computers to the same version.",
        PeerIssue.PairingMismatch =>
            "Pairing key mismatch: the other computer's device certificate has changed (it may have been reinstalled). Remove the pairing and pair again.",
        PeerIssue.PeerHasOtherPairing =>
            "Pairing key mismatch: the other computer is paired with a different device. Remove the pairing on that computer and pair again.",
        PeerIssue.PeerRejectsAddress => "Connection refused: the other computer's peer IP setting is not this computer",
        _ => "Error",
    };

    // ================= Settings window =================

    public override string SettingsTitle => "Foldspace Settings";
    public override string Ok => "OK";
    public override string Cancel => "Cancel";
    public override string Apply => "&Apply";
    public override string TabGeneral => "General";
    public override string TabConnection => "Connection";
    public override string TabReceive => "Receive";

    public override string GeneralDescription => "Drag files or folders onto \"Foldspace\" on the desktop to send them to the other computer.";
    public override string GroupStatus => "Connection status";
    public override string LabelStatus => "Status:";
    public override string LabelPeerComputer => "Other computer:";
    public override string ButtonTest => "&Test";
    public override string ButtonPair => "&Pair…";
    public override string ButtonUnpair => "&Unpair";
    public override string ButtonDisableService => "&Disable";
    public override string ButtonEnableService => "&Enable";
    public override string GroupSendFiles => "Send files";
    public override string DropZoneText => "Drag files or folders here.\nTo send many items at once, drag their parent folder instead.";
    public override string StartWithWindows => "&Start Foldspace when Windows starts";

    public override string GroupLocal => "This computer";
    public override string LabelComputerName => "Computer name:";
    public override string LabelNetworkAdapter => "&Network adapter:";
    public override string LabelLocalPort => "P&ort:";
    public override string LabelFingerprint => "Fingerprint:";
    public override string GroupPeer => "Other computer";
    public override string LabelPeerIp => "&IP address:";
    public override string LabelPeerPort => "Po&rt:";
    public override string GroupSecurity => "Security";
    public override string Encryption => "En&crypt file contents";
    public override string EncryptionNote =>
        "Authentication is always encrypted. Turning this off makes transfers faster, but file contents are sent in plain text. Transfers are encrypted if either computer has this on.";
    public override string PublicNetworkWarning => "The current network profile is Public. Windows Firewall may block the other computer from connecting.";
    public override string CannotConnectLink => "Can't connect?";
    public override string FirewallSetupLink => "Allow Foldspace through Windows Firewall…";

    public override string GroupReceiveFolder => "Receive folder";
    public override string ReceiveFolderNote => "Files received from the other computer are saved in this folder:";
    public override string Browse => "&Browse...";
    public override string BrowseTitle => "Choose the receive folder";
    public override string GroupConflict => "When an item with the same name already exists";
    public override string PolicyRename => "&Rename automatically, for example report (1).pdf";
    public override string PolicyOverwrite => "&Overwrite the existing file";
    public override string PolicySkip => "&Skip the file";
    public override string ConflictNote =>
        "For folders: Rename saves the whole folder under a new name; Overwrite and Skip merge into the existing folder.";
    public override string AskBeforeReceive => "As&k before receiving";
    public override string AskBeforeReceiveNote => "No answer within 60 seconds counts as declining.";

    public override string InvalidLocalPort => "The local port must be a number from 1024 to 65535.";
    public override string InvalidPeerIp => "The other computer's IP address isn't valid. Example: 192.168.1.20.";
    public override string PeerIpSameAsLocal => "The other computer's IP address can't be the same as this computer's.";
    public override string InvalidPeerPort => "The other computer's port must be a number from 1024 to 65535.";
    public override string InvalidReceiveFolder => "Enter the full path of the receive folder, for example C:\\Users\\me\\Downloads\\Foldspace.";
    public override string LocalPortChanged(int port) =>
        $"The local port is now {port}.\n\nOn the other computer, also change Connection > Other computer > Port to {port}, or it won't be able to connect.";

    public override string Testing => "Testing the connection...";

    public override string TestResult(TestConnectionResult r) => r.Outcome switch
    {
        TestOutcome.Success =>
            $"Connected, latency {r.RoundTripMs:0.#}\u00A0ms\n{r.PeerHostname} (version {r.PeerAppVersion}), {(r.Paired ? "paired" : "not paired")}",
        TestOutcome.NotConfigured => "The other computer's IP address isn't set",
        TestOutcome.Timeout => $"Timed out ({r.Endpoint}): Foldspace isn't running there or its service is disabled, the IP address is wrong or not on this network, or a firewall is blocking it",
        TestOutcome.Refused => $"Can't connect ({r.Endpoint}): Foldspace isn't running there, its service is disabled, or the port is wrong",
        TestOutcome.Unreachable => $"Can't reach {r.Endpoint}: make sure both computers are on the same network",
        TestOutcome.SocketError => $"Can't connect ({r.Endpoint}): {r.Detail}",
        TestOutcome.TlsFailed => $"Secure connection failed ({r.Endpoint}): the program on that port may not be Foldspace",
        TestOutcome.NoHandshakeResponse => "The other computer didn't answer the handshake",
        TestOutcome.BadResponse => "The other computer sent an invalid handshake reply",
        TestOutcome.VersionIncompatible => PeerIssueText(PeerIssue.VersionIncompatible, peerVersion: r.PeerAppVersion),
        TestOutcome.PairingMismatch => PeerIssueText(PeerIssue.PairingMismatch),
        TestOutcome.PeerHasOtherPairing => PeerIssueText(PeerIssue.PeerHasOtherPairing),
        TestOutcome.PeerRejectsAddress => PeerIssueText(PeerIssue.PeerRejectsAddress),
        TestOutcome.NoHeartbeat => "The handshake succeeded, but the heartbeat got no reply",
        _ => r.Outcome.ToString(),
    };

    public override string UnpairConfirm(string host) => $"Remove the pairing with {host}?\n\nYou'll need to pair again before you can transfer files.";
    public override string UnpairDone => "Pairing removed.";
    public override string ConfirmPairingCode => "Confirm the pairing code on both computers...";
    public override string FirewallHelpTitle => "Can't connect?";
    public override string FirewallHelp =>
        "Check the following on both computers:\n\n" +
        "1. Foldspace is running and its service is enabled.\n" +
        "2. Both computers are on the same network (for example, both 192.168.1.x).\n" +
        "3. The IP address and port under \"Other computer\" match the other computer's \"This computer\" settings.\n" +
        "4. Windows Firewall allows Foldspace: on the Connection tab, click \"Allow Foldspace through Windows Firewall\" (needs administrator rights).\n" +
        "    If the link isn't there, it's already set up.";
    public override string ConfirmExitWhileBusy => "A transfer is in progress. Exiting will stop it. Exit anyway?";
    public override string ConfirmDisableWhileBusy => "A transfer is in progress. Disabling the service will stop it. Disable anyway?";

    // ================= Pairing =================

    public override string PairingHeading(string host, string code, bool initiator) =>
        initiator ? $"Pair with {host}: {code}" : $"{host} wants to pair: {code}";
    public override string PairingText(string host, string code) =>
        $"Make sure {host} shows the same code, {code}.\nIf the numbers match, click Confirm. If they don't, click Reject: another device may be interfering.";
    public override string PairingConfirm => "&Confirm";
    public override string PairingReject => "&Reject";
    public override string PairingFingerprint(string fingerprint) => $"Other device's fingerprint: {fingerprint}";
    public override string PairingShowFingerprint => "Show device fingerprint";
    public override string PairingHideFingerprint => "Hide device fingerprint";
    public override string PairingWaiting(string host) => $"Waiting for {host} to confirm...";
    public override string SecondsLeft(int seconds) => seconds == 1 ? "1 second left" : $"{seconds} seconds left";
    public override string TimedOut => "Timed out";
    public override string PairingSucceededTitle => "Paired";
    public override string PairingFailedTitle => "Pairing failed";

    public override string Pairing(PairingOutcome o)
    {
        var host = o.PeerHostname ?? ThePeer;
        return o.Result switch
        {
            PairingResult.Success => $"Paired with {host}",
            PairingResult.NotConnected => "Couldn't connect to the other computer. Make sure Foldspace is running there and both computers are on the same network.",
            PairingResult.InProgress => "Pairing is already in progress",
            PairingResult.PeerCannotPair => $"{host} can't pair: {(o.PeerReason is { } r ? PairRejectText(r) : "unknown reason")}",
            PairingResult.BadResponse => "The other computer sent an invalid pairing reply",
            PairingResult.Timeout => "Pairing timed out (not finished within 60 seconds)",
            PairingResult.ConnectionLost => "The connection was lost, so pairing failed",
            PairingResult.Rejected => "Pairing rejected",
            PairingResult.RejectedByPeer => $"{host} rejected the pairing",
            PairingResult.VerificationFailed => "Pairing verification failed. Another device may be interfering. Try again.",
            PairingResult.Superseded => "Both computers started pairing at once; continuing with the other computer's request",
            PairingResult.ServiceStopped => "The service stopped, so pairing was cancelled",
            _ => o.Result.ToString(),
        };
    }

    // ================= Transfer window =================

    public override string TransfersTitle => "Foldspace - Transfers";
    public override string NoTransfers => "No transfers.";

    public override string JobTitle(TransferJob job)
    {
        var items = Items(job.ItemName, job.ItemCount);
        var host = job.PeerHostname ?? ThePeer;
        return job.Direction == TransferDirection.Send
            ? $"↑ {Verb(job, "Sent ", "Sending ")}{items} to {host}"
            : $"↓ {Verb(job, "Received ", "Receiving ")}{items} from {host}";
    }

    public override string JobHeadline(TransferJobState state, double percent) => state switch
    {
        TransferJobState.Queued => "Queued",
        TransferJobState.Preparing => "Preparing...",
        TransferJobState.WaitingForPeer => "Waiting for the other computer...",
        TransferJobState.Finalizing => "Finishing...",
        TransferJobState.Completed => "Done",
        TransferJobState.Failed => "Failed",
        TransferJobState.Cancelled => "Cancelled",
        _ => $"{percent:0}% complete",
    };

    public override string Speed(double megabytesPerSecond) => $"Speed: {megabytesPerSecond:0.0} MB/s";
    public override string TimeLeft(TimeSpan? left) => left is { } t ? $"Time left: {Duration(t)}" : "Time left: calculating";

    public override string Job(JobNote note, TransferDirection direction)
    {
        var verb = direction == TransferDirection.Send ? "sent" : "received";
        return note.Issue switch
        {
            JobIssue.NothingToSend => note.Path is { } path && note.FileIssue is { } fi
                ? $"Nothing to send ({path}: {FileIssueText(fi)})"
                : "Nothing to send",
            JobIssue.DriveRoot => "Can't send an entire drive. Drag a folder instead.",
            JobIssue.ServiceDisabled => "The service is disabled. Enable it in Settings first.",
            JobIssue.NotConfigured => "The other computer's IP address isn't set",
            JobIssue.NotPaired => "Not paired with the other computer",
            JobIssue.PeerError => "Can't connect right now: " + PeerIssueText(note.PeerIssue ?? PeerIssue.None),
            JobIssue.PeerOffline => "The other computer is offline",
            JobIssue.Rejected => note.Rejection is { } rr ? RejectText(rr, note.RequiredBytes, note.AvailableBytes) : "The other computer declined",
            JobIssue.NoResponse => "The other computer didn't respond",
            JobIssue.ConnectionLost => "The connection was lost",
            JobIssue.PairingMismatch => PeerIssueText(PeerIssue.PairingMismatch),
            JobIssue.DataChannelTimeout => "The other computer never started sending",
            JobIssue.Cancelled => CancelText(note.Cause ?? CancelReason.User),
            JobIssue.Interrupted => direction == TransferDirection.Send
                ? $"{CancelText(note.Cause ?? CancelReason.ConnectionLost)}. The other computer kept {FileCount(note.Count)} that finished."
                : $"{CancelText(note.Cause ?? CancelReason.ConnectionLost)}. Kept {FileCount(note.Count)} that finished.",
            JobIssue.Aborted => CancelText(note.Cause ?? CancelReason.ConnectionLost),
            JobIssue.AllFailed => $"No files could be {verb}",
            JobIssue.SomeProblems => note.Count == 1 ? $"1 item wasn't {verb}" : $"{note.Count} items weren't {verb}",
            JobIssue.Unexpected => $"Unexpected error: {note.Detail}",
            _ => note.Issue.ToString(),
        };
    }

    // ================= Notifications =================

    public override string CannotSend => "Can't send";
    public override string PeerOnlineTitle => "Other computer is online";
    public override string PeerOnlineText(string host) => $"Reconnected to {host}";
    public override string PeerOfflineTitle => "Other computer is offline";
    public override string PeerOfflineText(string host) => $"Lost the connection to {host}. Foldspace will keep trying to reconnect.";
    public override string SentTitle(int files, string size, string host) => $"Sent {FileCount(files)} ({size}) to {host}";
    public override string ReceivedTitle(int files, string size, string host) => $"Received {FileCount(files)} ({size}) from {host}";
    public override string NoNewFilesTitle(bool send, string host) =>
        send ? $"Sent to {host}, but no new files were added" : $"No new files from {host}";
    public override string IncomingRejectedTitle(string host) => $"Couldn't receive files from {host}";
    public override string IncomingRejectedText(IncomingRejection rejection, string receiveFolder) => rejection.Reason switch
    {
        RejectReason.InsufficientSpace =>
            $"Not enough disk space: {Format.Bytes(rejection.RequiredBytes)} needed, {Format.Bytes(rejection.AvailableBytes)} free.",
        _ => $"The receive folder can't be used: {receiveFolder}. Check in Settings that the folder can be written to.",
    };
    public override string SendFailed => "Send failed";
    public override string ReceiveFailed => "Receive failed";
    public override string SendCancelled => "Send cancelled";
    public override string ReceiveCancelled => "Receive cancelled";
    public override string SleepCancelledTitle => "Transfers cancelled";
    public override string SleepCancelledText => "The computer went to sleep, so transfers in progress were cancelled";
    public override string StillRunningTitle => "Foldspace is still running";
    public override string StillRunningText => "Open Settings from the Foldspace icon in the notification area, or exit from its menu.";
    public override string ShortcutCreatedTitle => "Desktop shortcut created";
    public override string ShortcutCreatedText => "Drag files onto \"Foldspace\" on the desktop to send them to the other computer";
    public override string ShortcutFailedTitle => "Couldn't create the desktop shortcut";
    public override string ShortcutDescription => "Drag files or folders here to send them to the other computer";

    public override string UninstallShortcutName => "Uninstall Foldspace";

    public override string DiscoveryTitle => "Choose a computer to pair with";

    public override string TraySpeedTest => "&Network speed test";
    public override string SpeedTestTitle => "Network speed test";
    public override string SpeedTestRunning(string host) => $"Testing the network speed from this computer to {host}. This takes about 15 seconds…";
    public override string SpeedTestNotConnected => "Connect to the paired computer first (green status) to run a speed test.";
    public override string SpeedTestResultText(string host, SpeedTestResult plain, SpeedTestResult parallel, SpeedTestResult encrypted) =>
        $"This computer → {host}\n\nUnencrypted, 1 connection: {plain.Mbps:F0} Mbps ({plain.MegabytesPerSecond:F1} MB/s)\n" +
        $"Unencrypted, 4 connections: {parallel.Mbps:F0} Mbps ({parallel.MegabytesPerSecond:F1} MB/s)\n" +
        $"Encrypted, 1 connection: {encrypted.Mbps:F0} Mbps ({encrypted.MegabytesPerSecond:F1} MB/s)\n\n" +
        "How it works: data is sent from memory for 5 seconds and discarded on arrival, with no disk reads or writes.\n" +
        "4 connections: 4 connections at once; the speed is their total.\n" +
        "Encrypted: uses the same TLS encryption as file transfers.";
    public override string SpeedTestFailed(string detail) => $"Speed test failed: {detail}";
    public override string DiscoveryDescription => "These computers on your network are running Foldspace. Choose the one to pair with; both computers will show the same 6-digit code for you to confirm.";
    public override string ColumnComputer => "Computer";
    public override string ColumnAddress => "IP address";
    public override string ColumnStatus => "Status";
    public override string DiscoverySearching => "Searching…";
    public override string DiscoveryFound(int count) => count == 1 ? "Found 1 computer." : $"Found {count} computers.";
    public override string DiscoveryNone =>
        "No other computers running Foldspace were found. Make sure Foldspace is open on the other computer and both are on the same network, or enter its IP address on the Connection tab.";
    public override string DiscoveryStatus(DiscoveredPeer peer) =>
        !peer.IsCompatible ? $"Incompatible version ({peer.AppVersion})"
        : peer.PairedWithMe ? "Paired with this computer"
        : peer.Paired ? "Paired with another computer"
        : "Ready to pair";
    public override string ButtonSearchAgain => "Search &again";
    public override string ButtonPairSelected => "&Pair";
    public override string ConnectingTo(string host) => $"Connecting to {host}…";
    public override string UninstallButton => "&Uninstall";
    public override string UninstallConfirm(string receiveFolder, bool removesFirewallRules, bool transfersActive) =>
        $"Uninstall Foldspace?\n\nThis removes the program, its settings and pairing, logs, the desktop shortcut and the Start menu entries. Files in the receive folder are kept:\n{receiveFolder}"
        + (removesFirewallRules ? "\n\nFoldspace's Windows Firewall rules will also be removed, which needs administrator permission." : "")
        + (transfersActive ? "\n\nA transfer is in progress. Uninstalling will stop it." : "");
    public override string UninstallDone(string receiveFolder, bool firewallRulesLeft) =>
        $"Foldspace has been uninstalled\n\nFiles in the receive folder were kept:\n{receiveFolder}"
        + (firewallRulesLeft ? "\n\nFoldspace's Windows Firewall rules weren't removed because administrator permission wasn't granted. You can delete them in Windows Defender Firewall > Advanced settings > Inbound Rules." : "");
    public override string IncomingOfferTitle(string host) => $"{host} wants to send you files";
    public override string IncomingOfferText(string items, int files, string size) => $"{items}: {FileCount(files)} ({size})";
    public override string Accept => "Accept";
    public override string Reject => "Decline";

    // ================= Errors =================

    public override string UnexpectedError(string message) => $"Something went wrong: {message}\n\nFoldspace will keep running. Details were written to the log.";
    public override string StartupFailed(string message, string logDir) => $"Foldspace couldn't start: {message}\n\nSee the log for details: {logDir}";

    // ================= Reason codes =================

    public override string FileIssueText(FileIssue issue) => issue switch
    {
        FileIssue.NotFound => "not found",
        FileIssue.ReparsePoint => "symbolic links and junctions aren't sent",
        FileIssue.FolderUnreadable => "can't read the folder (access denied)",
        FileIssue.NoReadPermission => "no permission to read",
        FileIssue.Locked => "locked by another program or can't be opened",
        FileIssue.ReadFailed => "couldn't read the source file",
        FileIssue.SourceModified => "the source file changed while sending",
        FileIssue.InvalidName => "the name isn't valid on Windows",
        FileIssue.HashMismatch => "integrity check failed (hash mismatch)",
        FileIssue.WriteFailed => "couldn't write to the receive folder",
        FileIssue.SkippedExisting => "an item with the same name already exists",
        FileIssue.FolderExists => "a folder with the same name already exists",
        FileIssue.FileExists => "a file with the same name exists, so the folder can't be created",
        FileIssue.OverwriteFailed => "the existing file is read-only or locked, so it can't be overwritten",
        FileIssue.MoveFailed => "couldn't move it into the receive folder",
        _ => issue.ToString(),
    };

    public override string RejectText(RejectReason reason, long requiredBytes, long availableBytes) => reason switch
    {
        RejectReason.NotPaired => "The other computer isn't paired with you, so it declined",
        RejectReason.Busy => "The other computer is receiving other files",
        RejectReason.Declined => "The other computer declined",
        RejectReason.DeclineTimeout => "The other computer didn't answer within 60 seconds, so it counts as declined",
        RejectReason.ReceiveFolderUnavailable => "The other computer can't write to its receive folder",
        RejectReason.InsufficientSpace =>
            $"Not enough disk space on the other computer: needs {Format.Bytes(requiredBytes)}, {Format.Bytes(availableBytes)} free",
        RejectReason.InvalidOffer => "The transfer request was invalid",
        _ => reason.ToString(),
    };

    public override string CancelText(CancelReason reason) => reason switch
    {
        CancelReason.User => "Cancelled",
        CancelReason.Peer => "The other computer cancelled the transfer",
        CancelReason.PeerSecurityViolation => "The other computer stopped the transfer for security reasons",
        CancelReason.PeerDiskFull => "The other computer ran out of disk space, so the transfer was cancelled",
        CancelReason.ServiceStopped => "The service stopped",
        CancelReason.PeerOffline => "The other computer went offline, so the transfer stopped",
        CancelReason.Unpaired => "The pairing was removed",
        CancelReason.ConnectionLost => "The connection was lost",
        CancelReason.SecurityViolation => "The other computer sent an unsafe file path, so the transfer was stopped",
        CancelReason.DiskFull => "Out of disk space, so the transfer was cancelled",
        CancelReason.CleanupFailed => "Couldn't clean up unfinished temporary files, so all files from this transfer were discarded",
        _ => reason.ToString(),
    };

    public override string PairRejectText(PairRejectReason reason) => reason switch
    {
        PairRejectReason.Busy => "it's pairing with another request",
        PairRejectReason.UserRejected => "the user rejected it",
        PairRejectReason.InvalidRequest => "the pairing request was invalid",
        PairRejectReason.VerificationFailed => "verification failed",
        _ => reason.ToString(),
    };
}
