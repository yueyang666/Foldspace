using System.Runtime.InteropServices;
using Microsoft.Win32;
using Foldspace.Localization;

namespace Foldspace.App.Platform;

/// <summary>
/// 讓免安裝的 exe 也有一般程式的「安裝」痕跡：出現在「設定 &gt; 應用程式」並可從那裡解除安裝，
/// 開始功能表有「Foldspace」資料夾（程式本身與解除安裝）。都寫在目前使用者底下，不需要系統管理員權限。
/// 每次啟動都重寫一次：exe 被移動或換了介面語言時會跟著更新。
/// </summary>
public static class Installation
{
    private const string UninstallKeyRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    private static string UninstallKey(AppPaths paths) => $@"{UninstallKeyRoot}\{AppPaths.AppName}{paths.InstanceSuffix}";

    /// <summary>開始功能表 &gt; 所有應用程式 &gt; Foldspace。</summary>
    public static string StartMenuFolder(AppPaths paths) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), paths.ShortcutName);

    public static void Register(AppPaths paths)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the program path");
        var uninstallCommand = $"\"{exe}\" {AppPaths.UninstallArgument}" + (paths.Profile is null ? "" : " " + paths.ProfileArguments);

        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey(paths)))
        {
            key.SetValue("DisplayName", paths.ShortcutName);
            key.SetValue("DisplayVersion", AppInfo.ProductVersion);
            key.SetValue("DisplayIcon", $"\"{exe}\",0");
            key.SetValue("Publisher", "yueyang");
            key.SetValue("InstallLocation", Path.GetDirectoryName(exe)!);
            key.SetValue("UninstallString", uninstallCommand);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
        }

        // 開始功能表：程式本身 + 解除安裝。先清掉資料夾裡其他捷徑（例如換語言前的解除安裝捷徑名稱）。
        var folder = StartMenuFolder(paths);
        Directory.CreateDirectory(folder);
        var appLink = Path.Combine(folder, paths.ShortcutName + ".lnk");
        var uninstallLink = Path.Combine(folder, Strings.Current.UninstallShortcutName + ".lnk");
        foreach (var stale in Directory.EnumerateFiles(folder, "*.lnk"))
        {
            if (!string.Equals(stale, appLink, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(stale, uninstallLink, StringComparison.OrdinalIgnoreCase))
                File.Delete(stale);
        }
        ShellLinkFile.Save(appLink, exe, paths.ProfileArguments, Strings.Current.GeneralDescription,
            appUserModelId: paths.AppUserModelId);
        ShellLinkFile.Save(uninstallLink, exe, $"{AppPaths.UninstallArgument} {paths.ProfileArguments}".Trim(),
            Strings.Current.UninstallShortcutName);
    }

    /// <summary>要在顯示任何視窗或通知之前呼叫。</summary>
    public static void SetProcessAppUserModelId(AppPaths paths) => SetCurrentProcessExplicitAppUserModelID(paths.AppUserModelId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);

    public static void Unregister(AppPaths paths)
    {
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey(paths), throwOnMissingSubKey: false);
        var folder = StartMenuFolder(paths);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }
}
