using System.Reflection;

namespace Foldspace.Core.Protocol;

public static class ProtocolConstants
{
    /// <summary>
    /// 協定版本，握手時交換；主版號不同即視為不相容，次版號只增加相容的功能。
    /// </summary>
    public const string ProtocolVersion = "1.0";

    public const int DefaultPort = 52500;
    /// <summary>同網段搜尋用的 UDP port（固定，跟 TCP 監聽 port 無關，搜尋時才知道要往哪裡送）。</summary>
    public const int DiscoveryPort = 52500;
    public const int MaxDiscoveryDatagramBytes = 4096;
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    public const byte ControlChannel = 0x01;
    public const byte DataChannelTls = 0x02;
    public const byte DataChannelPlain = 0x03;
    /// <summary>網路速度測試：先送一次性 token，加密時接著做 TLS，之後的資料接收端直接丟掉。</summary>
    public const byte SpeedTestChannel = 0x04;

    public const int MaxControlMessageBytes = 1024 * 1024;
    public const int TransferBufferSize = 1024 * 1024;
    public const int DataTokenBytes = 32;
    public const long FreeSpaceMarginBytes = 100L * 1024 * 1024;

    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan TlsTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    /// <summary>同網段搜尋等待回應的時間。</summary>
    public static readonly TimeSpan DiscoveryDuration = TimeSpan.FromSeconds(2);
    /// <summary>已配對但連不上對方時，多久重新搜尋一次（對方的 IP 可能變了）。</summary>
    public static readonly TimeSpan RediscoverInterval = TimeSpan.FromSeconds(30);
    /// <summary>本機拒絕或逾時後，同一台電腦多久內不能再發起配對（避免一直跳出配對視窗）。</summary>
    public static readonly TimeSpan PairingCooldown = TimeSpan.FromSeconds(30);
    public const int MissedHeartbeatLimit = 3;
    /// <summary>入站控制通道多久沒收到任何訊息就關閉（對方每 5 秒會送 PING）。</summary>
    public static readonly TimeSpan IncomingIdleTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan PairingTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan AskBeforeReceiveTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DataTokenLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan TransferResultTimeout = TimeSpan.FromMinutes(10);
    /// <summary>接收中這麼久完全沒有資料就視為連線中斷（資料連線可能是半開的）。</summary>
    public static readonly TimeSpan DataStallTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan LocalAddressRecheckInterval = TimeSpan.FromSeconds(10);

    /// <summary>離線後的重連間隔，最後一個值之後一直沿用。</summary>
    public static readonly TimeSpan[] ReconnectBackoff =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
        TimeSpan.FromSeconds(30),
    ];

    /// <summary>產品版本（例如 1.0.0）：顯示給使用者、握手時交換。</summary>
    public static string AppVersion { get; } =
        typeof(ProtocolConstants).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "0.0.0";

    /// <summary>完整的建置版本（產品版本+建置編號，例如 1.0.0+202610041530）：寫進日誌，分辨是哪一次建置。</summary>
    public static string BuildVersion { get; } =
        typeof(ProtocolConstants).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? AppVersion;

    public static int MajorVersion(string protocolVersion) =>
        int.TryParse(protocolVersion.Split('.')[0], out var major) ? major : -1;
}
