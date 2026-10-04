using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using Serilog;
using Foldspace.Localization;
using Foldspace.App.Platform;
using Foldspace.App.Views;
using Foldspace.Core;
using Foldspace.Core.Identity;
using Foldspace.Core.Net;
using Foldspace.Core.Settings;
using Foldspace.Core.Transfer;

namespace Foldspace.App;

/// <summary>常駐程式的核心：持有設定、服務、系統匣與視窗，並處理睡眠、網路變化等系統事件。</summary>
public sealed class AppController
{
    private static readonly TimeSpan OfflineToastInterval = TimeSpan.FromMinutes(10);

    private readonly SynchronizationContext _ui;
    private readonly SettingsStore _store;
    private readonly SettingsPairingStore _pairingStore;
    private readonly Lock _settingsGate = new();
    private readonly Toasts _toasts = new();
    private readonly DeviceIdentity _identity;
    private AppSettings _settings;
    private PeerService _service;
    private TrayIcon? _tray;
    private SettingsForm? _settingsForm;
    private TransferForm? _transferForm;
    private PeerState _lastState = PeerState.Disabled;
    private bool _wasConnected;
    private DateTimeOffset _lastOfflineToast = DateTimeOffset.MinValue;
    private bool _exiting;

    /// <param name="ui">UI 執行緒的同步內容；服務的事件在背景執行緒觸發，要切回這裡處理。</param>
    public AppController(AppPaths paths, SynchronizationContext ui)
    {
        _ui = ui;
        Paths = paths;
        _store = new SettingsStore(paths.SettingsFile, Log.Logger);
        _settings = _store.Load();
        _pairingStore = new SettingsPairingStore(() => Settings, UpdateSettings);
        _identity = DeviceIdentity.LoadOrCreate(paths.IdentityFile, new DpapiKeyProtector(), Environment.MachineName, Log.Logger);
        _service = CreateService(_settings);
    }

    public AppPaths Paths { get; }

    private static Strings T => Strings.Current;

    public AppSettings Settings
    {
        get { lock (_settingsGate) return _settings; }
    }

    public PeerService Service => _service;
    public PeerStatus Status => _service.Status;
    public string LocalFingerprint => _identity.Fingerprint;

    /// <summary>連線狀態改變（已切回 UI 執行緒）。</summary>
    public event Action<PeerStatus>? StatusChanged;

    /// <summary>配對結果（已切回 UI 執行緒）。</summary>
    public event Action<PairingOutcome>? PairingFinished;

    /// <summary>對方位址改了（從搜尋結果選擇、對方發起配對、或對方換了 IP），已存回設定（已切回 UI 執行緒）。</summary>
    public event Action<IPEndPoint>? PeerEndpointChanged;

    /// <summary>
    /// 從搜尋結果選了一台：改連那台並發起配對（兩邊會跳出配對碼對話框）。
    /// 已經和那台配對時（例如對方換了 IP）只更新位址。結果由 <see cref="PairingFinished"/> 通知。
    /// </summary>
    public async Task ConnectAndPairAsync(DiscoveredPeer peer)
    {
        Log.Information("Pairing with discovered device {Hostname} ({Address}:{Port})", peer.Hostname, peer.Address, peer.Port);
        _service.SetPeerEndpoint(peer.Address, peer.Port);
        if (peer.PairedWithMe && IsPairedWith(peer.Fingerprint))
            return;
        // 握手完成（未配對）才能發起配對；連不上時 PairAsync 會回報「無法連線」。
        await _service.WaitForStatusAsync(s => s.State is PeerState.Unpaired or PeerState.Connected or PeerState.Error, TimeSpan.FromSeconds(8));
        await _service.PairAsync();
    }

    private bool _speedTestRunning;

