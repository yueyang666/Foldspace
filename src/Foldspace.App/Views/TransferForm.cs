using Foldspace.Localization;
using Foldspace.App.Platform;
using Foldspace.Core;
using Foldspace.Core.Transfer;

namespace Foldspace.App.Views;

/// <summary>
/// 傳輸進度：比照 Windows 檔案複製進度視窗。傳送與接收都顯示，用箭頭區分方向；
/// 全部完成 3 秒後自動隱藏。
/// <para>
/// 列是動態加入的，WinForms 的自動縮放不會套用到後來加入的控制項，
/// 所以這個視窗不用自動縮放，所有座標都依目前螢幕的 DPI 自行換算。
/// </para>
/// </summary>
public sealed class TransferForm : Form
{
    private const int RowWidth = 440;
    private const int RowHeight = 112;
    private const int EmptyHeight = 48;
    private const int MaxVisibleRows = 4;
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(3);

    private readonly AppController _controller;
    private readonly FlowLayoutPanel _list = new();
    private readonly Label _empty = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly List<JobRow> _rows = [];
    private DateTime? _allDoneSince;

    public TransferForm(AppController controller)
    {
        _controller = controller;
        AutoScaleMode = AutoScaleMode.None;
        Text = Strings.Current.TransfersTitle;
        Icon = AppIcon.Create();
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = SystemColors.Window;

        _list.FlowDirection = FlowDirection.TopDown;
        _list.WrapContents = false;
        _list.AutoScroll = true;
        _list.Dock = DockStyle.Fill;
        Controls.Add(_list);

        _empty.Text = Strings.Current.NoTransfers;
        _empty.AutoSize = true;
        _list.Controls.Add(_empty);

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>96 DPI 的像素換算成目前螢幕的像素。</summary>
    private int Px(int logical) => (int)Math.Round(logical * DeviceDpi / 96.0);

    public void Add(TransferJob job)
    {
        if (_rows.Any(r => r.Job == job))
            return;
        var row = new JobRow(job, Px);
        _rows.Add(row);
        _list.Controls.Remove(_empty);
        _list.Controls.Add(row);
        _allDoneSince = null;
        FitToRows();
    }

    public void ShowNearTray()
    {
        if (!Visible)
        {
            FitToRows();
            Show();
        }
        _allDoneSince = null;
    }

    /// <summary>視窗高度跟著列數，最多顯示 4 列，貼齊工作區右下角。</summary>
    private void FitToRows()
    {
        _empty.Margin = new Padding(Px(16));
        var visible = Math.Clamp(_rows.Count, 1, MaxVisibleRows);
        var scrollBar = _rows.Count > MaxVisibleRows ? SystemInformation.VerticalScrollBarWidth : 0;
        ClientSize = new Size(Px(RowWidth) + scrollBar, _rows.Count == 0 ? Px(EmptyHeight) : Px(RowHeight) * visible);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - Px(12), area.Bottom - Height - Px(12));
    }

