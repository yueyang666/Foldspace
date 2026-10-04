using System.Runtime.InteropServices;
using Foldspace.Localization;

namespace Foldspace.App.Platform;

/// <summary>在桌面建立「Foldspace.lnk」，icon 用 Windows 內建的資料夾圖示。</summary>
public static class DesktopShortcut
{
    public static string PathFor(AppPaths paths) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), paths.ShortcutName + ".lnk");

    public static void Create(AppPaths paths)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the program path");
        ShellLinkFile.Save(PathFor(paths), exe, paths.ProfileArguments, Strings.Current.ShortcutDescription,
            ShellIcons.FolderIconFile, ShellIcons.FolderIconIndex);
        NotifyShell(PathFor(paths), created: true);
    }

    public static void Delete(AppPaths paths)
    {
        var path = PathFor(paths);
        if (!File.Exists(path))
            return;
        File.Delete(path);
        NotifyShell(path, created: false);
    }

    /// <summary>通知檔案總管桌面有變化，不必按 F5 就會更新。</summary>
    public static void NotifyShell(string path, bool created)
    {
        const int SHCNE_CREATE = 0x00000002, SHCNE_DELETE = 0x00000004, SHCNE_UPDATEDIR = 0x00001000;
        const uint SHCNF_PATHW = 0x0005;
        SHChangeNotify(created ? SHCNE_CREATE : SHCNE_DELETE, SHCNF_PATHW, path, IntPtr.Zero);
        SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, Path.GetDirectoryName(path)!, IntPtr.Zero);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);
}
