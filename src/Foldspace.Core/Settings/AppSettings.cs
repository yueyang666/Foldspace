using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Settings;

public enum ConflictPolicy
{
    Rename,
    Overwrite,
    Skip,
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public bool ServiceEnabled { get; init; } = true;
    /// <summary>綁定監聽的網卡 ID（<c>NetworkInterface.Id</c>）；空白表示自動選擇有預設閘道的網卡。</summary>
    public string? LocalInterfaceId { get; init; }
    /// <summary>上次綁定的 IP。網卡被換掉（ID 改變）時，用它找回同一個網路的網卡。</summary>
    public string? LocalIp { get; init; }
    public int LocalPort { get; init; } = ProtocolConstants.DefaultPort;
    public string? PeerIp { get; init; }
    public int PeerPort { get; init; } = ProtocolConstants.DefaultPort;
    public string? PeerFingerprint { get; init; }
    public string? PeerHostname { get; init; }
    public string ReceiveFolder { get; init; } = DefaultReceiveFolder;
    public bool Encryption { get; init; } = true;
    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.Rename;
    public bool AskBeforeReceive { get; init; }
    public bool StartWithWindows { get; init; }
    /// <summary>介面語言：auto（依系統顯示語言）、en、zh-Hant、zh-Hans。</summary>
    public string Language { get; init; } = "auto";
    /// <summary>是否已經提示過「按 X 只是隱藏到系統匣」。</summary>
    public bool CloseHintShown { get; init; }
    /// <summary>首次執行已建立桌面捷徑；之後被刪除也不自動重建。</summary>
    public bool DesktopShortcutCreated { get; init; }

    public static string DefaultReceiveFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Foldspace");

    [JsonIgnore]
    public bool IsPaired => !string.IsNullOrEmpty(PeerFingerprint);

    [JsonIgnore]
    public IPAddress? PeerAddress =>
        IPAddress.TryParse(PeerIp, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork ? ip : null;

    public static bool IsValidPort(int port) => port is >= ProtocolConstants.MinPort and <= ProtocolConstants.MaxPort;

    /// <summary>嚴格的 IPv4 點分格式（<c>IPAddress.TryParse</c> 會接受 "1" 這類簡寫）。</summary>
    public static bool IsValidIPv4(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var parts = text.Split('.');
        return parts.Length == 4 && parts.All(p =>
            p.Length is >= 1 and <= 3 && p.All(char.IsAsciiDigit) && int.Parse(p) <= 255 && (p.Length == 1 || p[0] != '0'));
    }
}

public sealed class SettingsStore(string path, ILogger log)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Path { get; } = path;

    /// <summary>讀取失敗時備份成 settings.bad.json 並使用預設值。</summary>
    public AppSettings Load()
    {
        if (!File.Exists(Path))
            return new AppSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(Path), JsonOptions)
                ?? throw new JsonException("settings is null");
            return Normalize(settings);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Settings file could not be read; backed up and using defaults");
            try
            {
                File.Copy(Path, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "settings.bad.json"), overwrite: true);
            }
            catch (Exception copyEx)
            {
                log.Warning(copyEx, "Could not back up the corrupt settings file");
            }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings) =>
        AtomicFile.WriteAllBytes(Path, JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions));

    private static AppSettings Normalize(AppSettings s) => s with
    {
        LocalPort = AppSettings.IsValidPort(s.LocalPort) ? s.LocalPort : ProtocolConstants.DefaultPort,
        PeerPort = AppSettings.IsValidPort(s.PeerPort) ? s.PeerPort : ProtocolConstants.DefaultPort,
        ReceiveFolder = string.IsNullOrWhiteSpace(s.ReceiveFolder) ? AppSettings.DefaultReceiveFolder : s.ReceiveFolder,
    };
}