    /// <summary>網路速度測試（診斷用）：不加密、加密各 5 秒，結果用對話框顯示。</summary>
    public async Task RunSpeedTestAsync()
    {
        if (_speedTestRunning)
            return;
        var host = Status.PeerHostname ?? T.ThePeer;
        if (Status.State != PeerState.Connected)
        {
            Dialogs.Show(null, T.SpeedTestTitle, T.SpeedTestNotConnected, TaskDialogIcon.Warning);
            return;
        }

        _speedTestRunning = true;
        _toasts.Show(T.SpeedTestTitle, T.SpeedTestRunning(host));
        try
        {
            var duration = TimeSpan.FromSeconds(5);
            var plain = await _service.RunSpeedTestAsync(encrypted: false, duration);
            var parallel = await _service.RunParallelSpeedTestAsync(4, encrypted: false, duration);
            var encrypted = await _service.RunSpeedTestAsync(encrypted: true, duration);
            Dialogs.Show(null, T.SpeedTestTitle, T.SpeedTestResultText(host, plain, parallel, encrypted), TaskDialogIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or TimeoutException
                                       or InvalidOperationException or Core.Protocol.ProtocolException
                                       or System.Security.Authentication.AuthenticationException)
        {
            Log.Warning("Speed test failed: {Error}", Core.Net.NetworkError.Describe(ex));
            Dialogs.Show(null, T.SpeedTestTitle, T.SpeedTestFailed(ex.Message), TaskDialogIcon.Warning);
        }
        finally
        {
            _speedTestRunning = false;
        }
    }

    private bool IsPairedWith(string fingerprint) => string.Equals(Settings.PeerFingerprint, fingerprint, StringComparison.Ordinal);

    // ================= 啟動 / 結束 =================

    public async Task StartAsync(string[] initialPaths)
    {
        _tray = new TrayIcon(this);
        _tray.Update(Status, Settings.ServiceEnabled);

        // 「設定 > 應用程式」與開始功能表（每次啟動都更新，exe 被移動時跟著改）
        try { Installation.Register(Paths); }
        catch (Exception ex) { Log.Warning("Could not register in Apps and the Start menu: {Error}", ex.Message); }
        // 通知套件可能用 exe 路徑另外註冊過（沒有固定 AppUserModelID 時）：清掉，只留現在的 ID。
        ToastRegistrations.Remove(keep: id => id.StartsWith("yueyang." + AppPaths.AppName, StringComparison.OrdinalIgnoreCase));

        if (!Settings.DesktopShortcutCreated)
        {
            TryCreateShortcut();
            UpdateSettings(s => s with { DesktopShortcutCreated = true });
        }
        else if (File.Exists(DesktopShortcut.PathFor(Paths)))
        {
            // 捷徑還在：更新它指向目前的 exe（exe 被移動或換到新的安裝位置時，舊捷徑會失效）。
            TryCreateShortcut(quiet: true);
        }

        // 只會有一個執行個體，啟動時不可能有進行中的傳輸，留下的暫存全部是上次中斷或當掉的殘留。
        // （只清超過 24 小時的話，上次留下的空資料夾會一直在，所以全部清除。）
        ReconcileAdapter();
        TempArea.CleanStale(Settings.ReceiveFolder, TimeSpan.Zero, Log.Logger);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        SystemEvents.SessionEnding += OnSessionEnding;

        // 開始監聽之前先設好防火牆，Windows 就不會跳出「是否允許存取」（需要時跳 UAC）。
        await Task.Run(() => FirewallSetup.EnsureOnStartup(Paths));

        if (Settings.ServiceEnabled)
            await _service.StartAsync();

        if (initialPaths.Length > 0)
            await HandleDropAsync(initialPaths, justStarted: true);
        else if (Settings.PeerAddress is null)
            ShowSettings(); // 首次執行：直接打開設定讓使用者填對方 IP
    }

    public async Task ExitAsync(bool askIfBusy = true)
    {
        if (_exiting)
            return;
        if (askIfBusy && HasActiveTransfers() &&
            !Dialogs.Confirm(null, AppPaths.DisplayName, T.ConfirmExitWhileBusy, T.TrayExit, TaskDialogIcon.Warning))
            return;

        _exiting = true;
        Log.Information("Exiting");
        await ShutdownAsync();
        Application.Exit();
    }

    /// <summary>解除安裝：確認後通知對方解除配對、停止服務，再移除所有東西並結束。</summary>
    public async Task UninstallAsync()
    {
        if (_exiting)
            return;
        var uninstalled = await Uninstaller.RunAsync(Paths, Settings, HasActiveTransfers(), async () =>
        {
            _exiting = true;
            // 先通知對方解除配對：對方會顯示「未配對」，而不是一直顯示「離線」。
            if (Settings.IsPaired && Status.State == PeerState.Connected)
            {
                try { await _service.UnpairAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (Exception ex) { Log.Warning("Could not notify the peer: {Error}", ex.Message); }
            }
            await ShutdownAsync();
        });
        if (uninstalled)
            Application.Exit();
    }

    private async Task ShutdownAsync()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        try
        {
            await _service.StopAsync("exiting").WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Stopping the service timed out");
        }
        _tray?.Dispose();
        SystemEvents.SessionEnding -= OnSessionEnding;
    }

    // ================= 服務 =================

    private PeerService CreateService(AppSettings s)
    {
        var service = new PeerService(new PeerServiceOptions
        {
            Identity = _identity,
            Hostname = Environment.MachineName,
            ResolveLocalAddress = () => NetworkAdapters.Resolve(Settings)?.Address,
            LocalPort = s.LocalPort,
            PeerAddress = s.PeerAddress,
            PeerPort = s.PeerPort,
            Encryption = s.Encryption,
            Logger = Log.Logger,
        }, _pairingStore, () =>
        {
            var current = Settings;
            return new ReceiveOptions(current.ReceiveFolder, current.ConflictPolicy, current.AskBeforeReceive);
        });

        service.StatusChanged += status => OnUi(() => OnStatusChanged(status));
        service.JobAdded += job => OnUi(() => OnJobAdded(job));
        service.PairingPromptRequested += prompt => OnUi(() => PairingDialog.Show(prompt));
        service.PairingFinished += outcome => OnUi(() => OnPairingFinished(outcome));
        // 位址由服務自己改（不必重啟服務），這裡只存回設定。
        service.PeerEndpointChanged += endpoint => OnUi(() =>
        {
            UpdateSettings(s => s with { PeerIp = endpoint.Address.ToString(), PeerPort = endpoint.Port });
            PeerEndpointChanged?.Invoke(endpoint);
        });
        // 傳送端會收到拒絕原因，但要處理的是本機的使用者（接收資料夾不能用、空間不足）。
        service.IncomingRejected += rejection => OnUi(() =>
            _toasts.Show(T.IncomingRejectedTitle(rejection.PeerHostname), T.IncomingRejectedText(rejection, Settings.ReceiveFolder)));
        service.AskBeforeReceive = (offer, ct) => _toasts.AskAsync(
            T.IncomingOfferTitle(offer.PeerHostname),
            T.IncomingOfferText(T.Items(offer.ItemName, offer.ItemCount), offer.FileCount, Format.Bytes(offer.TotalBytes)),
            ct);
        return service;
    }

    /// <summary>IP、port、加密設定改變時重新建立服務。</summary>
    private async Task RestartServiceAsync()
    {
        var old = _service;
        await old.StopAsync("settings changed; restarting");
        _service = CreateService(Settings);
        if (Settings.ServiceEnabled)
            await _service.StartAsync();
        OnStatusChanged(_service.Status);
    }

    public async Task<bool> SetServiceEnabledAsync(bool enabled)
    {
        if (enabled == Settings.ServiceEnabled)
            return true;

        if (!enabled && HasActiveTransfers() &&
            !Dialogs.Confirm(null, AppPaths.DisplayName, T.ConfirmDisableWhileBusy, T.ButtonDisableService, TaskDialogIcon.Warning))
            return false;

        UpdateSettings(s => s with { ServiceEnabled = enabled });
        if (enabled)
            await _service.StartAsync();
        else
            await _service.StopAsync("disabled by the user");
        OnStatusChanged(_service.Status);
        return true;
    }

    public Task ToggleServiceAsync() => SetServiceEnabledAsync(!Settings.ServiceEnabled);

    /// <summary>儲存設定視窗的修改。</summary>
    public async Task SaveSettingsAsync(AppSettings updated)
    {
        var before = Settings;
        UpdateSettings(_ => updated);

        if (before.StartWithWindows != updated.StartWithWindows)
        {
            try { AutoStart.Set(Paths, updated.StartWithWindows); }
            catch (Exception ex) { Log.Warning(ex, "Could not update start with Windows"); }
        }

        if (before.ReceiveFolder != updated.ReceiveFolder)
        {
            try { Directory.CreateDirectory(updated.ReceiveFolder); }
            catch (Exception ex) { Log.Warning(ex, "Could not create the receive folder {Folder}", updated.ReceiveFolder); }
        }

        var networkChanged = before.LocalInterfaceId != updated.LocalInterfaceId ||
                             before.LocalPort != updated.LocalPort ||
                             before.PeerIp != updated.PeerIp ||
                             before.PeerPort != updated.PeerPort ||
                             before.Encryption != updated.Encryption;
        if (networkChanged)
        {
            Log.Information("Network settings changed; restarting the service");
            await RestartServiceAsync();
        }
    }

    private void UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        lock (_settingsGate)
        {
            _settings = change(_settings);
            try
            {
                _store.Save(_settings);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not save settings");
            }
        }
    }

