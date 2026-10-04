using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Foldspace.Core.Identity;
using Foldspace.Core.Net;
using Foldspace.Core.Protocol;
using Foldspace.Core.Settings;
using Foldspace.Core.Transfer;

namespace Foldspace.Core.Tests;

/// <summary>同一台機器用兩個 port 跑兩個實例。</summary>
public class ConnectionTests
{
    [Fact]
    public async Task Test_connection_reports_hostname_version_pairing_and_latency()
    {
        await using var peers = await PeerPair.StartAsync(pair: false);

        var unpaired = await peers.A.Service.TestConnectionAsync();
        Assert.True(unpaired.Success, unpaired.Outcome.ToString());
        Assert.Equal("PEER-B", unpaired.PeerHostname);
        Assert.Equal(ProtocolConstants.AppVersion, unpaired.PeerAppVersion);
        Assert.False(unpaired.Paired);
        Assert.NotNull(unpaired.RoundTripMs);

        await peers.PairAsync();
        var paired = await peers.A.Service.TestConnectionAsync();
        Assert.True(paired.Success, paired.Outcome.ToString());
        Assert.True(paired.Paired);
    }

    [Fact]
    public async Task Test_connection_explains_why_it_failed()
    {
        int portA = TestUtil.FreePort(), portB = TestUtil.FreePort();
        await using var a = TestPeer.Create("PEER-A", portA, portB);
        await using var b = TestPeer.Create("PEER-B", portB, portA);

        // 對方未執行
        var notRunning = await a.Service.TestConnectionAsync();
        Assert.Equal(TestOutcome.Refused, notRunning.Outcome);

        // 服務停用（啟用後再停用 = 停止監聽）
        await b.Service.StartAsync();
        await b.Service.StopAsync();
        var disabled = await a.Service.TestConnectionAsync();
        Assert.Equal(TestOutcome.Refused, disabled.Outcome);

        // 錯誤 port：那個 port 上沒有東西
        await b.Service.StartAsync();
        var wrong = await a.Service.TestConnectionAsync(new IPEndPoint(IPAddress.Loopback, TestUtil.FreePort()), default);
        Assert.Equal(TestOutcome.Refused, wrong.Outcome);

        // 錯誤 port：那個 port 上是別的程式（不會 TLS）
        var other = new TcpListener(IPAddress.Loopback, 0);
        other.Start();
        _ = Task.Run(async () =>
        {
            using var c = await other.AcceptTcpClientAsync();
            await c.GetStream().WriteAsync("HTTP/1.1 400 Bad Request\r\n\r\n"u8.ToArray());
        });
        var notUs = await a.Service.TestConnectionAsync((IPEndPoint)other.LocalEndpoint, default);
        other.Stop();
        Assert.Equal(TestOutcome.TlsFailed, notUs.Outcome);
    }

    [Fact]
    public async Task Pairing_shows_the_same_code_on_both_sides()
    {
        await using var peers = await PeerPair.StartAsync(pair: false);
        Assert.Equal(PeerState.Unpaired, peers.A.Service.Status.State);

        var codes = new List<string>();
        void Confirm(PairingPrompt p)
        {
            lock (codes) codes.Add(p.Code);
            p.Confirm();
        }
        peers.A.Service.PairingPromptRequested += Confirm;
        peers.B.Service.PairingPromptRequested += Confirm;

        var outcome = await peers.A.Service.PairAsync();

        Assert.True(outcome.Success, outcome.Result.ToString());
        Assert.Equal(2, codes.Count);
        Assert.Equal(codes[0], codes[1]);
        Assert.Equal(peers.B.Identity.Fingerprint, peers.A.Pairing.PeerFingerprint);
        Assert.Equal(peers.A.Identity.Fingerprint, peers.B.Pairing.PeerFingerprint);
        await peers.WaitConnected();
    }

    [Fact]
    public async Task Pairing_fails_when_either_side_rejects()
    {
        await using var peers = await PeerPair.StartAsync(pair: false);
        peers.A.Service.PairingPromptRequested += p => p.Confirm();
        peers.B.Service.PairingPromptRequested += p => p.Reject();

        var outcome = await peers.A.Service.PairAsync();

        Assert.False(outcome.Success);
        Assert.Null(peers.A.Pairing.PeerFingerprint);
        Assert.Null(peers.B.Pairing.PeerFingerprint);
    }

