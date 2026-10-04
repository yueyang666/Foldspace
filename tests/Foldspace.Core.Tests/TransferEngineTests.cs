using System.IO.Hashing;
using Foldspace.Core.Protocol;
using Foldspace.Core.Settings;
using Foldspace.Core.Transfer;

namespace Foldspace.Core.Tests;

/// <summary>直接用 socket 對測資料通道與暫存區，不經過控制通道。</summary>
public class TransferEngineTests
{
    private static TransferOfferMessage Offer(params OfferItem[] items) => new()
    {
        JobId = Guid.NewGuid(),
        Items = items,
        FileCount = items.Sum(i => i.FileCount),
        DirectoryCount = 0,
        TotalBytes = items.Sum(i => i.Bytes),
    };

    private static OfferItem FileItem(string name, long bytes) => new() { Name = name, IsDirectory = false, Bytes = bytes, FileCount = 1 };
    private static OfferItem DirItem(string name) => new() { Name = name, IsDirectory = true, Bytes = 0, FileCount = 0 };

    private static EntryHeader Header(string path, long size, bool dir = false) => new()
    {
        Path = path, IsDirectory = dir, Size = size, LastWriteUtcTicks = TestUtil.FixedTime.Ticks,
    };

    /// <summary>傳送端腳本：自行組出資料流，可以送錯誤的雜湊或惡意路徑。</summary>
    private static async Task<(TransferReceiver Receiver, Exception? Error)> RunReceiver(
        string tempDir, TransferOfferMessage offer, Func<DataStreamWriter, Stream, Task> script)
    {
        var (a, b) = await TestUtil.SocketPair();
        using (a)
        using (b)
        {
            var job = new TransferJob(offer.JobId, TransferDirection.Receive, "test", offer.Items.Count);
            var receiver = new TransferReceiver(job, offer, tempDir, TestUtil.Log);
            var receiveTask = receiver.ReceiveAsync(b.GetStream(), default);
            var senderStream = a.GetStream();
            var scriptTask = script(new DataStreamWriter(senderStream), senderStream);

            Exception? error = null;
            try
            {
                await receiveTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                error = ex;
                a.Close();
            }
            try { await scriptTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch { /* 接收端中斷時腳本可能寫入失敗 */ }
            return (receiver, error);
        }
    }

    private static async Task SendFile(DataStreamWriter w, string path, byte[] data, ulong? hash = null)
    {
        await w.WriteHeaderAsync(Header(path, data.Length), default);
        if (data.Length > 0)
            await w.WriteChunkAsync(data, default);
        await w.WriteFileEndAsync(hash ?? XxHash3.HashToUInt64(data), default);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a.txt/../../evil.txt")]
    [InlineData("/tmp/evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("\\\\server\\share\\evil.txt")]
    public async Task Malicious_path_aborts_the_whole_job_without_writing_outside(string evilPath)
    {
        using var dir = new TempDir();
        var receive = dir.Sub("receive");
        var tempDir = TempArea.CreateJobDir(receive, Guid.NewGuid());
        var offer = Offer(FileItem("a.txt", 3));

        var (_, error) = await RunReceiver(tempDir, offer, async (w, s) =>
        {
            await SendFile(w, "a.txt", "abc"u8.ToArray());
            await SendFile(w, evilPath, "evil"u8.ToArray());
            await w.WriteEndOfPassAsync(default);
        });

        Assert.IsType<SecurityViolationException>(error);
        // 接收資料夾以外（TempDir 根目錄）不能多出任何東西。
        Assert.Equal(new[] { "receive/" }, Directory.EnumerateFileSystemEntries(dir.Path).Select(p => Path.GetFileName(p) + "/"));
        Assert.Empty(Directory.EnumerateFiles(dir.Path, "evil*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Hash_mismatch_is_retransmitted_once_then_succeeds()
    {
        using var dir = new TempDir();
        var tempDir = TempArea.CreateJobDir(dir.Sub("receive"), Guid.NewGuid());
        var data = "hello world"u8.ToArray();
        var offer = Offer(FileItem("a.txt", data.Length));
        List<string>? requested = null;

        var (receiver, error) = await RunReceiver(tempDir, offer, async (w, s) =>
        {
            await SendFile(w, "a.txt", data, hash: 12345);           // 第一輪：錯誤雜湊
            await w.WriteEndOfPassAsync(default);
            requested = (await PassResponseFraming.ReadAsync(s, default)).Retransmit.ToList();
            await SendFile(w, "a.txt", data);                        // 第二輪：正確
            await w.WriteEndOfPassAsync(default);
            await PassResponseFraming.ReadAsync(s, default);
        });

        Assert.Null(error);
        Assert.Equal(new[] { "a.txt" }, requested!);
        Assert.Contains("a.txt", receiver.VerifiedFiles);
        Assert.Empty(receiver.FailedFiles);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(tempDir, "a.txt")));
    }

    [Fact]
    public async Task Hash_mismatch_twice_marks_file_failed_and_removes_it()
    {
        using var dir = new TempDir();
        var tempDir = TempArea.CreateJobDir(dir.Sub("receive"), Guid.NewGuid());
        var data = "hello"u8.ToArray();
        var offer = Offer(FileItem("a.txt", data.Length));

        var (receiver, error) = await RunReceiver(tempDir, offer, async (w, s) =>
        {
            await SendFile(w, "a.txt", data, hash: 1);
            await w.WriteEndOfPassAsync(default);
            await PassResponseFraming.ReadAsync(s, default);
            await SendFile(w, "a.txt", data, hash: 2);
            await w.WriteEndOfPassAsync(default);
            await PassResponseFraming.ReadAsync(s, default);
        });

        Assert.Null(error);
        Assert.Contains("a.txt", receiver.FailedFiles.Keys);
        Assert.False(File.Exists(Path.Combine(tempDir, "a.txt")));
    }

    [Fact]
    public async Task Sender_abort_and_reserved_names_are_reported_without_stopping_the_job()
    {
        using var dir = new TempDir();
        var tempDir = TempArea.CreateJobDir(dir.Sub("receive"), Guid.NewGuid());
        var offer = Offer(DirItem("Docs"));

        var (receiver, error) = await RunReceiver(tempDir, offer, async (w, s) =>
        {
            await w.WriteHeaderAsync(Header("Docs", 0, dir: true), default);
            await w.WriteHeaderAsync(Header("Docs/locked.xlsx", 10), default);
            await w.WriteFileAbortAsync(FileIssue.Locked, default);
            await SendFile(w, "Docs/CON", "x"u8.ToArray());
            await SendFile(w, "Docs/ok.txt", "ok"u8.ToArray());
            await w.WriteEndOfPassAsync(default);
            await PassResponseFraming.ReadAsync(s, default);
        });

        Assert.Null(error);
        Assert.Equal(new[] { "Docs/ok.txt" }, receiver.VerifiedFiles);
        Assert.Equal(FileIssue.Locked, receiver.FailedFiles["Docs/locked.xlsx"]);
        Assert.Equal(FileIssue.InvalidName, receiver.FailedFiles["Docs/CON"]);
    }

    [Fact]
    public async Task Data_beyond_declared_size_is_a_protocol_error()
    {
        using var dir = new TempDir();
        var tempDir = TempArea.CreateJobDir(dir.Sub("receive"), Guid.NewGuid());
        var offer = Offer(FileItem("a.txt", 2));

        var (_, error) = await RunReceiver(tempDir, offer, async (w, s) =>
        {
            await w.WriteHeaderAsync(Header("a.txt", 2), default);
            await w.WriteChunkAsync("too long"u8.ToArray(), default);
        });

        Assert.IsType<ProtocolException>(error);
    }

    [Fact]
    public async Task Interrupted_stream_keeps_only_verified_files()
    {
        using var dir = new TempDir();
        var tempDir = TempArea.CreateJobDir(dir.Sub("receive"), Guid.NewGuid());
        var offer = Offer(FileItem("done.txt", 4), FileItem("partial.bin", 1000));

        var (receiver, error) = await RunReceiver(tempDir, offer, async (w, s) =>
        {
            await SendFile(w, "done.txt", "done"u8.ToArray());
            await w.WriteHeaderAsync(Header("partial.bin", 1000), default);
            await w.WriteChunkAsync(new byte[100], default);
            await s.FlushAsync();
            s.Close(); // 拔線
        });

        Assert.NotNull(error);
        Assert.Equal(new[] { "done.txt" }, receiver.VerifiedFiles);
        Assert.False(File.Exists(Path.Combine(tempDir, "partial.bin")), "未完成的檔案必須刪除");
    }
}

public class FinalizerTests
{
    [Fact]
    public void Unverified_files_are_removed_before_finalizing()
    {
        using var dir = new TempDir();
        var (temp, receive) = Setup(dir);
        Directory.CreateDirectory(Path.Combine(temp, "Batch"));
        File.WriteAllText(Path.Combine(temp, "Batch", "done.txt"), "ok");
        File.WriteAllText(Path.Combine(temp, "Batch", "partial.bin"), "half");

        var removed = TempArea.RemoveUnverified(temp, new HashSet<string> { "Batch/done.txt" }, TestUtil.Log);
        Run(temp, receive, ConflictPolicy.Rename, DirItem("Batch"));

        Assert.Equal(1, removed);
        Assert.True(File.Exists(Path.Combine(receive, "Batch", "done.txt")));
        Assert.False(File.Exists(Path.Combine(receive, "Batch", "partial.bin")), "未驗證的檔案絕不能進到接收資料夾");
    }

    private static OfferItem FileItem(string name) => new() { Name = name, IsDirectory = false, Bytes = 1, FileCount = 1 };
    private static OfferItem DirItem(string name) => new() { Name = name, IsDirectory = true, Bytes = 1, FileCount = 1 };

    private static (string Temp, string Receive) Setup(TempDir dir)
    {
        var receive = Directory.CreateDirectory(dir.Sub("receive")).FullName;
        var temp = Directory.CreateDirectory(dir.Sub("temp")).FullName;
        return (temp, receive);
    }

    private static FinalizeResult Run(string temp, string receive, ConflictPolicy policy, params OfferItem[] items) =>
        TransferFinalizer.Finalize(temp, receive, items, policy, new Dictionary<string, DateTime>(), TestUtil.Log);

    [Fact]
    public void Rename_policy_renames_files_and_whole_folders()
    {
        using var dir = new TempDir();
        var (temp, receive) = Setup(dir);
        File.WriteAllText(Path.Combine(receive, "report.pdf"), "old");
        Directory.CreateDirectory(Path.Combine(receive, "Photos"));
        File.WriteAllText(Path.Combine(receive, "Photos", "a.jpg"), "old");

        File.WriteAllText(Path.Combine(temp, "report.pdf"), "new");
        Directory.CreateDirectory(Path.Combine(temp, "Photos"));
        File.WriteAllText(Path.Combine(temp, "Photos", "a.jpg"), "new");
        File.WriteAllText(Path.Combine(temp, "Photos", "b.jpg"), "new");

        var result = Run(temp, receive, ConflictPolicy.Rename, FileItem("report.pdf"), DirItem("Photos"));

        Assert.Equal("old", File.ReadAllText(Path.Combine(receive, "report.pdf")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(receive, "report (1).pdf")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(receive, "Photos", "a.jpg")));
        Assert.False(File.Exists(Path.Combine(receive, "Photos", "b.jpg")), "改名策略不能合併資料夾");
        Assert.Equal("new", File.ReadAllText(Path.Combine(receive, "Photos (1)", "b.jpg")));
        Assert.All(result.TopLevel, r => Assert.Equal(FileOutcome.Renamed, r.Outcome));
    }

    [Fact]
    public void Overwrite_policy_replaces_files_and_merges_folders()
    {
        using var dir = new TempDir();
        var (temp, receive) = Setup(dir);
        File.WriteAllText(Path.Combine(receive, "report.pdf"), "old");
        Directory.CreateDirectory(Path.Combine(receive, "Photos"));
        File.WriteAllText(Path.Combine(receive, "Photos", "a.jpg"), "old");
        File.WriteAllText(Path.Combine(receive, "Photos", "keep.jpg"), "old");

        File.WriteAllText(Path.Combine(temp, "report.pdf"), "new");
        Directory.CreateDirectory(Path.Combine(temp, "Photos"));
        File.WriteAllText(Path.Combine(temp, "Photos", "a.jpg"), "new");
        File.WriteAllText(Path.Combine(temp, "Photos", "b.jpg"), "new");

        var result = Run(temp, receive, ConflictPolicy.Overwrite, FileItem("report.pdf"), DirItem("Photos"));

        Assert.Equal("new", File.ReadAllText(Path.Combine(receive, "report.pdf")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(receive, "Photos", "a.jpg")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(receive, "Photos", "b.jpg")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(receive, "Photos", "keep.jpg")));
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Skip_policy_skips_files_and_merges_folders_without_replacing()
    {
        using var dir = new TempDir();
        var (temp, receive) = Setup(dir);
        File.WriteAllText(Path.Combine(receive, "report.pdf"), "old");
        Directory.CreateDirectory(Path.Combine(receive, "Photos"));
        File.WriteAllText(Path.Combine(receive, "Photos", "a.jpg"), "old");

        File.WriteAllText(Path.Combine(temp, "report.pdf"), "new");
        Directory.CreateDirectory(Path.Combine(temp, "Photos"));
        File.WriteAllText(Path.Combine(temp, "Photos", "a.jpg"), "new");
        File.WriteAllText(Path.Combine(temp, "Photos", "b.jpg"), "new");

        var result = Run(temp, receive, ConflictPolicy.Skip, FileItem("report.pdf"), DirItem("Photos"));

        Assert.Equal("old", File.ReadAllText(Path.Combine(receive, "report.pdf")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(receive, "Photos", "a.jpg")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(receive, "Photos", "b.jpg")));
        Assert.Contains(result.Problems, p => p.Path == "report.pdf" && p.Outcome == FileOutcome.Skipped);
        Assert.Contains(result.Problems, p => p.Path == "Photos/a.jpg" && p.Outcome == FileOutcome.Skipped);
    }

    [Fact]
    public void Overwrite_of_read_only_file_fails_only_that_file()
    {
        if (!OperatingSystem.IsWindows())
            return; // 唯讀屬性擋覆蓋是 Windows 的行為

        using var dir = new TempDir();
        var (temp, receive) = Setup(dir);
        Directory.CreateDirectory(Path.Combine(receive, "Docs"));
        var locked = Path.Combine(receive, "Docs", "ro.txt");
        File.WriteAllText(locked, "old");
        File.SetAttributes(locked, FileAttributes.ReadOnly);

        Directory.CreateDirectory(Path.Combine(temp, "Docs"));
        File.WriteAllText(Path.Combine(temp, "Docs", "ro.txt"), "new");
        File.WriteAllText(Path.Combine(temp, "Docs", "ok.txt"), "new");

        try
        {
            var result = Run(temp, receive, ConflictPolicy.Overwrite, DirItem("Docs"));
            Assert.Equal("old", File.ReadAllText(locked));
            Assert.Equal("new", File.ReadAllText(Path.Combine(receive, "Docs", "ok.txt")));
            Assert.Contains(result.Problems, p => p.Path == "Docs/ro.txt" && p.Outcome == FileOutcome.Failed);
        }
        finally
        {
            File.SetAttributes(locked, FileAttributes.Normal);
        }
    }
}
