using System.Buffers.Binary;
using System.Text;
using Foldspace.Core.Pairing;
using Foldspace.Core.Protocol;
using Foldspace.Core.Settings;
using Foldspace.Core.Transfer;

namespace Foldspace.Core.Tests;

public class PathSafetyTests
{
    private static readonly IReadOnlySet<string> Top = new HashSet<string> { "Photos", "a.txt" };

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("Photos/../../evil.txt")]
    [InlineData("Photos/./x")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("C:evil")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("Photos\\..\\..\\x")]
    [InlineData("Photos//x")]
    [InlineData("Other/x")] // 不屬於 OFFER 宣告的頂層項目
    [InlineData("")]
    [InlineData("Photos/a:stream")]
    public void Rejects_paths_that_could_escape(string path)
    {
        Assert.Throws<SecurityViolationException>(() => PathSafety.SplitRelative(path, Top));
    }

    [Fact]
    public void Accepts_normal_nested_paths()
    {
        Assert.Equal(new[] { "Photos", "2024", "照片 🎉.jpg" }, PathSafety.SplitRelative("Photos/2024/照片 🎉.jpg", Top));
    }

    [Fact]
    public void Combine_stays_inside_base()
    {
        using var dir = new TempDir();
        var path = PathSafety.Combine(dir.Path, ["Photos", "x.jpg"]);
        Assert.StartsWith(Path.GetFullPath(dir.Path), path);
    }

    [Theory]
    [InlineData("CON", NameCheck.Unsupported)]
    [InlineData("nul.txt", NameCheck.Unsupported)]
    [InlineData("COM1", NameCheck.Unsupported)]
    [InlineData("trailing.", NameCheck.Unsupported)]
    [InlineData("trailing ", NameCheck.Unsupported)]
    [InlineData("CONSOLE.txt", NameCheck.Ok)]
    [InlineData("報告.pdf", NameCheck.Ok)]
    public void Detects_names_windows_cannot_create(string name, NameCheck expected)
    {
        Assert.Equal(expected, PathSafety.CheckName(name));
    }
}

public class PairingCodeTests
{
    [Fact]
    public void Both_sides_compute_the_same_code_regardless_of_role()
    {
        var na = PairingCode.NewNonce();
        var nb = PairingCode.NewNonce();
        var a = PairingCode.Compute("sha256:aaa", "sha256:bbb", na, nb);
        var b = PairingCode.Compute("sha256:bbb", "sha256:aaa", na, nb);
        Assert.Equal(a, b);
        Assert.Matches("^[0-9]{6}$", a);
    }

    [Fact]
    public void Different_nonces_give_different_codes()
    {
        var codes = Enumerable.Range(0, 20)
            .Select(_ => PairingCode.Compute("sha256:aaa", "sha256:bbb", PairingCode.NewNonce(), PairingCode.NewNonce()))
            .ToHashSet();
        Assert.True(codes.Count > 15);
    }

    [Fact]
    public void Commitment_only_matches_its_nonce()
    {
        var nonce = PairingCode.NewNonce();
        var commitment = PairingCode.Commit(nonce);
        Assert.True(PairingCode.VerifyCommitment(commitment, nonce));
        Assert.False(PairingCode.VerifyCommitment(commitment, PairingCode.NewNonce()));
    }
}

public class MessageFramingTests
{
    [Fact]
    public async Task Round_trips_polymorphic_messages()
    {
        var stream = new MemoryStream();
        var offer = new TransferOfferMessage
        {
            JobId = Guid.NewGuid(),
            Items = [new OfferItem { Name = "報告.pdf", IsDirectory = false, Bytes = 5_000_000_000, FileCount = 1 }],
            FileCount = 1,
            DirectoryCount = 0,
            TotalBytes = 5_000_000_000,
        };
        await MessageFraming.WriteAsync(stream, offer, default);
        stream.Position = 0;

        var read = Assert.IsType<TransferOfferMessage>(await MessageFraming.ReadAsync(stream, default));
        Assert.Equal(offer.JobId, read.JobId);
        Assert.Equal(5_000_000_000, read.Items[0].Bytes);
        Assert.Equal("報告.pdf", read.Items[0].Name);

        var json = Encoding.UTF8.GetString(stream.ToArray(), 4, (int)stream.Length - 4);
        Assert.Contains("\"type\":\"TRANSFER_OFFER\"", json);
    }

    [Fact]
    public async Task Rejects_messages_over_1MB()
    {
        var frame = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(frame, ProtocolConstants.MaxControlMessageBytes + 1);
        await Assert.ThrowsAsync<ProtocolException>(() => MessageFraming.ReadAsync(new MemoryStream(frame), default));
    }

    [Fact]
    public void Unknown_types_are_not_fatal()
    {
        var message = MessageFraming.Parse(Encoding.UTF8.GetBytes("{\"type\":\"FUTURE_THING\",\"id\":\"" + Guid.NewGuid() + "\"}"));
        Assert.Equal("FUTURE_THING", Assert.IsType<UnknownMessage>(message).RawType);
    }