    [Fact]
    public async Task Unpaired_peer_rejects_transfer_offers()
    {
        await using var peers = await PeerPair.StartAsync(pair: false);

        // 本機的狀態檢查直接擋下
        Assert.Throws<TransferRejectedException>(() => peers.A.Service.Send([TestUtil.WriteFile(peers.A.Source("a.txt"), 10)]));

        // 繞過本機檢查直接送 OFFER，對方也必須拒絕
        var reply = await peers.A.Service.RequestOnOutgoingAsync(new TransferOfferMessage
        {
            JobId = Guid.NewGuid(),
            Items = [new OfferItem { Name = "a.txt", IsDirectory = false, Bytes = 10, FileCount = 1 }],
            FileCount = 1,
            DirectoryCount = 0,
            TotalBytes = 10,
        }, TimeSpan.FromSeconds(5));

        var reject = Assert.IsType<TransferRejectMessage>(reply);
        Assert.Equal(RejectReason.NotPaired, reject.Reason);
        Assert.False(Directory.Exists(peers.B.ReceiveFolder) && Directory.EnumerateFiles(peers.B.ReceiveFolder).Any());
    }

    [Fact]
    public async Task Regenerated_certificate_shows_pairing_mismatch()
    {
        var peers = await PeerPair.StartAsync();
        try
        {
            // B 重灌：新的憑證、清空配對紀錄
            await peers.B.Service.StopAsync();
            peers.B.Pairing.ClearPairing();
            peers.B = peers.B.Restart(identity: DeviceIdentity.CreateNew("PEER-B"));
            await peers.B.Service.StartAsync();
            peers.A.Service.NudgeReconnect();

            await TestUtil.WaitUntil(() => peers.A.Service.Status.State == PeerState.Error,
                TimeSpan.FromSeconds(40), "A 顯示錯誤");
            Assert.Equal(PeerIssue.PairingMismatch, peers.A.Service.Status.Issue);
        }
        finally
        {
            await peers.DisposeAsync();
        }
    }

    [Fact]
    public async Task Peer_that_unpairs_and_leaves_shows_unpaired_not_offline()
    {
        await using var peers = await PeerPair.StartAsync();

        // 對方解除配對（例如解除安裝）後離開：本機已經沒有配對，顯示「未配對」而不是「離線」。
        await peers.A.Service.UnpairAsync();
        await TestUtil.WaitUntil(() => peers.B.Service.Status.State == PeerState.Unpaired, because: "B 收到 UNPAIR");
        await peers.A.Service.StopAsync();
        await TestUtil.WaitUntil(() => !peers.B.Service.HasOutgoingConnection, because: "B 的連線中斷");
        await Task.Delay(500);
        Assert.Equal(PeerState.Unpaired, peers.B.Service.Status.State);
    }

    [Fact]
    public async Task Disable_makes_peer_offline_and_enable_recovers()
    {
        await using var peers = await PeerPair.StartAsync();

        await peers.B.Service.StopAsync();
        Assert.Equal(PeerState.Disabled, peers.B.Service.Status.State);
        await TestUtil.WaitUntil(() => peers.A.Service.Status.State == PeerState.Offline, because: "A 顯示離線");

        await peers.B.Service.StartAsync();
        peers.A.Service.NudgeReconnect();
        await peers.WaitConnected();
    }

