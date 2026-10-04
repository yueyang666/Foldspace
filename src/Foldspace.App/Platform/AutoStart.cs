using Microsoft.Win32;

namespace Foldspace.App.Platform;

/// <summary>開機自動啟動：HKCU\Software\Microsoft\Windows\CurrentVersion\Run。</summary>
public static class AutoStart
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Set(AppPaths paths, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        var name = AppPaths.AppName + paths.InstanceSuffix;
        if (enabled)
        {
            var args = paths.Profile is null ? "" : $" --profile {paths.Profile}";
            key.SetValue(name, $"\"{Environment.ProcessPath}\"{args}");
        }
        else
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
