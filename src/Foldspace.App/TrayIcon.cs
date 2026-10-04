using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Foldspace.Localization;
using Foldspace.Core.Net;

namespace Foldspace.App;

/// <summary>系統匣圖示：顏色跟隨連線狀態；系統匣不能接受拖放，只負責顯示狀態與開啟視窗。</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Dictionary<PeerState, Icon> _icons = [];
    private readonly Icon _baseIcon;
    private readonly ToolStripMenuItem _toggleService;

    public TrayIcon(AppController controller)
    {
        _baseIcon = Platform.AppIcon.Create();

        // System 繪製模式才會像 Windows 原生的系統匣選單；預設項目用粗體。
        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System, ShowImageMargin = false };
        var t = Strings.Current;
        var open = menu.Items.Add(t.TrayOpenSettings, null, (_, _) => controller.ShowSettings());
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add(t.TrayOpenReceiveFolder, null, (_, _) => controller.OpenReceiveFolder());
        menu.Items.Add(t.TrayTransfers, null, (_, _) => controller.ShowTransfers());
        menu.Items.Add(t.TraySpeedTest, null, async (_, _) => await controller.RunSpeedTestAsync());
        menu.Items.Add(new ToolStripSeparator());
        _toggleService = new ToolStripMenuItem(t.TrayDisableService, null, async (_, _) => await controller.ToggleServiceAsync());
        menu.Items.Add(_toggleService);
        menu.Items.Add(t.TrayRecreateShortcut, null, (_, _) => controller.RecreateShortcut());
        menu.Items.Add(t.TrayOpenLogFolder, null, (_, _) => controller.OpenLogFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(t.TrayExit, null, async (_, _) => await controller.ExitAsync());

        _icon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = AppNameText,
            Icon = IconFor(PeerState.Disabled),
            Visible = true,
        };
        // 單擊或雙擊都開啟設定視窗。
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                controller.ShowSettings();
        };
    }

    private static string AppNameText => "Foldspace";

    public void Update(PeerStatus status, bool serviceEnabled)
    {
        _icon.Icon = IconFor(status.State);
        var text = $"{AppNameText} — {Strings.Current.Status(status)}";
        // NotifyIcon.Text 上限 127 字元
        _icon.Text = text.Length > 127 ? text[..127] : text;
        _toggleService.Text = serviceEnabled ? Strings.Current.TrayDisableService : Strings.Current.TrayEnableService;
    }

    public static Color ColorFor(PeerState state) => state switch
    {
        PeerState.Connected => Color.FromArgb(0x2E, 0xB8, 0x4F),
        PeerState.Searching => Color.FromArgb(0xF2, 0xC2, 0x1B),
        PeerState.Unpaired => Color.FromArgb(0xF2, 0x8C, 0x28),
        PeerState.Offline or PeerState.Error => Color.FromArgb(0xE0, 0x3E, 0x3E),
        _ => Color.FromArgb(0x9A, 0x9A, 0x9A),
    };

    private Icon IconFor(PeerState state)
    {
        if (_icons.TryGetValue(state, out var cached))
            return cached;

        // 系統匣用小圖示尺寸（100% 是 16），直接取 ico 裡對應的尺寸，不靠 Windows 縮放。
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var baseIcon = new Icon(_baseIcon, size, size))
                g.DrawIcon(baseIcon, new Rectangle(0, 0, size, size));

            // 狀態點放左下角，不蓋住右下角的摺角。白框一半畫在圓外，所以邊距 = 框寬的一半。
            var diameter = (int)Math.Round(size * 0.45);
            var margin = Math.Max(1, size / 16);
            using var fill = new SolidBrush(ColorFor(state));
            using var outline = new Pen(Color.White, margin * 2);
            var dot = new Rectangle(margin, size - diameter - margin, diameter, diameter);
            g.DrawEllipse(outline, dot);
            g.FillEllipse(fill, dot);
        }

        var handle = bitmap.GetHicon();
        try
        {
            var icon = (Icon)Icon.FromHandle(handle).Clone();
            _icons[state] = icon;
            return icon;
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var icon in _icons.Values)
            icon.Dispose();
        _baseIcon.Dispose();
    }
}
