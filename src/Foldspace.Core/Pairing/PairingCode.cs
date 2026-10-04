using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Foldspace.Core.Pairing;

/// <summary>
/// 數字比對配對碼。
/// <para>
/// 如果只用兩個指紋計算，中間人可以暴力產生金鑰讓兩端數字相同。
/// 這裡加上「先承諾、後公開」的亂數：發起端先送 SHA-256(Na)，回應端回 Nb，發起端再公開 Na。
/// 中間人在選擇自己的亂數時看不到對方尚未公開的亂數，只有百萬分之一的機會讓兩端數字一致。
/// </para>
/// </summary>
public static class PairingCode
{
    public const int NonceBytes = 32;

    public static byte[] NewNonce() => RandomNumberGenerator.GetBytes(NonceBytes);

    public static byte[] Commit(byte[] nonce) => SHA256.HashData(nonce);

    public static bool VerifyCommitment(byte[] commitment, byte[] nonce) =>
        nonce.Length == NonceBytes && CryptographicOperations.FixedTimeEquals(commitment, Commit(nonce));

    /// <summary>兩端用相同輸入算出相同的 6 位數字（指紋排序後串接，與誰是發起端無關）。</summary>
    public static string Compute(string fingerprintA, string fingerprintB, byte[] initiatorNonce, byte[] responderNonce)
    {
        var ordered = string.CompareOrdinal(fingerprintA, fingerprintB) <= 0
            ? fingerprintA + fingerprintB
            : fingerprintB + fingerprintA;

        var fingerprints = Encoding.UTF8.GetBytes(ordered);
        var input = new byte[fingerprints.Length + initiatorNonce.Length + responderNonce.Length];
        fingerprints.CopyTo(input, 0);
        initiatorNonce.CopyTo(input, fingerprints.Length);
        responderNonce.CopyTo(input, fingerprints.Length + initiatorNonce.Length);

        var hash = SHA256.HashData(input);
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash) % 1_000_000;
        return value.ToString("D6");
    }

    /// <summary>顯示格式：<c>123 456</c>。</summary>
    public static string Format(string code) => code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;
}