    [Fact]
    public void Hello_and_ack_are_distinct()
    {
        var ack = new HelloAckMessage
        {
            ProtocolVersion = "1.0", AppVersion = "1.0.0", DeviceId = "x", Hostname = "h", Encryption = true, Paired = true,
        };
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes<ControlMessage>(ack, ProtocolJson.Options);
        Assert.IsType<HelloAckMessage>(MessageFraming.Parse(bytes));
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void Round_trips_and_uses_spec_field_names()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(dir.Sub("settings.json"), TestUtil.Log);
        store.Save(new AppSettings { PeerIp = "192.168.1.20", ConflictPolicy = ConflictPolicy.Overwrite });

        var json = File.ReadAllText(store.Path);
        Assert.Contains("\"peerIp\": \"192.168.1.20\"", json);
        Assert.Contains("\"conflictPolicy\": \"overwrite\"", json);
        Assert.Equal("192.168.1.20", store.Load().PeerIp);
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_defaults_used()
    {
        using var dir = new TempDir();
        var path = dir.Sub("settings.json");
        File.WriteAllText(path, "{ not json");
        var settings = new SettingsStore(path, TestUtil.Log).Load();

        Assert.Equal(ProtocolConstants.DefaultPort, settings.LocalPort);
        Assert.True(File.Exists(dir.Sub("settings.bad.json")));
    }

    [Theory]
    [InlineData("192.168.1.20", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("1", false)]
    [InlineData("256.1.1.1", false)]
    [InlineData("01.2.3.4", false)]
    [InlineData("1.2.3", false)]
    [InlineData("", false)]
    public void Validates_ipv4(string text, bool valid) => Assert.Equal(valid, AppSettings.IsValidIPv4(text));
}

public class NamingTests
{
    [Theory]
    [InlineData("report.pdf", false, "report (1).pdf")]
    [InlineData("Photos", true, "Photos (1)")]
    [InlineData(".gitignore", false, ".gitignore (1)")]
    [InlineData("archive.tar.gz", false, "archive.tar (1).gz")]
    public void Name_with_counter(string name, bool isDir, string expected) =>
        Assert.Equal(expected, TransferScanner.NameWithCounter(name, 1, isDir));
}

public class LocalAdapterResolverTests
{
    private static AdapterInfo Nic(string id, string ip, bool gateway = true) =>
        new(id, id, System.Net.IPAddress.Parse(ip), gateway);

    [Fact]
    public void Same_adapter_follows_its_new_ip()
    {
        var adapters = new[] { Nic("{A}", "192.168.0.50") };
        Assert.Equal("192.168.0.50", LocalAdapterResolver.Resolve(adapters, "{A}", "192.168.0.20")!.Address.ToString());
    }

    [Fact]
    public void Replaced_adapter_with_same_ip_is_found()
    {
        // e1000 換成 virtio：Windows 給了新的網卡 ID，DHCP 依 MAC 給同一個 IP
        var adapters = new[] { Nic("{NEW}", "192.168.0.204"), Nic("{WIFI}", "10.0.0.5") };
        Assert.Equal("{NEW}", LocalAdapterResolver.Resolve(adapters, "{OLD}", "192.168.0.204")!.Id);
    }

    [Fact]
    public void Missing_adapter_and_ip_does_not_switch_to_another_network()
    {
        // 拔線：不能自動改用 Wi-Fi，狀態要顯示「本機 IP 已不存在」
        var adapters = new[] { Nic("{WIFI}", "10.0.0.5") };
        Assert.Null(LocalAdapterResolver.Resolve(adapters, "{OLD}", "192.168.0.204"));
        Assert.Null(LocalAdapterResolver.Resolve(adapters, "{OLD}", null));
    }

    [Fact]
    public void Automatic_prefers_adapter_with_gateway()
    {
        var adapters = new[] { Nic("{HOSTONLY}", "172.16.0.1", gateway: false), Nic("{LAN}", "192.168.0.204") };
        Assert.Equal("{LAN}", LocalAdapterResolver.Resolve(adapters, null, null)!.Id);
    }
}

public class VersionTests
{
    [Fact]
    public void Versions_are_product_and_internal_build_counter()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", ProtocolConstants.AppVersion);      // 產品版本，例如 1.0.0
        Assert.StartsWith(ProtocolConstants.AppVersion, ProtocolConstants.BuildVersion); // 建置版本，例如 1.0.0+202610041530
    }
}

public class LogTextTests
{
    [Fact]
    public void CancelReasonsHaveReadableLogText()
    {
        Assert.Equal("no", ((CancelReason?)null).ToLogText());
        Assert.Equal("cancelled by local user", CancelReason.User.ToLogText());
        foreach (var reason in Enum.GetValues<CancelReason>())
            Assert.NotEqual(reason.ToString(), reason.ToLogText());
    }

    [Fact]
    public void SocketErrorsAreDescribedByCodeNotOsText()
    {
        var ex = new IOException("Unable to write data to the transport connection",
            new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionAborted));
        Assert.Equal("IOException: socket error ConnectionAborted (10053)", Foldspace.Core.Net.NetworkError.Describe(ex));
    }
}
