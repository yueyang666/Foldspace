namespace Foldspace.App.Platform;

/// <summary>
/// 設定、憑證、日誌的位置。開發用的 <c>--profile &lt;名稱&gt;</c> 讓同一台電腦跑兩個實例互測，
/// 各自有獨立的設定、單一執行個體鎖與捷徑名稱。
/// </summary>
public sealed class AppPaths
{
    public const string AppName = "Foldspace";
    public const string DisplayName = "Foldspace";

    /// <summary>命令列參數：請執行中的 Foldspace 正常結束（安裝腳本換版前使用）。</summary>
    public const string ExitArgument = "--exit";

    /// <summary>命令列參數：解除安裝（開始功能表與「設定 &gt; 應用程式」使用）。</summary>
    public const string UninstallArgument = "--uninstall";

    /// <summary>命令列參數：以系統管理員身分移除防火牆規則（解除安裝時由 UAC 提升權限的子程序使用）。</summary>
    public const string RemoveFirewallRulesArgument = "--remove-firewall-rules";

    /// <summary>命令列參數：以系統管理員身分加上防火牆規則（啟動時由 UAC 提升權限的子程序使用）。</summary>
    public const string SetupFirewallArgument = "--setup-firewall";

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    private AppPaths(string? profile)
    {
        Profile = profile;
        DataDir = profile is null ? Root : Path.Combine(Root, "profiles", profile);
    }

    public string? Profile { get; }
    public string DataDir { get; }
    public string SettingsFile => Path.Combine(DataDir, "settings.json");
    public string IdentityFile => Path.Combine(DataDir, "identity.pfx.dpapi");
    public string LogDir => Path.Combine(DataDir, "logs");

    public string InstanceSuffix => Profile is null ? "" : "-" + Profile;
    public string MutexName => $@"Local\{AppName}{InstanceSuffix}";
    public string PipeName => $"{AppName}-{Environment.UserName}{InstanceSuffix}";
    public string ShortcutName => Profile is null ? DisplayName : $"{DisplayName} ({Profile})";
    /// <summary>
    /// 程式明確指定的 AppUserModelID。沒有指定時，Windows 會把指向同一個 exe 的桌面捷徑（資料夾圖示）
    /// 當成程式本身，工作列與通知都會顯示資料夾圖示。開始功能表的捷徑帶同一個 ID。
    /// </summary>
    public string AppUserModelId => "yueyang." + AppName + (Profile is null ? "" : "." + Profile);
    /// <summary>捷徑、開機啟動要帶的參數（只有開發用的 --profile）。</summary>
    public string ProfileArguments => Profile is null ? "" : $"--profile {Profile}";

    /// <summary>
    /// 是不是本程式的 exe 檔名：Foldspace.exe，或發佈時的 Foldspace-1.0.0-win-x64.exe。
    /// 解除安裝時用來找防火牆規則與通知註冊。
    /// </summary>
    public static bool IsProgramFileName(string fileName) =>
        fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        fileName.StartsWith(AppName, StringComparison.OrdinalIgnoreCase);

    /// <summary>從命令列取出 --profile，回傳剩下的參數（拖入的路徑）。</summary>
    public static (AppPaths Paths, string[] DroppedPaths) FromArgs(string[] args)
    {
        string? profile = null;
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--profile" && i + 1 < args.Length)
            {
                var name = args[++i];
                if (name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                    profile = name;
                continue;
            }
            rest.Add(args[i]);
        }
        return (new AppPaths(profile), rest.ToArray());
    }
}
