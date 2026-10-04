using System.Runtime.InteropServices;
using Foldspace.App.Platform;
using Foldspace.Localization;

namespace Foldspace.App.Views;

/// <summary>
/// 訊息框與確認框。用 TaskDialog 而不用 MessageBox：MessageBox 的按鈕（是/否、確定）跟著 Windows 的語言，
/// TaskDialog 可以自訂按鈕文字，跟著 Foldspace 的語言。
/// 訊息裡第一個空行之前的文字當成標題（主要指示）。
/// </summary>
public static class Dialogs
{
    public static void Show(IWin32Window? owner, string caption, string message, TaskDialogIcon icon)
    {
        var ok = new TaskDialogButton(Strings.Current.Ok);
        ShowPage(owner, NewPage(caption, message, icon, ok));
    }

    /// <summary>確認框：<paramref name="confirmText"/> 是動作按鈕（例如「解除配對」），預設按鈕是「取消」。</summary>
    public static bool Confirm(IWin32Window? owner, string caption, string message, string confirmText, TaskDialogIcon icon)
    {
        var confirm = new TaskDialogButton(confirmText);
        var cancel = new TaskDialogButton(Strings.Current.Cancel);
        var page = NewPage(caption, message, icon, confirm, cancel);
        page.DefaultButton = cancel;
        return ShowPage(owner, page) == confirm;
    }

    private static TaskDialogPage NewPage(string caption, string message, TaskDialogIcon icon, params TaskDialogButton[] buttons)
    {
        var split = message.IndexOf("\n\n", StringComparison.Ordinal);
        var page = new TaskDialogPage
        {
            Caption = caption,
            Icon = icon,
            Heading = split < 0 ? null : message[..split],
            Text = split < 0 ? message : message[(split + 2)..],
            AllowCancel = true, // Esc 與標題列的關閉鈕 = 取消
        };
        foreach (var button in buttons)
            page.Buttons.Add(button);
        return page;
    }

    private static TaskDialogButton ShowPage(IWin32Window? owner, TaskDialogPage page)
    {
        return owner is not null
            ? TaskDialog.ShowDialog(owner, page, TaskDialogStartupLocation.CenterOwner)
            : ShowOnTop(page);
    }

    /// <summary>沒有父視窗的對話框（系統匣、「設定 &gt; 應用程式」、對方發起的配對），出現在最前面並取得焦點。</summary>
    public static TaskDialogButton ShowOnTop(TaskDialogPage page)
    {
        // 從系統匣或「設定 > 應用程式」叫出、沒有父視窗時：用一個看不見的最上層視窗當擁有者，
        // 對話框才會出現在其他視窗前面，工作列上也有 Foldspace 圖示。
        // 擁有者放在畫面內並設成全透明：放在畫面外 (-32000, -32000) 會被 Windows 當成最小化，對話框就跟著被壓到後面。
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
        using var host = new Form
        {
            Text = page.Caption ?? "",
            Icon = AppIcon.Create(),
            ShowInTaskbar = true,
            TopMost = true,
            Opacity = 0,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2),
            Size = new Size(1, 1),
        };
        host.Show();
        BringToForeground(host.Handle);
        return TaskDialog.ShowDialog(host, page, TaskDialogStartupLocation.CenterScreen);
    }

    /// <summary>
    /// 剛啟動、沒有前景權限的程序（例如由「設定 &gt; 應用程式」啟動）呼叫 SetForegroundWindow 會被 Windows 擋下，
    /// 只讓工作列按鈕閃爍。擋下時模擬按一下 Alt：Windows 把它當成本程序收到使用者輸入，前景鎖定就會解除。
    /// </summary>
    private static void BringToForeground(IntPtr window)
    {
        if (SetForegroundWindow(window) && GetForegroundWindow() == window)
            return;
        const byte VK_MENU = 0x12;
        const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        SetForegroundWindow(window);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
}
