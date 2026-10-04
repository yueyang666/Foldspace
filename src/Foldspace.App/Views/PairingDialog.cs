using Foldspace.App.Platform;
using Foldspace.Core.Net;
using Foldspace.Core.Protocol;
using Foldspace.Localization;

namespace Foldspace.App.Views;

/// <summary>
/// 配對確認：直接使用 Windows 內建的 TaskDialog。
/// 兩端同時顯示同一組 6 位數字，雙方都按「確認」才完成；對方拒絕、逾時或連線中斷時自動關閉。
/// </summary>
public static class PairingDialog
{
    public static void Show(PairingPrompt prompt)
    {
        var t = Strings.Current;
        var confirm = new TaskDialogButton(t.PairingConfirm) { AllowCloseDialog = false };
        var reject = new TaskDialogButton(t.PairingReject);
        var deadline = DateTime.UtcNow + ProtocolConstants.PairingTimeout;

        var page = new TaskDialogPage
        {
            Caption = "Foldspace",
            Icon = TaskDialogIcon.Shield,
            Heading = t.PairingHeading(prompt.PeerHostname, prompt.Code, prompt.IsInitiator),
            Text = t.PairingText(prompt.PeerHostname, prompt.Code),
            Footnote = new TaskDialogFootnote(Countdown(deadline)),
            Expander = new TaskDialogExpander(t.PairingFingerprint(prompt.PeerFingerprint))
            {
                CollapsedButtonText = t.PairingShowFingerprint,
                ExpandedButtonText = t.PairingHideFingerprint,
            },
            Buttons = { confirm, reject },
            DefaultButton = confirm,
            AllowCancel = true,
            SizeToContent = true,
        };

        var decided = false;
        confirm.Click += (_, _) =>
        {
            decided = true;
            prompt.Confirm();
            page.Heading = t.PairingWaiting(prompt.PeerHostname);
            confirm.Enabled = false;
        };

        // 每 0.5 秒更新倒數；對方拒絕、逾時、連線中斷或配對完成時關閉對話框。
        using var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            if (page.BoundDialog is null)
                return;
            page.Footnote!.Text = Countdown(deadline);
            if (prompt.Closed.IsCancellationRequested)
                page.BoundDialog.Close();
        };
        timer.Start();

        // 配對可能由對方發起、本程式正在背景：要出現在最前面並取得焦點。
        var result = Dialogs.ShowOnTop(page);
        timer.Stop();
        if (!decided && result != confirm)
            prompt.Reject(); // 按「拒絕」或直接關閉視窗都視為拒絕
    }

    private static string Countdown(DateTime deadline)
    {
        var left = deadline - DateTime.UtcNow;
        return left > TimeSpan.Zero ? Strings.Current.SecondsLeft((int)left.TotalSeconds) : Strings.Current.TimedOut;
    }
}
