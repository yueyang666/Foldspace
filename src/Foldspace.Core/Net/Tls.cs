using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Foldspace.Core.Identity;
using Foldspace.Core.Protocol;

namespace Foldspace.Core.Net;

/// <summary>
/// 雙向憑證的 TLS。憑證都是自簽的，所以 TLS 層接受任何憑證，
/// 身分驗證在應用層比對指紋（未配對時只允許握手、心跳、配對）。
/// <para>Windows 10 的 Schannel 不支援 TLS 1.3，因此允許 1.2 並由系統協商。</para>
/// </summary>
internal static class Tls
{
    private const SslProtocols Protocols = SslProtocols.Tls12 | SslProtocols.Tls13;

    public static async Task<(SslStream Stream, string RemoteFingerprint)> AuthenticateAsClientAsync(
        Stream inner, DeviceIdentity identity, CancellationToken ct)
    {
        var ssl = new SslStream(inner, leaveInnerStreamOpen: false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProtocolConstants.TlsTimeout);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "foldspace",
                // 用 CertificateContext 而不是 ClientCertificates：後者會因為自簽憑證不在對方的信任清單而不送出。
                ClientCertificateContext = SslStreamCertificateContext.Create(identity.Certificate, additionalCertificates: null, offline: true),
                EnabledSslProtocols = Protocols,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
            }, timeout.Token).ConfigureAwait(false);
            return (ssl, RemoteFingerprintOf(ssl));
        }
        catch
        {
            await ssl.DisposeAsync();
            throw;
        }
    }

    public static async Task<(SslStream Stream, string RemoteFingerprint)> AuthenticateAsServerAsync(
        Stream inner, DeviceIdentity identity, CancellationToken ct)
    {
        var ssl = new SslStream(inner, leaveInnerStreamOpen: false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProtocolConstants.TlsTimeout);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificateContext = SslStreamCertificateContext.Create(identity.Certificate, additionalCertificates: null, offline: true),
                ClientCertificateRequired = true,
                EnabledSslProtocols = Protocols,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
            }, timeout.Token).ConfigureAwait(false);
            return (ssl, RemoteFingerprintOf(ssl));
        }
        catch
        {
            await ssl.DisposeAsync();
            throw;
        }
    }

    private static string RemoteFingerprintOf(SslStream ssl)
    {
        if (ssl.RemoteCertificate is null)
            throw new AuthenticationException("Peer did not present a certificate");
        if (ssl.RemoteCertificate is X509Certificate2 cert2)
            return DeviceIdentity.ComputeFingerprint(cert2); // 由 SslStream 持有，不要 Dispose
        using var copy = new X509Certificate2(ssl.RemoteCertificate);
        return DeviceIdentity.ComputeFingerprint(copy);
    }
}