    private bool HasActiveTransfers() => _service.Jobs.Any(j => !j.IsFinished);

    // ================= 拖放 =================

    /// <summary>其他啟動嘗試（雙擊或拖到捷徑）送來的參數。</summary>
    public void HandleArgs(string[] args)
    {
        if (args is [AppPaths.ExitArgument])
        {
            Log.Information("Exit requested (--exit)");
            _ = ExitAsync(askIfBusy: false);
        }
        else if (args is [AppPaths.UninstallArgument])
            _ = UninstallAsync();
        else if (args.Length == 0)
            ShowSettings();
        else
            _ = HandleDropAsync(args, justStarted: false);
    }

    public async Task HandleDropAsync(IReadOnlyList<string> paths, bool justStarted)
    {
        Log.Information("{Count} item(s) dropped", paths.Count);
        if (!Settings.ServiceEnabled)
        {
            _toasts.Show(T.CannotSend, T.Job(new JobNote(JobIssue.ServiceDisabled), TransferDirection.Send));
            return;
        }

        // 程式剛由拖放啟動：等待連線最多 10 秒。
        if (justStarted && Status.State != PeerState.Connected &&
            !await _service.WaitForConnectedAsync(TimeSpan.FromSeconds(10)))
        {
            _toasts.Show(T.CannotSend, T.Status(Status));
            return;
        }

        try
        {
            _service.Send(paths);
        }
        catch (TransferRejectedException ex)
        {
            _toasts.Show(T.CannotSend, T.Job(ex.Note, TransferDirection.Send));
        }
    }