    private void Tick()
    {
        foreach (var row in _rows)
            row.UpdateFromJob();

        if (!Visible || _rows.Count == 0)
            return;

        if (_rows.All(r => r.Job.IsFinished))
        {
            _allDoneSince ??= DateTime.UtcNow;
            if (DateTime.UtcNow - _allDoneSince >= AutoHideDelay)
            {
                Hide();
                foreach (var row in _rows)
                {
                    _list.Controls.Remove(row);
                    row.Dispose();
                }
                _rows.Clear();
                _list.Controls.Add(_empty);
                _controller.Service.ForgetFinishedJobs();
                _allDoneSince = null;
            }
        }
        else
        {
            _allDoneSince = null;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>一個任務：標題、大字的完成百分比、進度條與取消、詳細資訊。速度用指數移動平均平滑。</summary>
    private sealed class JobRow : UserControl
    {
        private readonly Label _title = new();
        private readonly Label _headline = new();
        private readonly ProgressBar _progress = new();
        private readonly Button _cancel = new();
        private readonly Label _detail = new();
        private long _lastBytes;
        private DateTime _lastTime = DateTime.UtcNow;
        private double _speed; // bytes/s
        private DateTime _lastProgress = DateTime.UtcNow;

        public JobRow(TransferJob job, Func<int, int> px)
        {
            Job = job;
            AutoScaleMode = AutoScaleMode.None;
            Size = new Size(px(RowWidth), px(RowHeight));
            Margin = Padding.Empty;
            BackColor = SystemColors.Window;

            _title.AutoSize = false;
            _title.AutoEllipsis = true;
            _title.Location = new Point(px(16), px(12));
            _title.Size = new Size(px(RowWidth - 32), px(18));

            // 和 Windows 複製視窗一樣，用較大的藍色字顯示「已完成 45%」。
            _headline.AutoSize = true;
            _headline.Location = new Point(px(16), px(34));
            _headline.Font = new Font(Font.FontFamily, 12F);
            _headline.ForeColor = Color.FromArgb(0x00, 0x33, 0x99);

            _progress.Location = new Point(px(16), px(64));
            _progress.Size = new Size(px(RowWidth - 32 - 75 - 10), px(16));
            _progress.Maximum = 1000;

            _cancel.Text = Strings.Current.Cancel;
            _cancel.Location = new Point(px(RowWidth - 16 - 75), px(60));
            _cancel.Size = new Size(px(75), px(23));
            _cancel.UseVisualStyleBackColor = true;
            _cancel.Click += (_, _) => Job.Cancel();

            _detail.AutoSize = false;
            _detail.AutoEllipsis = true;
            _detail.Location = new Point(px(16), px(88));
            _detail.Size = new Size(px(RowWidth - 32), px(18));

            Controls.AddRange([_title, _headline, _progress, _cancel, _detail]);
            UpdateFromJob();
        }

        public TransferJob Job { get; }

        /// <summary>每列底下一條淺灰分隔線。</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(SystemColors.ControlLight);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }

        public void UpdateFromJob()
        {
            var now = DateTime.UtcNow;
            var bytes = Job.BytesTransferred;
            var seconds = (now - _lastTime).TotalSeconds;
            if (seconds > 0 && Job.State == TransferJobState.Transferring)
            {
                var instant = (bytes - _lastBytes) / seconds;
                _speed = _speed == 0 ? instant : _speed * 0.7 + instant * 0.3;
                if (bytes != _lastBytes)
                    _lastProgress = now;
                else if (now - _lastProgress > TimeSpan.FromSeconds(3))
                    _speed = 0; // 停住了：不要用慢慢衰減的平均速度算出幾千小時的剩餘時間
            }
            _lastBytes = bytes;
            _lastTime = now;

            var t = Strings.Current;
            _title.Text = t.JobTitle(Job);

            var percent = Job.State == TransferJobState.Completed ? 100
                : Job.TotalBytes <= 0 ? 0
                : Math.Min(100, Job.BytesTransferred * 100.0 / Job.TotalBytes);
            _headline.Text = t.JobHeadline(Job.State, percent);

            var indeterminate = Job.State is TransferJobState.Preparing or TransferJobState.WaitingForPeer;
            var style = indeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (_progress.Style != style)
                _progress.Style = style;
            if (!indeterminate)
                SetProgress((int)(percent * 10));
            _cancel.Visible = !Job.IsFinished;

            var files = Job.FileCount > 0 ? t.FileCount(Job.FileCount) + ", " : "";
            var size = $"{Format.Bytes(Math.Min(Job.BytesTransferred, Job.TotalBytes))} / {Format.Bytes(Job.TotalBytes)}";
            var note = Job.Note is { } n ? t.Job(n, Job.Direction) : null;
            _detail.Text = Job.State switch
            {
                TransferJobState.Transferring => $"{files}{size}    {t.Speed(_speed / 1024 / 1024)}    {t.TimeLeft(Remaining())}",
                TransferJobState.Finalizing => $"{files}{size}",
                TransferJobState.Completed => $"{files}{Format.Bytes(Job.TotalBytes)}" + (note is null ? "" : $"    {note}"),
                TransferJobState.Failed or TransferJobState.Cancelled => note ?? "",
                _ => "",
            };
            _detail.ForeColor = Job.State == TransferJobState.Failed ? Color.FromArgb(0xC4, 0x2B, 0x1C) : SystemColors.ControlText;
        }

        /// <summary>
        /// Windows 的進度條往前時會慢慢動畫補上；小檔案一下就完成時，畫面停在一半。
        /// 先設到目標的下一格再退回，往回設定不會有動畫，進度條會立即顯示正確位置。
        /// </summary>
        private void SetProgress(int value)
        {
            if (value <= _progress.Value)
            {
                _progress.Value = value;
                return;
            }
            if (value < _progress.Maximum)
            {
                _progress.Value = value + 1;
                _progress.Value = value;
            }
            else
            {
                _progress.Value = value;
                _progress.Value = value - 1;
                _progress.Value = value;
            }
        }

        private TimeSpan? Remaining()
        {
            if (_speed < 1)
                return null;
            var left = Math.Max(0, Job.TotalBytes - Job.BytesTransferred);
            return TimeSpan.FromSeconds(left / _speed);
        }
    }
}
