using Serilog;
using Foldspace.App.Platform;
using Foldspace.Core.Net;
using Foldspace.Localization;

namespace Foldspace.App.Views;

/// <summary>
/// 「選擇要配對的電腦」：列出同網段開著 Foldspace 的電腦，選一台按「配對」。
/// 外觀比照 Windows 內建的傳統對話框：說明、清單（電腦名稱、IP、狀態）、狀態列、按鈕列。
/// </summary>
public sealed class DiscoveryDialog : Form
{
    private readonly Func<Task<IReadOnlyList<DiscoveredPeer>>> _discover;
    private readonly ListView _list = new();
    private readonly Label _status = new();
    private readonly Button _searchAgain = new();
    private readonly Button _pair = new();
    private readonly Button _cancel = new();

    private static Strings T => Strings.Current;

    public DiscoveryDialog(Func<Task<IReadOnlyList<DiscoveredPeer>>> discover)
    {
        _discover = discover;
        SuspendLayout();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = T.DiscoveryTitle;
        Icon = AppIcon.Create();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 330);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(11),
            ColumnCount = 1,
            RowCount = 4,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var description = new Label
        {
            Text = T.DiscoveryDescription,
            AutoSize = true,
            MaximumSize = new Size(438, 0),
            Margin = new Padding(0, 0, 0, 8),
        };
        layout.Controls.Add(description, 0, 0);

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.Dock = DockStyle.Fill;
        _list.Margin = Padding.Empty;
        _list.Columns.Add(T.ColumnComputer, 170);
        _list.Columns.Add(T.ColumnAddress, 110);
        _list.Columns.Add(T.ColumnStatus, 150);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.ItemActivate += (_, _) =>
        {
            if (_pair.Enabled)
                _pair.PerformClick();
        };
        layout.Controls.Add(_list, 0, 1);

        _status.AutoSize = true;
        _status.MaximumSize = new Size(438, 0);
        _status.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_status, 0, 2);

        // 按鈕列：左邊「重新搜尋」，右邊「配對」「取消」（Windows 對話框慣例）。
        var buttons = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 11, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        SetupButton(_searchAgain, T.ButtonSearchAgain);
        SetupButton(_pair, T.ButtonPairSelected);
        SetupButton(_cancel, T.Cancel);
        _searchAgain.Margin = Padding.Empty;
        _pair.Margin = new Padding(0, 0, 6, 0);
        _cancel.Margin = Padding.Empty;
        _pair.DialogResult = DialogResult.OK;
        _cancel.DialogResult = DialogResult.Cancel;
        _searchAgain.Click += async (_, _) => await SearchAsync();
        buttons.Controls.Add(_searchAgain, 0, 0);
        buttons.Controls.Add(_pair, 2, 0);
        buttons.Controls.Add(_cancel, 3, 0);
        layout.Controls.Add(buttons, 0, 3);

        Controls.Add(layout);
        AcceptButton = _pair;
        CancelButton = _cancel;
        ResumeLayout(false);
        PerformLayout();

        Shown += async (_, _) => await SearchAsync();
    }

    /// <summary>使用者選的電腦（按「配對」時）。</summary>
    public DiscoveredPeer? SelectedPeer => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as DiscoveredPeer : null;

    private async Task SearchAsync()
    {
        _list.Items.Clear();
        _status.Text = T.DiscoverySearching;
        _searchAgain.Enabled = false;
        UpdateButtons();
        UseWaitCursor = true;

        IReadOnlyList<DiscoveredPeer> peers;
        try
        {
            peers = await _discover();
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or InvalidOperationException)
        {
            Log.Warning("Discovery failed: {Error}", ex.Message);
            peers = [];
        }
        if (IsDisposed)
            return;

        UseWaitCursor = false;
        _searchAgain.Enabled = true;
        foreach (var peer in peers)
        {
            var item = new ListViewItem([peer.Hostname, peer.Address.ToString(), T.DiscoveryStatus(peer)]) { Tag = peer };
            if (!peer.CanPair)
                item.ForeColor = SystemColors.GrayText;
            _list.Items.Add(item);
        }
        _status.Text = peers.Count == 0 ? T.DiscoveryNone : T.DiscoveryFound(peers.Count);

        // 預設選第一台可以配對的電腦
        if (_list.Items.Cast<ListViewItem>().FirstOrDefault(i => ((DiscoveredPeer)i.Tag!).CanPair) is { } first)
        {
            first.Selected = true;
            first.Focused = true;
            _list.Focus();
        }
        UpdateButtons();
    }

    private void UpdateButtons() => _pair.Enabled = SelectedPeer?.CanPair == true;

    private static void SetupButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowOnly;
        button.MinimumSize = new Size(75, 23);
        button.UseVisualStyleBackColor = true;
    }
}
