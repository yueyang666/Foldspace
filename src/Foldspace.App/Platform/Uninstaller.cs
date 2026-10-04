using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;
using Foldspace.App.Views;
using Foldspace.Core.Settings;
using Foldspace.Core.Transfer;
using Foldspace.Localization;

namespace Foldspace.App.Platform;

/// <summary>
/// 解除安裝：移除程式在系統裡留下的所有東西，只保留接收資料夾裡的檔案。
/// 防火牆規則是 Windows 在第一次允許連線時建立的，移除需要系統管理員權限，
/// 由一個透過 UAC 提升權限的子程序（本程式加上 <see cref="AppPaths.RemoveFirewallRulesArgument"/>）處理。
/// </summary>
public static class Uninstaller
{
    private static Strings T => Strings.Current;

    /// <param name="beforeRemoval">使用者確認後、開始移除前執行（執行中的程式用來通知對方並停止服務）。</param>
    /// <returns>是否已解除安裝（使用者按取消時為 false）。</returns>
    public static async Task<bool> RunAsync(AppPaths paths, AppSettings settings, bool transfersActive, Func<Task>? beforeRemoval)
    {
        var firewallPrograms = FindFirewallPrograms();
        if (!Dialogs.Confirm(null, AppPaths.DisplayName,
                T.UninstallConfirm(settings.ReceiveFolder, firewallPrograms.Count > 0, transfersActive),
                T.UninstallButton, TaskDialogIcon.Warning))
            return false;

        Log.Information("Uninstalling; firewall rules found for {Count} program path(s)", firewallPrograms.Count);
        if (beforeRemoval is not null)
            await beforeRemoval();

        var firewallLeft = firewallPrograms.Count > 0 && !RemoveFirewallRulesElevated(firewallPrograms);
        RemoveEverything(paths, settings);
        Dialogs.Show(null, AppPaths.DisplayName, T.UninstallDone(settings.ReceiveFolder, firewallLeft), TaskDialogIcon.Information);

        // 開發用的 --profile 實例共用同一個 exe，不刪程式本身。
        if (paths.Profile is null)
            ScheduleSelfDelete();
        return true;
    }

    // ================= 防火牆 =================

    /// <summary>防火牆裡屬於 Foldspace 的規則對應的程式路徑。讀取規則不需要系統管理員權限。</summary>
    public static IReadOnlyList<string> FindFirewallPrograms()
    {
        var programs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null)
                return [];
            dynamic policy = Activator.CreateInstance(type)!;
            foreach (dynamic rule in policy.Rules)
            {
                string? program = rule.ApplicationName;
                if (!string.IsNullOrEmpty(program) && AppPaths.IsProgramFileName(Path.GetFileName(program)))
                    programs.Add(program);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Could not read firewall rules: {Error}", ex.Message);
        }
        return [.. programs];
    }

    /// <summary>以 UAC 提升權限執行本程式移除規則；使用者拒絕或沒有移除乾淨時回傳 false。</summary>
    private static bool RemoveFirewallRulesElevated(IReadOnlyList<string> programs)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = AppPaths.RemoveFirewallRulesArgument + " " + string.Join(' ', programs.Select(p => $"\"{p}\"")),
        };
        try
        {
            using var process = Process.Start(start);
            process?.WaitForExit();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED：使用者在 UAC 按了「否」
        {
            Log.Information("Firewall rule removal declined at the UAC prompt");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning("Could not start firewall rule removal: {Error}", ex.Message);
            return false;
        }

        var left = FindFirewallPrograms().Intersect(programs, StringComparer.OrdinalIgnoreCase).ToList();
        Log.Information(left.Count == 0 ? "Firewall rules removed" : "Firewall rules still present for {Programs}", left);
        return left.Count == 0;
    }

    /// <summary>在提升權限的子程序裡執行：刪除這些程式的所有防火牆規則。</summary>
    public static int RemoveFirewallRules(IEnumerable<string> programs)
    {
        var exitCode = 0;
        foreach (var program in programs)
        {
            using var netsh = Process.Start(new ProcessStartInfo("netsh.exe")
            {
                Arguments = $"advfirewall firewall delete rule name=all program=\"{program}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            netsh?.WaitForExit();
            if (netsh is null || netsh.ExitCode != 0)
                exitCode = 1;
        }
        return exitCode;
    }

    // ================= 設定、捷徑、登錄 =================

    private static void RemoveEverything(AppPaths paths, AppSettings settings)
    {
        Try("autostart entry", () => AutoStart.Set(paths, enabled: false));
        Try("desktop shortcut", () => DesktopShortcut.Delete(paths));
        Try("Start menu and Apps entries", () => Installation.Unregister(paths));
        Try("notification registration", () =>
        {
            ToastNotificationManagerCompat.Uninstall();
            // 包含用 exe 路徑算出的 ID；--profile 實例只移除自己的。
            ToastRegistrations.Remove(keep: id => paths.Profile is not null && id != paths.AppUserModelId);
        });
        Try("temp folder in the receive folder", () =>
        {
            var temp = TempArea.Root(settings.ReceiveFolder);
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        });

        // 設定、設備憑證與日誌：日誌檔開著，先關閉日誌再刪。之後不會再有日誌。
        Log.Information("Uninstall finished; removing the data folder {Dir}", paths.DataDir);
        Log.CloseAndFlush();
        var dataDirs = paths.Profile is null ? new[] { AppPaths.Root } : [paths.DataDir];
        foreach (var dir in dataDirs.Where(Directory.Exists))
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(200); // 防毒軟體或檔案總管可能還短暫開著檔案
                }
            }
        }
    }

    private static void Try(string what, Action action)
    {
        try
        {
            action();
            Log.Information("Removed {What}", what);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not remove {What}: {Error}", what, ex.Message);
        }
    }

    /// <summary>
    /// 執行中的 exe 不能刪除自己：交給一個隱藏的 PowerShell 等本程序結束後再刪。
    /// 一併刪除單一檔案執行時解壓縮的原生程式庫（%TEMP%\.net\Foldspace），
    /// 以及 exe 所在的資料夾（只限名稱是 Foldspace 且已經空了，避免刪到使用者的資料夾）。
    /// </summary>
    private static void ScheduleSelfDelete()
    {
        var exe = Environment.ProcessPath!;
        var dir = Path.GetDirectoryName(exe)!;
        var extracted = Path.Combine(Path.GetTempPath(), ".net", AppPaths.AppName);
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

        var script = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500; "
            + $"Remove-Item -LiteralPath {Quote(exe)} -Force -ErrorAction SilentlyContinue; "
            + $"Remove-Item -LiteralPath {Quote(extracted)} -Recurse -Force -ErrorAction SilentlyContinue; ";
        if (string.Equals(Path.GetFileName(dir), AppPaths.AppName, StringComparison.OrdinalIgnoreCase))
            script += $"if (-not (Get-ChildItem -LiteralPath {Quote(dir)} -Force -ErrorAction SilentlyContinue)) "
                + $"{{ Remove-Item -LiteralPath {Quote(dir)} -Force -ErrorAction SilentlyContinue }}";

        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", script })
            start.ArgumentList.Add(arg);
        try
        {
            Process.Start(start)?.Dispose();
        }
        catch (Exception)
        {
            // 日誌已關閉；刪不掉 exe 只會留下程式檔本身。
        }
    }
}