    // ================= 事件 =================

    private void OnStatusChanged(PeerStatus status)
    {
        _tray?.Update(status, Settings.ServiceEnabled);
        StatusChanged?.Invoke(status);

        var previous = _lastState;
        _lastState = status.State;
        if (status.State == PeerState.Connected && previous != PeerState.Connected)
        {
            // 只在「離線 → 已連線」時通知；配對完成（未配對 → 已連線）由配對結果通知。
            if (_wasConnected && previous is PeerState.Offline or PeerState.Searching)
                _toasts.Show(T.PeerOnlineTitle, T.PeerOnlineText(status.PeerHostname ?? T.ThePeer));
            _wasConnected = true;
        }
        else if (previous == PeerState.Connected && status.State == PeerState.Offline &&
                 DateTimeOffset.UtcNow - _lastOfflineToast > OfflineToastInterval)
        {
            _lastOfflineToast = DateTimeOffset.UtcNow;
            _toasts.Show(T.PeerOfflineTitle, T.PeerOfflineText(status.PeerHostname ?? T.ThePeer));
        }
        else if (status.State == PeerState.Error && previous != PeerState.Error && status.Issue == PeerIssue.PortInUse)
        {
            // port 被占用：跳出設定視窗請使用者更換（不自動換 port，否則對方會連不上）。
            ShowSettings();
        }
    }

    private void OnJobAdded(TransferJob job)
    {
        ShowTransfers();
        _transferForm!.Add(job);
        job.Changed += j =>
        {
            if (j.IsFinished)
                OnUi(() => OnJobFinished(j));
        };
        // 小檔案可能在這裡訂閱之前就已經傳完（程式剛啟動時 UI 執行緒較忙），補發一次。
        if (job.IsFinished)
            OnJobFinished(job);
    }

    private readonly HashSet<Guid> _notifiedJobs = [];

    private void OnJobFinished(TransferJob job)
    {
        if (!_notifiedJobs.Add(job.JobId))
            return;

        var host = job.PeerHostname ?? T.ThePeer;
        var send = job.Direction == TransferDirection.Send;
        var items = T.Items(job.ItemName, job.ItemCount);
        var note = job.Note is { } n ? T.Job(n, job.Direction) : null;
        switch (job.State)
        {
            case TransferJobState.Completed:
            {
                var result = job.Result;
                var count = result?.SucceededFileCount ?? job.FileCount;
                var size = Format.Bytes(result?.ReceivedBytes ?? job.TotalBytes);
                // 同名項目全部略過時一個檔案都沒有新增，不寫「已收到 0 個檔案（5.3 MB）」。
                var title = count == 0 && (result?.ExpectedFileCount ?? 0) > 0 ? T.NoNewFilesTitle(send, host)
                    : send ? T.SentTitle(count, size, host) : T.ReceivedTitle(count, size, host);
                _toasts.Show(title, note ?? items, send ? null : job.FirstFinalPath);
                break;
            }
            case TransferJobState.Failed:
                _toasts.Show(send ? T.SendFailed : T.ReceiveFailed, $"{items}: {note}", send ? null : job.FirstFinalPath);
                break;
            case TransferJobState.Cancelled:
                _toasts.Show(send ? T.SendCancelled : T.ReceiveCancelled, $"{items}: {note}");
                break;
        }
    }

