using System.Net;
using Foldspace.Core.Net;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Tests;

/// <summary>同網段搜尋與從搜尋結果配對。測試時搜尋改用本機的單播位址，不往外廣播。</summary>
public class DiscoveryTests
{
    [Fact]
    public async Task Discovery_finds_other_computers_but_not_itself()
    {
        int portA = TestUtil.FreePort(), portB = TestUtil.FreePort();
        await using var a = TestPeer.Create("PEER-A", portA, portB, peerConfigured: false,
            discoveryTargets: TestPeer.DiscoverPorts(portA, portB));
        await using var b = TestPeer.Create("PEER-B", portB, portA, peerConfigured: false);
        await a.Service.StartAsync();
        await b.Service.StartAsync();

        var found = await DiscoverUntilFoundAsync(a);

        var peer = Assert.Single(found);
        Assert.Equal("PEER-B", peer.Hostname);
        Assert.Equal(IPAddress.Loopback, peer.Address);
        Assert.Equal(portB, peer.Port);
        Assert.Equal(b.Identity.Fingerprint, peer.Fingerprint);
        Assert.False(peer.Paired);
        Assert.True(peer.CanPair);
    }

    [Fact]
    public async Task Pairing_from_discovery_works_when_the_other_side_has_no_peer_configured()
    {
        int portA = TestUtil.FreePort(), portB = TestUtil.FreePort();
        await using var a = TestPeer.Create("PEER-A", portA, portB, peerConfigured: false,
            discoveryTargets: TestPeer.DiscoverPorts(portB));
        await using var b = TestPeer.Create("PEER-B", portB, portA, peerConfigured: false);
        a.Service.PairingPromptRequested += p => p.Confirm();
        b.Service.PairingPromptRequested += p => p.Confirm();
        IPEndPoint? learned = null;
        b.Service.PeerEndpointChanged += e => learned = e;
        await a.Service.StartAsync();
        await b.Service.StartAsync();
        Assert.Equal(PeerState.NotConfigured, b.Service.Status.State);

        // 使用者在搜尋結果裡選了 B
        var peer = Assert.Single(await DiscoverUntilFoundAsync(a));
        a.Service.SetPeerEndpoint(peer.Address, peer.Port);
        await TestUtil.WaitUntil(() => a.Service.Status.State == PeerState.Unpaired, because: "A 連上 B（尚未配對）");

        var outcome = await a.Service.PairAsync();
        Assert.True(outcome.Success, outcome.Result.ToString());

        // B 從配對的連線學到 A 的位址，連回 A
        await TestUtil.WaitUntil(() => a.Service.Status.State == PeerState.Connected && b.Service.Status.State == PeerState.Connected,
            because: $"雙方都已連線 (A={a.Service.Status}, B={b.Service.Status})");
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, portA), b.Service.PeerEndpoint);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, portA), learned);
    }

    [Fact]
    public async Task A_rejected_computer_cannot_reopen_the_pairing_dialog_right_away()
    {
        int portA = TestUtil.FreePort(), portB = TestUtil.FreePort();
        await using var a = TestPeer.Create("PEER-A", portA, portB);
        await using var b = TestPeer.Create("PEER-B", portB, portA, peerConfigured: false);
        var prompts = 0;
        a.Service.PairingPromptRequested += p => p.Confirm();
        b.Service.PairingPromptRequested += p =>
        {
            Interlocked.Increment(ref prompts);
            p.Reject();
        };
        await a.Service.StartAsync();
        await b.Service.StartAsync();
        await TestUtil.WaitUntil(() => a.Service.Status.State == PeerState.Unpaired, because: "A 連上 B");

        var first = await a.Service.PairAsync();
        Assert.Equal(PairingResult.RejectedByPeer, first.Result);

        var second = await a.Service.PairAsync();
        Assert.Equal(PairingResult.PeerCannotPair, second.Result);
        Assert.Equal(PairRejectReason.Busy, second.PeerReason);
        Assert.Equal(1, prompts);
    }

    [Fact]
    public async Task A_paired_computer_that_moved_is_found_again()
    {
        int portA = TestUtil.FreePort(), portB = TestUtil.FreePort(), newPortB = TestUtil.FreePort();
        await using var a = TestPeer.Create("PEER-A", portA, portB,
            discoveryTargets: TestPeer.DiscoverPorts(portB, newPortB), rediscoverInterval: TimeSpan.FromMilliseconds(300));
        await using var b = TestPeer.Create("PEER-B", portB, portA);
        a.Service.PairingPromptRequested += p => p.Confirm();
        b.Service.PairingPromptRequested += p => p.Confirm();
        await b.Service.StartAsync();
        await a.Service.StartAsync();
        await TestUtil.WaitUntil(() => a.Service.Status.State == PeerState.Unpaired, because: "A 連上 B");
        Assert.True((await a.Service.PairAsync()).Success);
        await TestUtil.WaitUntil(() => a.Service.Status.State == PeerState.Connected, because: "配對完成");

        // B「換了 IP」：同一個身分與配對，改在新的 port 執行。
        await b.Service.StopAsync();
        await using var movedB = TestPeer.Create("PEER-B", newPortB, portA, identity: b.Identity, pairing: b.Pairing);
        await movedB.Service.StartAsync();

        await TestUtil.WaitUntil(() => a.Service.Status.State == PeerState.Connected && a.Service.PeerEndpoint?.Port == newPortB,
            TimeSpan.FromSeconds(15), $"A 重新找到 B 的新位址 (A={a.Service.Status}, {a.Service.PeerEndpoint})");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Speed_test_measures_throughput_without_touching_disk(bool encrypted)
    {
        await using var peers = await PeerPair.StartAsync();

        var result = await peers.A.Service.RunSpeedTestAsync(encrypted, TimeSpan.FromMilliseconds(500));

        Assert.Equal(encrypted, result.Encrypted);
        Assert.True(result.Bytes > 0);
        Assert.True(result.Mbps > 0);
        // 測速資料不會落地：接收資料夾沒有任何檔案。
        Assert.False(Directory.Exists(peers.B.ReceiveFolder) && Directory.EnumerateFileSystemEntries(peers.B.ReceiveFolder).Any());
    }

    [Fact]
    public async Task Parallel_speed_test_adds_up_all_connections()
    {
        await using var peers = await PeerPair.StartAsync();
        var result = await peers.A.Service.RunParallelSpeedTestAsync(4, encrypted: false, TimeSpan.FromMilliseconds(500));
        Assert.True(result.Bytes > 0);
        Assert.True(result.Mbps > 0);
    }

    [Fact]
    public async Task Speed_test_requires_a_paired_connection()
    {
        await using var peers = await PeerPair.StartAsync(pair: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => peers.A.Service.RunSpeedTestAsync(false, TimeSpan.FromMilliseconds(200)));
    }

    /// <summary>回應端的 UDP socket 是在背景開的，剛啟動時可能還沒準備好。</summary>
    private static async Task<IReadOnlyList<DiscoveredPeer>> DiscoverUntilFoundAsync(TestPeer peer)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var found = await peer.Service.DiscoverAsync(TimeSpan.FromMilliseconds(800));
            if (found.Count > 0)
                return found;
        }
        return [];
    }
}
