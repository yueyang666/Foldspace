using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace Foldspace.App.Platform;

/// <summary>
/// 讓對方連得進來：在 Windows 防火牆加一條 Foldspace 的輸入規則，不等 Windows 跳出「是否允許存取」。
/// 規則：只限本程式、TCP 與 UDP、私人／公用／網域都適用，但來源只限同一個子網路。
/// 新增規則需要系統管理員權限，由一個透過 UAC 提升權限的子程序（<see cref="AppPaths.SetupFirewallArgument"/>）處理。
/// 讀取規則不需要權限，所以啟動時先檢查，規則已經正確就不會跳 UAC。
/// </summary>
public static class FirewallSetup
{
    public const string RuleName = "Foldspace";

    private const int DirectionIn = 1;          // NET_FW_RULE_DIR_IN
    private const int ActionBlock = 0;          // NET_FW_ACTION_BLOCK
    private const int ProtocolAny = 256;        // NET_FW_IP_PROTOCOL_ANY
    private const int AllProfiles = 0x7;        // 網域 | 私人 | 公用

    public enum Result
    {
        Done,
        /// <summary>使用者在 UAC 按了「否」。</summary>
        Declined,
        Failed,
    }

    private static string Exe => Environment.ProcessPath!;

    /// <summary>
    /// 目前的 exe 有正確的允許規則，且沒有封鎖規則（使用者曾在 Windows 的詢問視窗按「取消」時會留下封鎖規則，
    /// 封鎖優先於允許）。讀不到防火牆設定時回傳 null。
    /// </summary>
    public static bool? IsConfigured()
    {
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null)
                return null;
            dynamic policy = Activator.CreateInstance(type)!;
            bool allowed = false, blocked = false;
            foreach (dynamic rule in policy.Rules)
            {
                string? program = rule.ApplicationName;
                if (!string.Equals(program, Exe, StringComparison.OrdinalIgnoreCase) ||
                    (int)rule.Direction != DirectionIn || !(bool)rule.Enabled)
                    continue;
                if ((int)rule.Action == ActionBlock)
                    blocked = true;
                else if (rule.Name == RuleName && ((int)rule.Profiles & AllProfiles) == AllProfiles &&
                         (int)rule.Protocol == ProtocolAny && rule.RemoteAddresses == "LocalSubnet")
                    allowed = true;
            }
            return allowed && !blocked;
        }
        catch (Exception ex)
        {
            Log.Warning("Could not read firewall rules: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 啟動時呼叫：規則不對就跳 UAC 設定。使用者對這個 exe 拒絕過就不再每次詢問
    /// （可以在設定視窗的「連線」分頁重試）；exe 換了位置會再問一次。
    /// </summary>
    public static void EnsureOnStartup(AppPaths paths)
    {
        var configured = IsConfigured();
        if (configured is not false)
            return;
        var declinedFile = DeclinedFile(paths);
        if (File.Exists(declinedFile) && string.Equals(File.ReadAllText(declinedFile).Trim(), Exe, StringComparison.OrdinalIgnoreCase))
        {
            Log.Information("Firewall rule is missing; setup was declined before");
            return;
        }
        if (Run() == Result.Declined)
        {
            try { File.WriteAllText(declinedFile, Exe); }
            catch (IOException) { }
        }
    }

    /// <summary>以 UAC 提升權限執行本程式設定規則，完成後重新檢查。</summary>
    public static Result Run(AppPaths? paths = null)
    {
        var start = new ProcessStartInfo(Exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"{AppPaths.SetupFirewallArgument} \"{Exe}\"",
        };
        try
        {
            using var process = Process.Start(start);
            process?.WaitForExit();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED：使用者在 UAC 按了「否」
        {
            Log.Information("Firewall setup declined at the UAC prompt");
            return Result.Declined;
        }
        catch (Exception ex)
        {
            Log.Warning("Could not start firewall setup: {Error}", ex.Message);
            return Result.Failed;
        }

        var ok = IsConfigured() == true;
        Log.Information(ok ? "Firewall rule added" : "Firewall setup finished but the rule is not in place");
        if (ok && paths is not null)
            File.Delete(DeclinedFile(paths));
        return ok ? Result.Done : Result.Failed;
    }

    /// <summary>在提升權限的子程序裡執行：刪掉這個 exe 的所有規則（含 Windows 自動建立的封鎖規則），再加上 Foldspace 的規則。</summary>
    public static int Configure(string exe)
    {
        Netsh($"advfirewall firewall delete rule name=all program=\"{exe}\"");
        return Netsh($"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exe}\" " +
                     "enable=yes profile=any protocol=any remoteip=localsubnet");
    }

    private static int Netsh(string arguments)
    {
        using var netsh = Process.Start(new ProcessStartInfo("netsh.exe")
        {
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        netsh?.WaitForExit();
        return netsh?.ExitCode ?? 1;
    }

    private static string DeclinedFile(AppPaths paths) => Path.Combine(paths.DataDir, "firewall-declined");
}