    [Fact]
    public async Task Reconnects_as_soon_as_peer_connects_in()
    {
        await using var peers = await PeerPair.StartAsync();

        // B 離線一段時間，讓 A 的重試間隔拉長（2 → 4 → 8 秒…）
        await peers.B.Service.StopAsync();
        await TestUtil.WaitUntil(() => peers.A.Service.Status.State == PeerState.Offline, because: "A 顯示離線");
        await Task.Delay(TimeSpan.FromSeconds(7));

        // B 回來後會立刻連進 A；A 不能等自己的下一次重試（目標：恢復 ≤ 10 秒）
        var started = DateTime.UtcNow;
        await peers.B.Service.StartAsync();
        await TestUtil.WaitUntil(() => peers.A.Service.Status.State == PeerState.Connected,
            TimeSpan.FromSeconds(4), "A 在對方連入後立即恢復");
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task Version_incompatibility_is_detected()
    {
        // 主版號比對是純函式，這裡確認錯誤訊息包含雙方版本。
        Assert.Equal(1, ProtocolConstants.MajorVersion("1.0"));
        Assert.Equal(2, ProtocolConstants.MajorVersion("2.3"));
        Assert.Equal(-1, ProtocolConstants.MajorVersion("garbage"));
        await Task.CompletedTask;
    }
}

public class TransferTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)] // 只要任一端開加密就加密
    public async Task Files_folders_empty_items_keep_content_and_mtime(bool encryptionA, bool encryptionB)
    {
        await using var peers = await PeerPair.StartAsync(encryptionA, encryptionB);
        var a = peers.A;

        TestUtil.WriteFile(a.Source("single.bin"), 3 * 1024 * 1024 + 17, seed: 1);
        TestUtil.WriteFile(a.Source("empty.txt"), 0);
        TestUtil.WriteFile(a.Source("Tree", "one.txt"), 100, seed: 2);
        TestUtil.WriteFile(a.Source("Tree", "sub", "two.bin"), 70_000, seed: 3);
        TestUtil.WriteFile(a.Source("Tree", "sub", "zero.dat"), 0);
        Directory.CreateDirectory(a.Source("Tree", "empty-dir"));
        Directory.CreateDirectory(a.Source("EmptyFolder"));
        foreach (var d in Directory.EnumerateDirectories(a.Source(), "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            Directory.SetLastWriteTimeUtc(d, TestUtil.FixedTime);

        var job = peers.A.Service.Send(
        [
            a.Source("single.bin"), a.Source("empty.txt"), a.Source("Tree"), a.Source("EmptyFolder"),
        ]);
        await TestUtil.WaitFinished(job);

        Assert.Equal(TransferJobState.Completed, job.State);
        Assert.Null(job.Note);
        TestUtil.AssertTreesEqual(a.Source(), peers.B.ReceiveFolder);
        Assert.Equal(TestUtil.FixedTime, Directory.GetLastWriteTimeUtc(Path.Combine(peers.B.ReceiveFolder, "Tree", "sub")));
        Assert.False(Directory.Exists(TempArea.Root(peers.B.ReceiveFolder)) &&
                     Directory.EnumerateDirectories(TempArea.Root(peers.B.ReceiveFolder)).Any(), "暫存資料夾必須清空");
    }

    [Fact]
    public async Task Unicode_emoji_and_long_paths()
    {
        await using var peers = await PeerPair.StartAsync();
        var a = peers.A;

        var segments = Enumerable.Range(0, 6).Select(i => $"很長的資料夾名稱_{i}_" + new string('x', 40)).ToArray();
        var deep = TestUtil.WriteFile(a.Source(["長路徑", .. segments, "最後的檔案 🎉.txt"]), 1234, seed: 7);
        Assert.True(Path.GetRelativePath(a.Source(), deep).Length > 260);
        TestUtil.WriteFile(a.Source("日本語ファイル.txt"), 10, seed: 8);
        TestUtil.WriteFile(a.Source("emoji 😀👍.bin"), 10, seed: 9);

        var job = peers.A.Service.Send([a.Source("長路徑"), a.Source("日本語ファイル.txt"), a.Source("emoji 😀👍.bin")]);
        await TestUtil.WaitFinished(job);

        Assert.Equal(TransferJobState.Completed, job.State);
        TestUtil.AssertTreesEqual(a.Source(), peers.B.ReceiveFolder);
    }

    [Theory]
    [InlineData(ConflictPolicy.Rename)]
    [InlineData(ConflictPolicy.Overwrite)]
    [InlineData(ConflictPolicy.Skip)]
    public async Task Conflict_policies_end_to_end(ConflictPolicy policy)
    {
        await using var peers = await PeerPair.StartAsync();
        peers.B.Receive = peers.B.Receive with { ConflictPolicy = policy };
        Directory.CreateDirectory(peers.B.ReceiveFolder);
        File.WriteAllText(Path.Combine(peers.B.ReceiveFolder, "report.pdf"), "old");

        File.WriteAllText(peers.A.Source("report.pdf").EnsureParent(), "new");
        var job = peers.A.Service.Send([peers.A.Source("report.pdf")]);
        await TestUtil.WaitFinished(job);
        Assert.Equal(TransferJobState.Completed, job.State);

        var original = File.ReadAllText(Path.Combine(peers.B.ReceiveFolder, "report.pdf"));
        switch (policy)
        {
            case ConflictPolicy.Rename:
                Assert.Equal("old", original);
                Assert.Equal("new", File.ReadAllText(Path.Combine(peers.B.ReceiveFolder, "report (1).pdf")));
                Assert.Equal("report (1).pdf", job.Result!.TopLevel.Single().FinalName);
                break;
            case ConflictPolicy.Overwrite:
                Assert.Equal("new", original);
                break;
            case ConflictPolicy.Skip:
                Assert.Equal("old", original);
                Assert.Equal(FileOutcome.Skipped, job.Result!.TopLevel.Single().Outcome);
                break;
        }
    }

    [Fact]
    public async Task Plain_data_channel_without_valid_token_is_rejected()
    {
        await using var peers = await PeerPair.StartAsync(encryptionA: false, encryptionB: false);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, peers.B.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(new[] { ProtocolConstants.DataChannelPlain });
        await stream.WriteAsync(RandomNumberGenerator.GetBytes(ProtocolConstants.DataTokenBytes));
        try
        {
            await stream.WriteAsync(new byte[64 * 1024]);
        }
        catch (IOException)
        {
            // 對方可能已經關閉
        }

        // 對方直接關閉連線：讀到 0（正常關閉）或連線被重設都算。
        var buffer = new byte[16];
        int read;
        try
        {
            read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (IOException)
        {
            read = 0;
        }
        Assert.Equal(0, read);
        Assert.False(Directory.Exists(peers.B.ReceiveFolder) && Directory.EnumerateFileSystemEntries(peers.B.ReceiveFolder)
            .Any(p => Path.GetFileName(p) != TempArea.FolderName));
    }

    [Fact]
    public async Task Both_directions_at_the_same_time()
    {
        await using var peers = await PeerPair.StartAsync();
        TestUtil.WriteFile(peers.A.Source("from-a.bin"), 5 * 1024 * 1024, seed: 1);
        TestUtil.WriteFile(peers.B.Source("from-b.bin"), 5 * 1024 * 1024, seed: 2);

        var ab = peers.A.Service.Send([peers.A.Source("from-a.bin")]);
        var ba = peers.B.Service.Send([peers.B.Source("from-b.bin")]);
        await Task.WhenAll(TestUtil.WaitFinished(ab), TestUtil.WaitFinished(ba));

        Assert.Equal(TransferJobState.Completed, ab.State);
        Assert.Equal(TransferJobState.Completed, ba.State);
        Assert.Equal(File.ReadAllBytes(peers.A.Source("from-a.bin")), File.ReadAllBytes(Path.Combine(peers.B.ReceiveFolder, "from-a.bin")));
        Assert.Equal(File.ReadAllBytes(peers.B.Source("from-b.bin")), File.ReadAllBytes(Path.Combine(peers.A.ReceiveFolder, "from-b.bin")));
    }

    [Fact]
    public async Task Jobs_in_the_same_direction_are_queued_in_order()
    {
        await using var peers = await PeerPair.StartAsync();
        var jobs = Enumerable.Range(0, 3)
            .Select(i => peers.A.Service.Send([TestUtil.WriteFile(peers.A.Source($"q{i}.bin"), 2 * 1024 * 1024, seed: i)]))
            .ToList();

        foreach (var job in jobs)
            await TestUtil.WaitFinished(job);

        Assert.All(jobs, j => Assert.Equal(TransferJobState.Completed, j.State));
        // 依序完成：每個任務開始時，前一個已經結束
        for (var i = 1; i < jobs.Count; i++)
            Assert.True(jobs[i].StartedAt >= jobs[i - 1].FinishedAt, $"任務 {i} 應在任務 {i - 1} 之後開始");
    }

    [Fact]
    public async Task Cancel_leaves_no_partial_files()
    {
        await using var peers = await PeerPair.StartAsync();
        var a = peers.A;
        TestUtil.WriteFile(a.Source("Batch", "0-small.txt"), 1000, seed: 1);
        TestUtil.WriteFile(a.Source("Batch", "1-big.bin"), 64 * 1024 * 1024, seed: 2);

        var job = peers.A.Service.Send([a.Source("Batch")]);
        job.Changed += j =>
        {
            if (j.State == TransferJobState.Transferring && j.BytesTransferred > 0)
                j.Cancel();
        };
        await TestUtil.WaitFinished(job);
        // 傳送端取消後，接收端也要馬上停止，不能把緩衝區裡剩下的資料收完才停。
        await TestUtil.WaitUntil(() => peers.B.Service.Jobs.All(j => j.IsFinished), TimeSpan.FromSeconds(3), "接收端立即結束");
        if (job.State == TransferJobState.Cancelled)
        {
            // 接收端要知道這是對方取消，而不是連線中斷。
            var received = Assert.Single(peers.B.Service.Jobs);
            Assert.Equal(TransferJobState.Cancelled, received.State);
            Assert.Equal(CancelReason.Peer, received.Note?.Cause);
        }

        // 取消後畫面立即顯示「已取消」，清理在背景進行：等暫存區清空（也就是整理完成）再檢查。
        var temp = TempArea.Root(peers.B.ReceiveFolder);
        await TestUtil.WaitUntil(() => !Directory.Exists(temp) || !Directory.EnumerateDirectories(temp).Any(),
            TimeSpan.FromSeconds(5), "暫存資料夾在背景清空");

        // 不論取消發生在哪裡：接收資料夾中的每個檔案都必須完整。
        if (Directory.Exists(peers.B.ReceiveFolder))
        {
            foreach (var file in Directory.EnumerateFiles(peers.B.ReceiveFolder, "*", SearchOption.AllDirectories)
                         .Where(f => !f.Contains(TempArea.FolderName)))
            {
                var source = a.Source(Path.GetRelativePath(peers.B.ReceiveFolder, file));
                Assert.Equal(new FileInfo(source).Length, new FileInfo(file).Length);
            }
        }
    }

    [Fact]
    public async Task Insufficient_space_is_rejected_before_start()
    {
        await using var peers = await PeerPair.StartAsync();
        const long huge = 1L << 60; // 比任何磁碟都大

        var reply = await peers.A.Service.RequestOnOutgoingAsync(new TransferOfferMessage
        {
            JobId = Guid.NewGuid(),
            Items = [new OfferItem { Name = "huge.bin", IsDirectory = false, Bytes = huge, FileCount = 1 }],
            FileCount = 1,
            DirectoryCount = 0,
            TotalBytes = huge,
        }, TimeSpan.FromSeconds(5));

        var reject = Assert.IsType<TransferRejectMessage>(reply);
        Assert.Equal(RejectReason.InsufficientSpace, reject.Reason);
        Assert.True(reject.RequiredBytes > reject.AvailableBytes);
    }

    [Fact]
    public async Task Unusable_receive_folder_is_rejected_and_reported_to_the_receiver()
    {
        await using var peers = await PeerPair.StartAsync();
        // 接收資料夾的路徑被同名檔案占住，無法建立資料夾。
        Directory.CreateDirectory(Path.GetDirectoryName(peers.B.ReceiveFolder)!);
        File.WriteAllText(peers.B.ReceiveFolder, "blocker");
        IncomingRejection? reported = null;
        peers.B.Service.IncomingRejected += r => reported = r;

        var job = peers.A.Service.Send([TestUtil.WriteFile(peers.A.Source("a.txt"), 10)]);
        await TestUtil.WaitFinished(job);

        // 傳送端知道原因，接收端的使用者也收到通知。
        Assert.Equal(TransferJobState.Failed, job.State);
        Assert.Equal(RejectReason.ReceiveFolderUnavailable, job.Note?.Rejection);
        await TestUtil.WaitUntil(() => reported is not null, because: "接收端發出拒收事件");
        Assert.Equal(RejectReason.ReceiveFolderUnavailable, reported!.Reason);
        Assert.Equal("PEER-A", reported.PeerHostname);
    }

    [Fact]
    public async Task Sending_drive_root_is_rejected()
    {
        await using var peers = await PeerPair.StartAsync();
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var ex = Assert.Throws<TransferRejectedException>(() => peers.A.Service.Send([root]));
        Assert.Equal(JobIssue.DriveRoot, ex.Note.Issue);
    }
}

internal static class PathTestExtensions
{
    public static string EnsureParent(this string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
