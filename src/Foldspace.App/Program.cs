using Serilog;
using Foldspace.Localization;
using Foldspace.App.Platform;
using Foldspace.Core.Settings;

namespace Foldspace.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 解除安裝時以 UAC 提升權限啟動的子程序：只移除防火牆規則就結束。
        if (args is [AppPaths.RemoveFirewallRulesArgument, .. var programs])
        {
            Environment.ExitCode = Uninstaller.RemoveFirewallRules(programs);
            return;
        }
        // 啟動時以 UAC 提升權限的子程序：只加上防火牆規則就結束。
        if (args is [AppPaths.SetupFirewallArgument, var exe])
        {
            Environment.ExitCode = FirewallSetup.Configure(exe);
            return;
        }

        var (paths, droppedPaths) = AppPaths.FromArgs(args);
        // 在任何視窗與通知之前：讓工作列和通知用開始功能表捷徑（程式圖示），而不是桌面的資料夾捷徑。
        try { Installation.SetProcessAppUserModelId(paths); } catch { /* 沒有也能執行，只是圖示不對 */ }
        // 使用者點了舊的通知而啟動程式時，參數不是檔案路徑。
        if (Toasts.WasProcessToastActivated())
            droppedPaths = [];

        ConfigureLogging(paths);
        // 介面語言：設定檔的 language（auto / en / zh-Hant / zh-Hans），auto 跟隨 Windows 顯示語言。
        Strings.Initialize(new SettingsStore(paths.SettingsFile, Log.Logger).Load().Language);

        // --exit：請執行中的 Foldspace 正常結束；本身不啟動。
        var exitRequested = droppedPaths.Contains(AppPaths.ExitArgument);

        using var instance = SingleInstance.TryAcquire(paths);
        if (exitRequested)
        {
            if (instance is null && !SingleInstance.SendToRunningInstance(paths, [AppPaths.ExitArgument]))
                Log.Warning("Could not reach the running Foldspace; exit request not delivered");
            Log.CloseAndFlush();
            return;
        }
        if (instance is null)
        {
            // 已在執行：把參數交給它（沒有參數 = 請它顯示設定視窗），自己立即結束。
            // 本程序是使用者剛啟動的（有前景權限），交給執行中的那一份，它的視窗才能出現在最前面。
            AllowSetForegroundWindow(-1 /* ASFW_ANY */);
            if (!SingleInstance.SendToRunningInstance(paths, droppedPaths))
                Log.Warning("Could not reach the running Foldspace");
            Log.CloseAndFlush();
            return;
        }

        // 原生 Windows 外觀：系統訊息字型（繁中為微軟正黑體 9pt）、視覺樣式、依螢幕 DPI 縮放。
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(SystemFonts.MessageBoxFont ?? Control.DefaultFont);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            // UI 執行緒例外：記錄、顯示一句話，程式繼續執行。
            Log.Error(e.Exception, "Unhandled exception on the UI thread");
            Views.Dialogs.Show(null, AppPaths.DisplayName, Strings.Current.UnexpectedError(e.Exception.Message), TaskDialogIcon.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        // 沒有執行中的 Foldspace 時直接解除安裝（執行中的話，上面已經把 --uninstall 交給它處理）。
        if (droppedPaths is [AppPaths.UninstallArgument])
        {
            var settings = new SettingsStore(paths.SettingsFile, Log.Logger).Load();
            Uninstaller.RunAsync(paths, settings, transfersActive: false, beforeRemoval: null).GetAwaiter().GetResult();
            Log.CloseAndFlush();
            return;
        }

        // 讓 await 之後回到 UI 執行緒。
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var ui = SynchronizationContext.Current!;

        Log.Information("Foldspace {Version} starting ({Language}), data folder {Dir}",
            AppInfo.BuildVersion,
            Strings.Current.CultureName, paths.DataDir);
        AppController controller;
        try
        {
            controller = new AppController(paths, ui);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            Views.Dialogs.Show(null, AppPaths.DisplayName, Strings.Current.StartupFailed(ex.Message, paths.LogDir), TaskDialogIcon.Error);
            Log.CloseAndFlush();
            return;
        }

        instance.Listen(received => ui.Post(_ => controller.HandleArgs(received), null));
        ui.Post(async _ =>
        {
            try
            {
                await controller.StartAsync(droppedPaths);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Startup failed");
                Views.Dialogs.Show(null, AppPaths.DisplayName, Strings.Current.StartupFailed(ex.Message, paths.LogDir), TaskDialogIcon.Error);
                Application.Exit();
            }
        }, null);

        Application.Run(); // 沒有主視窗：常駐在系統匣，直到選單的「結束」
        Log.CloseAndFlush();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>%AppData%\Foldspace\logs\foldspace-YYYYMMDD.log，保留 14 天，單檔 10 MB。</summary>
    private static void ConfigureLogging(AppPaths paths)
    {
        Directory.CreateDirectory(paths.LogDir);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogDir, "foldspace-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: null,
                retainedFileTimeLimit: TimeSpan.FromDays(14),
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
