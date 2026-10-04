using Foldspace.Localization;
using Foldspace.App.Platform;
using Foldspace.Core.Identity;
using Foldspace.Core.Net;
using Foldspace.Core.Protocol;
using Foldspace.Core.Settings;

namespace Foldspace.App.Views;

/// <summary>
/// 設定：比照 Windows 內建的內容對話框（例如「乙太網路 內容」）。
/// 原生分頁、群組框、確定/取消/套用；欄位修改後按「套用」或「確定」才生效，
/// 「測試連線 / 配對 / 停用服務」是立即執行的動作按鈕。
/// 版面以 96 DPI 的像素排版，由 WinForms 依螢幕 DPI 自動縮放。
/// </summary>
public sealed class SettingsForm : Form
{
    // 版面常數（96 DPI）：對話框邊界 11、群組框內縮 12、標籤欄寬 112。
    private const int Margin11 = 11;
    private const int PageWidth = 398;
    // 「連線」分頁在英文＋公用網路＋防火牆未設定時最長（加密說明 4 行、公用網路提醒、連結列），以它為準。
    private const int PageHeight = 460;
    private const int GroupWidth = 366;

    private readonly AppController _controller;
    private static Strings T => Strings.Current;
    private readonly TabControl _tabs = new();
    private bool _loading;
    /// <summary>載入時選到的網卡（設定為「自動」時是預設網卡），用來判斷是否有修改。</summary>
    private string? _loadedAdapterId;
    private bool _busy;

    // 一般
    private readonly PictureBox _statusDot = new();
    private readonly Label _statusValue = new();
    private readonly Label _peerHostValue = new();
    private readonly Label _statusWarning = new();
    private readonly TableLayoutPanel _statusWarningRow = NewTable(2);
    private readonly Button _testButton = new();
    private readonly Button _pairButton = new();
    private readonly Button _serviceButton = new();
    private readonly Label _testResult = new();
    private readonly Panel _dropZone = new();
    private readonly CheckBox _startWithWindows = new();

    // 連線
    private readonly ComboBox _adapter = new();
    private readonly TextBox _localPort = new();
    private readonly TextBox _peerIp = new();
    private readonly TextBox _peerPort = new();
    private readonly CheckBox _encryption = new();
    private readonly TableLayoutPanel _publicWarning = NewTable(2);
    private readonly LinkLabel _firewallSetupLink = new();

    // 接收
    private readonly TextBox _receiveFolder = new();
    private readonly RadioButton _policyRename = new();
    private readonly RadioButton _policyOverwrite = new();
    private readonly RadioButton _policySkip = new();
    private readonly CheckBox _askBeforeReceive = new();

    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _applyButton = new();

    public SettingsForm(AppController controller)
    {
        _controller = controller;
        SuspendLayout();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = T.SettingsTitle;
        Icon = AppIcon.Create();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Margin11 + PageWidth + Margin11, Margin11 + PageHeight + 11 + 23 + Margin11);

        _tabs.Location = new Point(Margin11, Margin11);
        _tabs.Size = new Size(PageWidth, PageHeight);
        _tabs.TabPages.Add(BuildGeneralPage());
        _tabs.TabPages.Add(BuildConnectionPage());
        _tabs.TabPages.Add(BuildReceivePage());
        Controls.Add(_tabs);

        // 確定 / 取消 / 套用：靠右、75 x 23、間距 6
        var buttonY = Margin11 + PageHeight + 11;
        var x = ClientSize.Width - Margin11 - 75;
        SetupButton(_applyButton, T.Apply);
        SetupButton(_cancelButton, T.Cancel);
        SetupButton(_okButton, T.Ok);
        _applyButton.Location = new Point(x, buttonY);
        _cancelButton.Location = new Point(x -= 81, buttonY);
        _okButton.Location = new Point(x - 81, buttonY);
        _applyButton.Enabled = false;
        Controls.AddRange([_okButton, _cancelButton, _applyButton]);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        _okButton.Click += async (_, _) =>
        {
            if (await ApplyAsync())
                HideToTray();
        };
        _cancelButton.Click += (_, _) =>
        {
            LoadFromSettings();
            HideToTray();
        };
        _applyButton.Click += async (_, _) => await ApplyAsync();

