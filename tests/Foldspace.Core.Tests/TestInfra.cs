using System.Net;
using System.Net.Sockets;
using Serilog;
using Foldspace.Core.Identity;
using Foldspace.Core.Net;
using Foldspace.Core.Settings;
using Foldspace.Core.Transfer;

namespace Foldspace.Core.Tests;

/// <summary>測試用的暫存資料夾，結束時刪除。</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "foldspace-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(params string[] parts) => System.IO.Path.Combine(parts.Prepend(Path).ToArray());

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* 測試清理失敗不影響結果 */ }
    }
}

internal static class TestUtil
{
    public static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    /// <summary>修改時間設成整數秒，方便精確比對。</summary>
    public static readonly DateTime FixedTime = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static string WriteFile(string path, int size, int seed = 1, DateTime? mtime = null)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        File.WriteAllBytes(path, data);
        File.SetLastWriteTimeUtc(path, mtime ?? FixedTime);
        return path;
    }

    public static async Task WaitUntil(Func<bool> condition, TimeSpan? timeout = null, string? because = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("等待逾時：" + because);
            await Task.Delay(50);
        }
    }

    public static async Task<TransferJob> WaitFinished(TransferJob job, TimeSpan? timeout = null)
    {
        await WaitUntil(() => job.IsFinished, timeout ?? TimeSpan.FromSeconds(60), $"job {job.ItemName} finished (now {job.State})");
        return job;
    }

    /// <summary>比對兩個資料夾樹：相同的相對路徑、內容與修改時間。</summary>
    public static void AssertTreesEqual(string expected, string actual)
    {
        var expectedFiles = RelativeEntries(expected);
        var actualFiles = RelativeEntries(actual);
        Assert.Equal(expectedFiles, actualFiles);

        foreach (var relative in expectedFiles.Where(r => !r.EndsWith('/')))
        {
            var a = System.IO.Path.Combine(expected, relative);
            var b = System.IO.Path.Combine(actual, relative);
            Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
            Assert.Equal(File.GetLastWriteTimeUtc(a), File.GetLastWriteTimeUtc(b));
        }
    }

    public static List<string> RelativeEntries(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(p => System.IO.Path.GetRelativePath(root, p).Replace('\\', '/') + (Directory.Exists(p) ? "/" : ""))
            .Where(p => !p.StartsWith(TempArea.FolderName, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>一對已連線的 loopback socket（用來直接測資料通道的格式）。</summary>
    public static async Task<(TcpClient A, TcpClient B)> SocketPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var a = new TcpClient();
        var connect = a.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var b = await listener.AcceptTcpClientAsync();
        await connect;
        listener.Stop();
        return (a, b);
    }
}

/// <summary>同一台機器上用不同 port 模擬一台電腦。</summary>
internal sealed class TestPeer : IAsyncDisposable
{
    private readonly TempDir _dir = new();

    private TestPeer(string hostname, int port, int peerPort, bool encryption, DeviceIdentity identity, InMemoryPairingStore pairing,
        bool peerConfigured = true, Func<IReadOnlyList<IPEndPoint>>? discoveryTargets = null, TimeSpan? rediscoverInterval = null)
    {
        Hostname = hostname;
        Port = port;
        PeerPort = peerPort;
        Identity = identity;
        Pairing = pairing;
        ReceiveFolder = _dir.Sub("receive");
        Receive = new ReceiveOptions(ReceiveFolder, ConflictPolicy.Rename, AskBeforeReceive: false);
        Service = new PeerService(new PeerServiceOptions
        {
            Identity = identity,
            Hostname = hostname,
            ResolveLocalAddress = () => IPAddress.Loopback,
            LocalPort = port,
            PeerAddress = peerConfigured ? IPAddress.Loopback : null,
            PeerPort = peerPort,
            Encryption = encryption,
            Logger = TestUtil.Log,
            // 搜尋：UDP 用和 TCP 相同的 port 號碼；預設不往外廣播、也不自動重新搜尋。
            DiscoveryPort = port,
            DiscoveryTargets = discoveryTargets ?? (() => []),
            RediscoverInterval = rediscoverInterval ?? TimeSpan.FromHours(1),
        }, pairing, () => Receive);
    }

    public string Hostname { get; }
    public int Port { get; }
    public int PeerPort { get; }
    public DeviceIdentity Identity { get; }
    public InMemoryPairingStore Pairing { get; }
    public PeerService Service { get; }
    public string ReceiveFolder { get; }
    public ReceiveOptions Receive { get; set; }
    public string Source(params string[] parts) => _dir.Sub(["source", .. parts]);

    public static TestPeer Create(string hostname, int port, int peerPort, bool encryption = true,
        DeviceIdentity? identity = null, InMemoryPairingStore? pairing = null, bool peerConfigured = true,
        Func<IReadOnlyList<IPEndPoint>>? discoveryTargets = null, TimeSpan? rediscoverInterval = null) =>
        new(hostname, port, peerPort, encryption, identity ?? DeviceIdentity.CreateNew(hostname), pairing ?? new InMemoryPairingStore(),
            peerConfigured, discoveryTargets, rediscoverInterval);

    /// <summary>只往這些 port（本機）送搜尋。</summary>
    public static Func<IReadOnlyList<IPEndPoint>> DiscoverPorts(params int[] ports) =>
        () => [.. ports.Select(p => new IPEndPoint(IPAddress.Loopback, p))];

    /// <summary>以新的服務實例重新啟動（模擬重開程式；可換身分模擬重灌）。</summary>
    public TestPeer Restart(DeviceIdentity? identity = null, bool? encryption = null) =>
        new(Hostname, Port, PeerPort, encryption ?? true, identity ?? Identity, Pairing);

    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync();
        _dir.Dispose();
    }
}

/// <summary>兩台已啟動的電腦。</summary>
internal sealed class PeerPair : IAsyncDisposable
{
    public required TestPeer A { get; set; }
    public required TestPeer B { get; set; }

    public static async Task<PeerPair> StartAsync(bool encryptionA = true, bool encryptionB = true, bool pair = true)
    {
        int portA = TestUtil.FreePort(), portB = TestUtil.FreePort();
        var pairPeers = new PeerPair
        {
            A = TestPeer.Create("PEER-A", portA, portB, encryptionA),
            B = TestPeer.Create("PEER-B", portB, portA, encryptionB),
        };
        await pairPeers.B.Service.StartAsync();
        await pairPeers.A.Service.StartAsync();
        await TestUtil.WaitUntil(() => pairPeers.A.Service.HasOutgoingConnection && pairPeers.B.Service.HasOutgoingConnection,
            because: "雙方建立控制通道");

        if (pair)
            await pairPeers.PairAsync();
        return pairPeers;
    }

    public async Task PairAsync()
    {
        void AutoConfirm(PairingPrompt prompt) => prompt.Confirm();
        A.Service.PairingPromptRequested += AutoConfirm;
        B.Service.PairingPromptRequested += AutoConfirm;
        try
        {
            var outcome = await A.Service.PairAsync();
            Assert.True(outcome.Success, outcome.Result.ToString());
            await WaitConnected();
        }
        finally
        {
            A.Service.PairingPromptRequested -= AutoConfirm;
            B.Service.PairingPromptRequested -= AutoConfirm;
        }
    }

    public Task WaitConnected() => TestUtil.WaitUntil(
        () => A.Service.Status.State == PeerState.Connected && B.Service.Status.State == PeerState.Connected,
        because: $"both connected (A={A.Service.Status}, B={B.Service.Status})");

    public async ValueTask DisposeAsync()
    {
        await A.DisposeAsync();
        await B.DisposeAsync();
    }
}