    private void OnPairingFinished(PairingOutcome outcome)
    {
        _toasts.Show(outcome.Success ? T.PairingSucceededTitle : T.PairingFailedTitle, T.Pairing(outcome));
        PairingFinished?.Invoke(outcome);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                Log.Information("Going to sleep");
                if (HasActiveTransfers())
                    _toasts.Show(T.SleepCancelledTitle, T.SleepCancelledText);
                StopServiceBlocking("going to sleep");
                break;

            case PowerModes.Resume:
                Log.Information("Resumed from sleep");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(3)); // 讓網卡就緒
                    if (Settings.ServiceEnabled && !_exiting)
                    {
                        await _service.StartAsync();
                        _service.NudgeReconnect();
                    }
                });
                break;
        }
    }

    /// <summary>Windows 登出、關機：正常關閉連線，不詢問。</summary>
    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        Log.Information("Windows is signing out or shutting down");
        StopServiceBlocking("Windows session ending");
        OnUi(() => _ = ExitAsync(askIfBusy: false));
    }

    /// <summary>
    /// 在系統事件裡同步停止服務。放到執行緒集區執行：SystemEvents 可能在 UI 執行緒觸發，
    /// 直接在這裡等待非同步工作會因為 await 要回到 UI 執行緒而互相卡死。
    /// </summary>
    private void StopServiceBlocking(string reason)
    {
        try { Task.Run(() => _service.StopAsync(reason)).Wait(TimeSpan.FromSeconds(3)); }
        catch (Exception ex) { Log.Warning(ex, "Stopping the service failed: {Reason}", reason); }
    }

    /// <summary>
    /// 設定的網卡被換掉（例如換成 virtio、USB 網卡）但同一個 IP 還在時，把設定改成新的網卡；
    /// 同一張網卡的 IP 改變時也更新記錄的 IP。找不到時不動設定，狀態會顯示「本機 IP 已不存在」。
    /// </summary>
    private void ReconcileAdapter()
    {
        var s = Settings;
        if (string.IsNullOrEmpty(s.LocalInterfaceId))
            return; // 自動選擇
        var match = NetworkAdapters.Resolve(s);
        if (match is null)
            return;
        var ip = match.Address.ToString();
        if (match.Id == s.LocalInterfaceId && ip == s.LocalIp)
            return;
        if (match.Id == s.LocalInterfaceId)
            Log.Information("Updated the recorded IP of adapter {Name}: {Ip} (was {OldIp})", match.Name, ip, s.LocalIp ?? "not recorded");
        else
            Log.Information("Bound adapter changed to {Name} ({Ip}); was {OldId} ({OldIp})", match.Name, ip, s.LocalInterfaceId, s.LocalIp);
        UpdateSettings(x => x with { LocalInterfaceId = match.Id, LocalIp = ip });
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        ReconcileAdapter();
        Log.Information("Network changed; retrying the connection now");
        _service.NudgeReconnect();
    }

    // ================= 視窗與選單 =================

    public void ShowSettings()
    {
        _settingsForm ??= new SettingsForm(this);
        _settingsForm.ShowAndActivate();
    }

    public void ShowTransfers()
    {
        _transferForm ??= new TransferForm(this);
        _transferForm.ShowNearTray();
    }

    /// <summary>設定視窗第一次被按 X 時提醒一次。</summary>
    public void OnSettingsWindowHidden()
    {
        if (Settings.CloseHintShown)
            return;
        UpdateSettings(s => s with { CloseHintShown = true });
        _toasts.Show(T.StillRunningTitle, T.StillRunningText);
    }

    public void OpenReceiveFolder()
    {
        try { Toasts.OpenFolder(Settings.ReceiveFolder); }
        catch (Exception ex) { Log.Warning(ex, "Could not open the receive folder"); }
    }

    public void OpenLogFolder()
    {
        try { Toasts.OpenFolder(Paths.LogDir); }
        catch (Exception ex) { Log.Warning(ex, "Could not open the log folder"); }
    }

    public void RecreateShortcut()
    {
        if (TryCreateShortcut())
            _toasts.Show(T.ShortcutCreatedTitle, T.ShortcutCreatedText);
    }

    private bool TryCreateShortcut(bool quiet = false)
    {
        try
        {
            var existed = File.Exists(DesktopShortcut.PathFor(Paths));
            DesktopShortcut.Create(Paths);
            if (!quiet)
                Log.Information(existed ? "Updated desktop shortcut {Path}" : "Created desktop shortcut {Path}", DesktopShortcut.PathFor(Paths));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not create the desktop shortcut");
            if (!quiet)
                _toasts.Show(T.ShortcutFailedTitle, ex.Message);
            return false;
        }
    }

    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void OnUi(Action action)
    {
        if (_exiting)
            return;
        _ui.Post(_ => action(), null);
    }
}
