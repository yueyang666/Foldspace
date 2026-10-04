using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Serilog;

namespace Foldspace.Core.Identity;

/// <summary>保護私鑰檔的方式。Windows 用 DPAPI（實作在 App），測試用不加密版本。</summary>
public interface IKeyProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

public sealed class NoOpKeyProtector : IKeyProtector
{
    public static readonly NoOpKeyProtector Instance = new();
    public byte[] Protect(byte[] data) => data;
    public byte[] Unprotect(byte[] data) => data;
}

/// <summary>本機的自簽憑證（ECDSA P-256，20 年）。設備 ID = 公鑰的 SHA-256 指紋。</summary>
public sealed class DeviceIdentity : IDisposable
{
    private DeviceIdentity(X509Certificate2 certificate, byte[] pfx)
    {
        Certificate = certificate;
        Pfx = pfx;
        Fingerprint = ComputeFingerprint(certificate);
    }

    public X509Certificate2 Certificate { get; }
    public string Fingerprint { get; }
    internal byte[] Pfx { get; }

    public static DeviceIdentity CreateNew(string hostname)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=Foldspace {hostname}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], critical: false));

        var now = DateTimeOffset.UtcNow;
        using var ephemeral = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(20));
        // Windows 的 Schannel 不能用記憶體中的臨時金鑰當 TLS 憑證，所以匯出成 PFX 再載入一次。
        var pfx = ephemeral.Export(X509ContentType.Pfx);
        return FromPfx(pfx);
    }

    public static DeviceIdentity FromPfx(byte[] pfx) =>
        new(X509CertificateLoader.LoadPkcs12(pfx, password: null), pfx);

    /// <summary>從檔案載入；不存在或損毀時產生新的身分（對方需重新配對）。</summary>
    public static DeviceIdentity LoadOrCreate(string path, IKeyProtector protector, string hostname, ILogger log)
    {
        if (File.Exists(path))
        {
            try
            {
                return FromPfx(protector.Unprotect(File.ReadAllBytes(path)));
            }
            catch (Exception ex)
            {
                log.Error(ex, "Device certificate could not be read; generating a new one (the peer will need to pair again)");
                TryBackup(path);
            }
        }

        var identity = CreateNew(hostname);
        AtomicFile.WriteAllBytes(path, protector.Protect(identity.Pfx));
        log.Information("Generated new device certificate {Fingerprint}", identity.Fingerprint);
        return identity;
    }

    public static string ComputeFingerprint(X509Certificate2 certificate) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));

    /// <summary>顯示用的短指紋，例如 <c>3F2A·91C0·…</c>。</summary>
    public static string ShortFingerprint(string fingerprint)
    {
        var hex = fingerprint.StartsWith("sha256:", StringComparison.Ordinal) ? fingerprint[7..] : fingerprint;
        return hex.Length < 16 ? hex : string.Join('·', Enumerable.Range(0, 4).Select(i => hex.Substring(i * 4, 4))).ToUpperInvariant();
    }

    private static void TryBackup(string path)
    {
        try { File.Copy(path, path + ".bad", overwrite: true); }
        catch { /* 備份失敗不影響產生新憑證 */ }
    }

    public void Dispose() => Certificate.Dispose();
}