        ResumeLayout(false);
        PerformLayout();

        controller.StatusChanged += UpdateStatus;
        controller.PairingFinished += outcome => ShowResult(T.Pairing(outcome), outcome.Success);
        controller.PeerEndpointChanged += OnPeerEndpointChanged;
    }

    // ================= 分頁：一般 =================

    private TabPage BuildGeneralPage()
    {
        var page = NewPage(T.TabGeneral);

        // 由上而下自動排列：文字換行或警告出現/消失時，下面的內容會自動跟著移動。
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 12, 12, 0),
            BackColor = Color.Transparent,
        };
        page.Controls.Add(flow);

        // 頁首：圖示 + 名稱 + 說明（同檔案內容的「一般」頁）
        var header = NewTable(2);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var icon = new PictureBox
        {
            Image = AppIcon.Create(32).ToBitmap(),
            Size = new Size(32, 32),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Margin = Padding.Empty,
        };
        header.Controls.Add(icon, 0, 0);
        header.SetRowSpan(icon, 2);
        // 名稱與本機版本在同一行，例如「Foldspace  版本 1.0.0」
        var titleLine = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var title = TableLabel("Foldspace");
        title.Font = new Font(Font, FontStyle.Bold);
        title.Margin = new Padding(0, 3, 8, 3);
        titleLine.Controls.Add(title);
        // 顯示產品版本；測試建置時滑鼠停留會顯示完整的建置版本（除錯用）。
        var versionLabel = TableLabel(T.Version(AppInfo.ProductVersion));
        if (AppInfo.BuildVersion != AppInfo.ProductVersion)
            new ToolTip().SetToolTip(versionLabel, AppInfo.BuildVersion);
        titleLine.Controls.Add(versionLabel);
        header.Controls.Add(titleLine, 1, 0);
        header.Controls.Add(TableLabel(T.GeneralDescription, GroupWidth - 48), 1, 1);
        flow.Controls.Add(header);

        var separator = NewSeparator(0, 0, GroupWidth);
        separator.Margin = new Padding(0, 10, 0, 10);
        flow.Controls.Add(separator);

        // 連線狀態
        var status = NewAutoGroup(T.GroupStatus);
        var table = NewTable(2);
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        table.Controls.Add(TableLabel(T.LabelStatus), 0, 0);
        // 狀態前面的小圓點和系統匣圖示同色。圓點是獨立的控制項，排在文字前面。
        // （Label 的 Image 會畫在文字底下，不會把文字往右推。）
        var statusLine = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        _statusDot.Size = new Size(10, 10);
        _statusDot.SizeMode = PictureBoxSizeMode.CenterImage;
        _statusDot.Margin = new Padding(0, 6, 6, 0);
        _statusValue.AutoSize = true;
        _statusValue.MaximumSize = new Size(GroupWidth - 126, 0);
        _statusValue.Margin = new Padding(0, 3, 0, 3);
        statusLine.Controls.AddRange([_statusDot, _statusValue]);
        table.Controls.Add(statusLine, 1, 0);

        table.Controls.Add(TableLabel(T.LabelPeerComputer), 0, 1);
        _peerHostValue.AutoSize = true;
        _peerHostValue.Margin = new Padding(0, 3, 0, 3);
        table.Controls.Add(_peerHostValue, 1, 1);

        // 警告：圖示 + 文字（各自獨立的控制項）；隱藏時這一列不佔空間
        _statusWarningRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _statusWarningRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _statusWarningRow.Margin = new Padding(0, 6, 0, 0);
        _statusWarningRow.Controls.Add(new PictureBox
        {
            Image = ShellIcons.Stock(StockIconId.Warning, 16),
            Size = new Size(16, 16),
            Margin = new Padding(0, 0, 4, 0),
        }, 0, 0);
        _statusWarning.AutoSize = true;
        _statusWarning.MaximumSize = new Size(GroupWidth - 50, 0);
        _statusWarning.Margin = Padding.Empty;
        _statusWarningRow.Controls.Add(_statusWarning, 1, 0);
        _statusWarningRow.Visible = false;
        table.Controls.Add(_statusWarningRow, 0, 2);
        table.SetColumnSpan(_statusWarningRow, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
        SetupButton(_testButton, T.ButtonTest, 96);
        SetupButton(_pairButton, T.ButtonPair, 96);
        SetupButton(_serviceButton, T.ButtonDisableService, 96);
        _testButton.Margin = new Padding(0, 0, 6, 0);
        _pairButton.Margin = new Padding(0, 0, 6, 0);
        _serviceButton.Margin = Padding.Empty;
        buttons.Controls.AddRange([_testButton, _pairButton, _serviceButton]);
        table.Controls.Add(buttons, 0, 3);
        table.SetColumnSpan(buttons, 2);
        _testButton.Click += TestButton_Click;
        _pairButton.Click += PairButton_Click;
        _serviceButton.Click += ServiceButton_Click;

        _testResult.AutoSize = true;
        _testResult.MaximumSize = new Size(GroupWidth - 30, 0);
        _testResult.Margin = new Padding(0, 8, 0, 0);
        _testResult.Visible = false;
        table.Controls.Add(_testResult, 0, 4);
        table.SetColumnSpan(_testResult, 2);

        status.Controls.Add(table);
        flow.Controls.Add(status);

        // 傳送檔案：拖放區（沒有命令列長度限制）
        var send = new GroupBox { Text = T.GroupSendFiles, Size = new Size(GroupWidth, 92), Margin = new Padding(0, 8, 0, 0) };
        _dropZone.Location = new Point(12, 22);
        _dropZone.Size = new Size(GroupWidth - 24, 58);
        _dropZone.BorderStyle = BorderStyle.Fixed3D;
        _dropZone.BackColor = SystemColors.Window;
        _dropZone.AllowDrop = true;
        var dropText = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = T.DropZoneText,
            AllowDrop = true,
            BackColor = SystemColors.Window,
        };
        _dropZone.Controls.Add(dropText);
        foreach (Control target in new Control[] { _dropZone, dropText })
        {
            target.DragEnter += DropZone_DragEnter;
            target.DragDrop += DropZone_DragDrop;
        }
        send.Controls.Add(_dropZone);
        flow.Controls.Add(send);

        SetupCheck(_startWithWindows, T.StartWithWindows);
        _startWithWindows.Margin = new Padding(0, 10, 0, 0);
        flow.Controls.Add(_startWithWindows);
        return page;
    }

    /// <summary>自動長高的群組框：寬度固定，高度跟著內容。</summary>
    private static GroupBox NewAutoGroup(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(GroupWidth, 0),
        MaximumSize = new Size(GroupWidth, 0),
        Padding = new Padding(9, 6, 9, 9),
        Margin = Padding.Empty,
    };

    private static TableLayoutPanel NewTable(int columns) => new()
    {
        ColumnCount = columns,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Top,
        Margin = Padding.Empty,
        BackColor = Color.Transparent,
    };

    private static Label TableLabel(string text, int? width = null) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = width is { } w ? new Size(w, 0) : Size.Empty,
        Margin = new Padding(0, 3, 12, 3),
    };

    // ================= 分頁：連線 =================

    private TabPage BuildConnectionPage()
    {
        var page = NewPage(T.TabConnection);
        var flow = NewFlow(page);

        // 本機：標籤欄自動調整寬度（英文標籤比中文長）
        var local = NewAutoGroup(T.GroupLocal);
        var localTable = NewFieldTable();
        AddField(localTable, T.LabelComputerName, FieldValue(Environment.MachineName));
        _adapter.DropDownStyle = ComboBoxStyle.DropDownList;
        _adapter.Width = 220;
        _adapter.DisplayMember = nameof(AdapterInfo.Display);
        _adapter.SelectedIndexChanged += FormChanged;
        AddField(localTable, T.LabelNetworkAdapter, _adapter);
        SetupText(_localPort, 60, maxLength: 5);
        AddField(localTable, T.LabelLocalPort, _localPort);
        AddField(localTable, T.LabelFingerprint, FieldValue(DeviceIdentity.ShortFingerprint(_controller.LocalFingerprint)));
        local.Controls.Add(localTable);
        flow.Controls.Add(local);

        var peer = NewAutoGroup(T.GroupPeer);
        peer.Margin = new Padding(0, 8, 0, 0);
        var peerTable = NewFieldTable();
        SetupText(_peerIp, 120, maxLength: 15);
        AddField(peerTable, T.LabelPeerIp, _peerIp);
        SetupText(_peerPort, 60, maxLength: 5);
        AddField(peerTable, T.LabelPeerPort, _peerPort);
        peer.Controls.Add(peerTable);
        flow.Controls.Add(peer);

        var security = NewAutoGroup(T.GroupSecurity);
        security.Margin = new Padding(0, 8, 0, 0);
        var securityFlow = NewInnerFlow();
        SetupCheck(_encryption, T.Encryption);
        securityFlow.Controls.Add(_encryption);
        var encryptionNote = TableLabel(T.EncryptionNote, GroupWidth - 44);
        encryptionNote.Margin = new Padding(18, 0, 0, 0);
        securityFlow.Controls.Add(encryptionNote);
        security.Controls.Add(securityFlow);
        flow.Controls.Add(security);

        // 公用網路警告：圖示 + 文字；平常隱藏
        _publicWarning.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _publicWarning.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _publicWarning.Margin = new Padding(0, 10, 0, 0);
        _publicWarning.Controls.Add(new PictureBox
        {
            Image = ShellIcons.Stock(StockIconId.Warning, 16),
            Size = new Size(16, 16),
            Margin = new Padding(0, 0, 4, 0),
        }, 0, 0);
        var publicText = TableLabel(T.PublicNetworkWarning, GroupWidth - 24);
        publicText.Margin = Padding.Empty;
        _publicWarning.Controls.Add(publicText, 1, 0);
        _publicWarning.Visible = false;
        flow.Controls.Add(_publicWarning);

        // 防火牆規則沒設好時（例如啟動時在 UAC 按了「否」）才顯示，讓使用者重試。
        _firewallSetupLink.Text = T.FirewallSetupLink;
        _firewallSetupLink.AutoSize = true;
        _firewallSetupLink.Margin = new Padding(0, 0, 16, 0);
        _firewallSetupLink.Visible = false;
        _firewallSetupLink.LinkClicked += async (_, _) =>
        {
            SetBusy(true);
            try
            {
                var result = await Task.Run(() => FirewallSetup.Run(_controller.Paths));
                if (result == FirewallSetup.Result.Failed)
                    ShowFirewallHelp();
                UpdateFirewallState();
            }
            finally
            {
                SetBusy(false);
            }
        };
        // 兩個連結放在同一行（太寬時自動換行），省下垂直空間。
        var help = new LinkLabel { Text = T.CannotConnectLink, AutoSize = true, Margin = Padding.Empty };
        help.LinkClicked += (_, _) => ShowFirewallHelp();
        var links = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            MaximumSize = new Size(GroupWidth, 0),
            Margin = new Padding(0, 10, 0, 0),
        };
        links.Controls.AddRange([_firewallSetupLink, help]);
        flow.Controls.Add(links);
        return page;
    }

    // ================= 分頁：接收 =================

    private TabPage BuildReceivePage()
    {
        var page = NewPage(T.TabReceive);
        var flow = NewFlow(page);

        var folder = NewAutoGroup(T.GroupReceiveFolder);
        var folderFlow = NewInnerFlow();
        folderFlow.Controls.Add(TableLabel(T.ReceiveFolderNote, GroupWidth - 30));
        var pathRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        SetupText(_receiveFolder, GroupWidth - 30 - 81);
        _receiveFolder.Margin = new Padding(0, 1, 6, 0);
        var browse = new Button();
        SetupButton(browse, T.Browse);
        browse.Margin = Padding.Empty;
        browse.Click += BrowseButton_Click;
        pathRow.Controls.AddRange([_receiveFolder, browse]);
        folderFlow.Controls.Add(pathRow);
        folder.Controls.Add(folderFlow);
        flow.Controls.Add(folder);

        var conflict = NewAutoGroup(T.GroupConflict);
        conflict.Margin = new Padding(0, 8, 0, 0);
        var conflictFlow = NewInnerFlow();
        SetupRadio(_policyRename, T.PolicyRename);
        SetupRadio(_policyOverwrite, T.PolicyOverwrite);
        SetupRadio(_policySkip, T.PolicySkip);
        conflictFlow.Controls.AddRange([_policyRename, _policyOverwrite, _policySkip]);
        var conflictNote = TableLabel(T.ConflictNote, GroupWidth - 30);
        conflictNote.Margin = new Padding(0, 6, 0, 0);
        conflictFlow.Controls.Add(conflictNote);
        conflict.Controls.Add(conflictFlow);
        flow.Controls.Add(conflict);

        SetupCheck(_askBeforeReceive, T.AskBeforeReceive);
        _askBeforeReceive.Margin = new Padding(0, 10, 0, 0);
        flow.Controls.Add(_askBeforeReceive);
        var askNote = TableLabel(T.AskBeforeReceiveNote, GroupWidth - 20);
        askNote.Margin = new Padding(18, 0, 0, 0);
        flow.Controls.Add(askNote);
        return page;
    }

    /// <summary>分頁內由上而下自動排列的容器。</summary>
    private static FlowLayoutPanel NewFlow(TabPage page)
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 12, 12, 0),
            BackColor = Color.Transparent,
        };
        page.Controls.Add(flow);
        return flow;
    }

    /// <summary>群組框內由上而下排列的容器。</summary>
    private static FlowLayoutPanel NewInnerFlow() => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Dock = DockStyle.Top,
        Margin = Padding.Empty,
        BackColor = Color.Transparent,
    };

    /// <summary>「標籤 | 欄位」兩欄的表格，標籤欄依最長的標籤自動調整寬度。</summary>
    private static TableLayoutPanel NewFieldTable()
    {
        var table = NewTable(2);
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return table;
    }

    private static void AddField(TableLayoutPanel table, string label, Control field)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = TableLabel(label);
        caption.Anchor = AnchorStyles.Left;
        field.Anchor = AnchorStyles.Left;
        field.Margin = new Padding(0, 2, 0, 2);
        table.Controls.Add(caption, 0, row);
        table.Controls.Add(field, 1, row);
    }

    private static Label FieldValue(string text) => new() { Text = text, AutoSize = true };

    // ================= 控制項工廠 =================

    private static TabPage NewPage(string text) => new(text) { UseVisualStyleBackColor = true };

    /// <summary>蝕刻的水平分隔線。</summary>
    private static Label NewSeparator(int x, int y, int width) => new()
    {
        AutoSize = false,
        BorderStyle = BorderStyle.Fixed3D,
        Location = new Point(x, y),
        Size = new Size(width, 2),
    };

    private void SetupText(TextBox box, int width, int maxLength = 32767)
    {
        box.Width = width;
        box.MaxLength = maxLength;
        box.TextChanged += FormChanged;
    }

    private void SetupCheck(CheckBox check, string text)
    {
        check.Text = text;
        check.AutoSize = true;
        check.CheckedChanged += FormChanged;
    }

    private void SetupRadio(RadioButton radio, string text)
    {
        radio.Text = text;
        radio.AutoSize = true;
        radio.Margin = new Padding(0, 2, 0, 2);
        radio.CheckedChanged += FormChanged;
    }

    /// <summary>標準按鈕：至少 75 x 23，文字較長時自動加寬（英文）。</summary>
    private static void SetupButton(Button button, string text, int minWidth = 75)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowOnly;
        button.MinimumSize = new Size(minWidth, 23);
        button.Size = new Size(minWidth, 23);
        button.UseVisualStyleBackColor = true;
    }

    // ================= 顯示 / 隱藏 =================

    public void ShowAndActivate()
    {
        if (!Visible)
        {
            LoadFromSettings();
            UpdateFirewallState();
        }
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        // 從背景程序叫出視窗時，Activate 不一定能搶到前景；短暫設為最上層確保看得到。
        TopMost = true;
        Activate();
        TopMost = false;
    }

    /// <summary>按 X 只隱藏到系統匣；只有選單的「結束」會真正結束。未套用的修改視為取消。</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            LoadFromSettings();
            HideToTray();
        }
        base.OnFormClosing(e);
    }

    private void HideToTray()
    {
        Hide();
        _controller.OnSettingsWindowHidden();
    }

    // ================= 欄位 =================

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            var s = _controller.Settings;

            var adapters = NetworkAdapters.List();
            // 用 Items 而不是 DataSource：視窗第一次顯示前還沒有 BindingContext，選取會失效。
            _adapter.Items.Clear();
            _adapter.Items.AddRange(adapters.Cast<object>().ToArray());
            _adapter.SelectedItem = LocalAdapterResolver.Resolve(adapters, s.LocalInterfaceId, s.LocalIp)
                ?? LocalAdapterResolver.Default(adapters);
            _loadedAdapterId = (_adapter.SelectedItem as AdapterInfo)?.Id;

            _localPort.Text = s.LocalPort.ToString();
            _peerIp.Text = s.PeerIp ?? "";
            _peerPort.Text = s.PeerPort.ToString();
            _receiveFolder.Text = s.ReceiveFolder;
            _policyRename.Checked = s.ConflictPolicy == ConflictPolicy.Rename;
            _policyOverwrite.Checked = s.ConflictPolicy == ConflictPolicy.Overwrite;
            _policySkip.Checked = s.ConflictPolicy == ConflictPolicy.Skip;
            _askBeforeReceive.Checked = s.AskBeforeReceive;
            _encryption.Checked = s.Encryption;
            _startWithWindows.Checked = s.StartWithWindows;
            // 畫面上的值可能本來就和設定不同（例如設定的網卡已不存在），這時「套用」一開始就能按。
            _applyButton.Enabled = IsDirty();
            UpdateStatus(_controller.Status);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>對方位址被自動更新（搜尋、對方發起配對）：畫面上的欄位跟著改，不算使用者的修改。</summary>
    private void OnPeerEndpointChanged(System.Net.IPEndPoint endpoint)
    {
        _loading = true;
        try
        {
            _peerIp.Text = endpoint.Address.ToString();
            _peerPort.Text = endpoint.Port.ToString();
        }
        finally
        {
            _loading = false;
        }
        _applyButton.Enabled = IsDirty();
    }

    private void FormChanged(object? sender, EventArgs e)
    {
        if (!_loading)
            _applyButton.Enabled = IsDirty();
    }

    /// <summary>畫面上的值是否和目前的設定不同（改回原值時「套用」會再變成不能按）。</summary>
    private bool IsDirty()
    {
        var s = _controller.Settings;
        var adapterId = (_adapter.SelectedItem as AdapterInfo)?.Id;
        var policy = _policyOverwrite.Checked ? ConflictPolicy.Overwrite
            : _policySkip.Checked ? ConflictPolicy.Skip
            : ConflictPolicy.Rename;
        return _localPort.Text.Trim() != s.LocalPort.ToString()
            || _peerIp.Text.Trim() != (s.PeerIp ?? "")
            || _peerPort.Text.Trim() != s.PeerPort.ToString()
            || _receiveFolder.Text.Trim() != s.ReceiveFolder
            // 設定為「自動」時，和開啟時自動選到的網卡比；否則和設定的網卡比。
            || adapterId != (string.IsNullOrEmpty(s.LocalInterfaceId) ? _loadedAdapterId : s.LocalInterfaceId)
            || policy != s.ConflictPolicy
            || _askBeforeReceive.Checked != s.AskBeforeReceive
            || _encryption.Checked != s.Encryption
            || _startWithWindows.Checked != s.StartWithWindows;
    }

    /// <summary>驗證並組出新的設定；有錯誤時切到該分頁、以訊息方塊說明並把焦點移到該欄位，回傳 null。</summary>
    private AppSettings? ReadForm()
    {
        var adapter = _adapter.SelectedItem as AdapterInfo;

        if (!int.TryParse(_localPort.Text.Trim(), out var localPort) || !AppSettings.IsValidPort(localPort))
            return Invalid(_localPort, T.InvalidLocalPort);

        var peerIp = _peerIp.Text.Trim();
        if (peerIp.Length > 0 && !AppSettings.IsValidIPv4(peerIp))
            return Invalid(_peerIp, T.InvalidPeerIp);
        if (peerIp.Length > 0 && adapter is not null && adapter.Address.ToString() == peerIp)
            return Invalid(_peerIp, T.PeerIpSameAsLocal);

        if (!int.TryParse(_peerPort.Text.Trim(), out var peerPort) || !AppSettings.IsValidPort(peerPort))
            return Invalid(_peerPort, T.InvalidPeerPort);

        var folder = _receiveFolder.Text.Trim();
        if (folder.Length == 0 || !Path.IsPathFullyQualified(folder))
            return Invalid(_receiveFolder, T.InvalidReceiveFolder);

        var policy = _policyOverwrite.Checked ? ConflictPolicy.Overwrite
            : _policySkip.Checked ? ConflictPolicy.Skip
            : ConflictPolicy.Rename;

        return _controller.Settings with
        {
            LocalInterfaceId = adapter?.Id,
            LocalIp = adapter?.Address.ToString(),
            LocalPort = localPort,
            PeerIp = peerIp.Length == 0 ? null : peerIp,
            PeerPort = peerPort,
            ReceiveFolder = folder,
            ConflictPolicy = policy,
            AskBeforeReceive = _askBeforeReceive.Checked,
            Encryption = _encryption.Checked,
            StartWithWindows = _startWithWindows.Checked,
        };
    }

    private AppSettings? Invalid(TextBox field, string message)
    {
        for (Control? node = field; node is not null; node = node.Parent)
        {
            if (node is TabPage page)
            {
                _tabs.SelectedTab = page;
                break;
            }
        }
        Dialogs.Show(this, Text, message, TaskDialogIcon.Warning);
        field.Focus();
        field.SelectAll();
        return null;
    }

    private async Task<bool> ApplyAsync()
    {
        if (!_applyButton.Enabled)
            return true; // 沒有修改

        var updated = ReadForm();
        if (updated is null)
            return false;

        var portChanged = updated.LocalPort != _controller.Settings.LocalPort;
        await _controller.SaveSettingsAsync(updated);
        _applyButton.Enabled = false;
        if (portChanged)
        {
            Dialogs.Show(this, Text, T.LocalPortChanged(updated.LocalPort), TaskDialogIcon.Information);
        }
        return true;
    }

    // ================= 狀態 =================

    private void UpdateStatus(PeerStatus status)
    {
        // 連上之後，先前「測試連線」等的錯誤訊息已經過時。
        if (_resultIsError && status.State == PeerState.Connected && _lastState != PeerState.Connected)
            HideResult();
        // 配對狀態變了（例如對方解除配對），之前的「已和 X 配對」或測試連線結果就不對了。
        if (_testResult.Visible && _resultPairedAtShow != _controller.Settings.IsPaired)
            HideResult();
        _lastState = status.State;

        _statusValue.Text = T.Status(status);
        _peerHostValue.Text = status.State is PeerState.Connected or PeerState.Unpaired ? status.PeerHostname ?? "-" : "-";
        _statusWarning.Text = status.IncomingBlocked ? T.IncomingBlockedWarning : "";
        _statusWarningRow.Visible = status.IncomingBlocked;
        _statusDot.Image = StatusDot(status.State);

        _serviceButton.Text = _controller.Settings.ServiceEnabled ? T.ButtonDisableService : T.ButtonEnableService;
        var paired = _controller.Settings.IsPaired;
        _pairButton.Text = paired ? T.ButtonUnpair : T.ButtonPair;
        // 未配對時「配對…」會先搜尋，所以不必先連上對方；只有服務停用時不能用。
        _pairButton.Enabled = !_busy && (paired || status.State != PeerState.Disabled);
        _testButton.Enabled = !_busy;
        _serviceButton.Enabled = !_busy;
    }

    private readonly Dictionary<PeerState, Bitmap> _dots = [];

    /// <summary>和系統匣圖示同色的小圓點，依螢幕 DPI 產生。</summary>
    private Bitmap StatusDot(PeerState state)
    {
        if (_dots.TryGetValue(state, out var cached))
            return cached;
        var size = LogicalToDeviceUnits(10);
        var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(TrayIcon.ColorFor(state));
            using var edge = new Pen(Color.FromArgb(64, 0, 0, 0));
            g.FillEllipse(fill, 0, 0, size - 1, size - 1);
            g.DrawEllipse(edge, 0, 0, size - 1, size - 1);
        }
        return _dots[state] = bitmap;
    }

    private PeerState? _lastState;
    private bool _resultIsError;
    private bool _resultPairedAtShow;

    private void HideResult()
    {
        _testResult.Visible = false;
        _resultIsError = false;
    }

    private void ShowResult(string message, bool success)
    {
        _resultIsError = !success;
        _resultPairedAtShow = _controller.Settings.IsPaired;
        _testResult.Text = message;
        _testResult.ForeColor = success ? SystemColors.ControlText : Color.FromArgb(0xC4, 0x2B, 0x1C);
        _testResult.Visible = true;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        UpdateStatus(_controller.Status);
    }

    // ================= 動作按鈕 =================

    private async void ServiceButton_Click(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            await _controller.SetServiceEnabledAsync(!_controller.Settings.ServiceEnabled);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void TestButton_Click(object? sender, EventArgs e)
    {
        // 有未套用的修改時先套用，測的才是畫面上的設定。
        if (!await ApplyAsync())
            return;

        SetBusy(true);
        ShowResult(T.Testing, true);
        try
        {
            var result = await _controller.Service.TestConnectionAsync();
            ShowResult(T.TestResult(result), result.Success);
            if (result.PeerHostname is not null)
                _peerHostValue.Text = result.PeerHostname;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void PairButton_Click(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            if (_controller.Settings.IsPaired)
            {
                var host = _controller.Settings.PeerHostname ?? T.ThePeer;
                if (!Dialogs.Confirm(this, Text, T.UnpairConfirm(host), T.ButtonUnpair, TaskDialogIcon.None))
                    return;
                await _controller.Service.UnpairAsync();
                ShowResult(T.UnpairDone, true);
            }
            else
            {
                // 搜尋同網段的電腦，選一台配對（兩邊會跳出配對碼對話框）；結果由 PairingFinished 顯示。
                using var dialog = new DiscoveryDialog(() => _controller.Service.DiscoverAsync());
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedPeer is not { } peer)
                    return;
                ShowResult(T.ConnectingTo(peer.Hostname), true);
                await _controller.ConnectAndPairAsync(peer);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = T.BrowseTitle,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (Directory.Exists(_receiveFolder.Text))
            dialog.InitialDirectory = _receiveFolder.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _receiveFolder.Text = dialog.SelectedPath;
    }

    /// <summary>防火牆規則已設好時，公用網路也連得進來，不需要提醒。</summary>
    private void UpdateFirewallState()
    {
        var configured = FirewallSetup.IsConfigured() != false;
        _firewallSetupLink.Visible = !configured;
        _publicWarning.Visible = !configured && NetworkProfile.IsAnyConnectedNetworkPublic();
    }

    private void ShowFirewallHelp()
    {
        Dialogs.Show(this, T.FirewallHelpTitle, T.FirewallHelp, TaskDialogIcon.Information);
    }

    // ================= 拖放區 =================

    private void DropZone_DragEnter(object? sender, DragEventArgs e) =>
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    private async void DropZone_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            await _controller.HandleDropAsync(paths, justStarted: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.StatusChanged -= UpdateStatus;
            _controller.PeerEndpointChanged -= OnPeerEndpointChanged;
        }
        base.Dispose(disposing);
    }
}
