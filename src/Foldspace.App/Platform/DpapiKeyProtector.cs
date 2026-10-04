using System.Security.Cryptography;
using Foldspace.Core.Identity;

namespace Foldspace.App.Platform;

/// <summary>用目前 Windows 使用者的 DPAPI 保護憑證私鑰檔。</summary>
public sealed class DpapiKeyProtector : IKeyProtector
{
    // 改掉這個值的話，已經存在的設備憑證就解不開（會自動產生新的），所有配對都要重來。
    private static readonly byte[] Entropy = "Foldspace.Identity.v1"u8.ToArray();

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
