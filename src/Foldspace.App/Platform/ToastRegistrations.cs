using Microsoft.Win32;
using Serilog;

namespace Foldspace.App.Platform;

/// <summary>
/// 通知套件替每個 AppUserModelID 留下的註冊：
/// HKCU\Software\Classes\AppUserModelId\&lt;ID&gt;（CustomActivator 指向一個 CLSID）、
/// HKCU\Software\Classes\CLSID\&lt;CLSID&gt;\LocalServer32（本程式路徑）、
/// %LocalAppData%\ToastNotificationManagerCompat\Apps\&lt;ID&gt;（通知圖示）。
/// 沒有指定 AppUserModelID 時，套件會用 exe 路徑算出 ID 另外註冊；本程式使用固定的 ID，
/// 所以要清掉這種註冊，否則「設定 &gt; 通知」會出現兩個 Foldspace。
/// </summary>
public static class ToastRegistrations
{
    /// <summary>移除指向 Foldspace 的通知註冊，<paramref name="keep"/> 回傳 true 的 ID 除外。</summary>
    public static void Remove(Func<string, bool> keep)
    {
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\AppUserModelId", writable: true);
        if (root is null)
            return;
        foreach (var id in root.GetSubKeyNames())
        {
            if (keep(id))
                continue;
            try
            {
                string? clsid;
                using (var key = root.OpenSubKey(id))
                    clsid = key?.GetValue("CustomActivator") as string;
                if (clsid is null)
                    continue;
                var clsidPath = $@"Software\Classes\CLSID\{clsid}";
                string? server;
                using (var key = Registry.CurrentUser.OpenSubKey(clsidPath + @"\LocalServer32"))
                    server = key?.GetValue(null) as string;
                if (server is null || !AppPaths.IsProgramFileName(Path.GetFileName(ProgramPath(server))))
                    continue; // 不是本程式的註冊

                Registry.CurrentUser.DeleteSubKeyTree(clsidPath, throwOnMissingSubKey: false);
                root.DeleteSubKeyTree(id, throwOnMissingSubKey: false);
                var icons = IconFolder(id);
                if (Directory.Exists(icons))
                    Directory.Delete(icons, recursive: true);
                Log.Information("Removed notification registration {Id}", id);
            }
            catch (Exception ex)
            {
                Log.Warning("Could not remove notification registration {Id}: {Error}", id, ex.Message);
            }
        }
    }

    /// <summary>套件放通知圖示的資料夾：ID 含路徑分隔字元時（用 exe 路徑算出的 ID）改用 ID 算出的 GUID 當名稱。</summary>
    private static string IconFolder(string id) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToastNotificationManagerCompat", "Apps",
            id.Contains('\\') || id.Contains('/') ? GenerateGuid(id) : id);

    /// <summary>與通知套件（Win32AppInfo.GenerateGuid）相同的算法。</summary>
    internal static string GenerateGuid(string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        var guid = new byte[16];
        if (bytes.Length < 16)
            Array.Copy(bytes, guid, bytes.Length);
        else
            Array.Copy(System.Security.Cryptography.SHA1.HashData(bytes), guid, 16);
        var hex = Convert.ToHexString(guid);
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    /// <summary>命令列（例如 <c>"C:\x\Foldspace.exe" -ToastActivated</c>）裡的程式路徑。</summary>
    internal static string ProgramPath(string commandLine)
    {
        var text = commandLine.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 0 ? text[1..end] : text[1..];
        }
        var space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }
}
