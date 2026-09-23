using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;

namespace MusicNetEasePlugin.Infrastructure.Protocol;

internal sealed record XeapiPublicKey(string PublicKey, string Version, string Sk)
{
    public override string ToString() => "[xeapi 公钥状态]";
}

/// <summary>
/// 移植固定上游 a8c781f 的 xeapi：两层 ECB、随机 XOR/旋转和 X25519 + AES-GCM 密钥封装。
/// 这些常量属于网易兼容协议，不用于本地账号保护。固定熵参数仅供独立向量测试，生产始终使用系统随机源。
/// </summary>
internal static class XeapiCrypto
{
    private static readonly byte[] StaticKey = Convert.FromHexString("ab1d5a430f6bb04a3f01e81ddd72bd916d5ce591248ac128714806d7f8fb1b84");
    private const string SignKey = "mUHCwVNWJbunMqAHf5MImuirT6plvs6VSFW62MGHstFQxhBGdEoIhLItH3djc4+FB/OKty3+lL2rGeoFBpVe5g==";
    public static string Sign(string timestamp, string nonce) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(SignKey), Encoding.UTF8.GetBytes(timestamp + nonce)));

    public static XeapiPublicKey DecodePublicKey(string encrypted)
    {
        using var document = JsonDocument.Parse(Decrypt(StaticKey, Convert.FromBase64String(encrypted)));
        var body = document.RootElement;
        var key = body.GetProperty("publicKey").GetString()!;
        var version = body.GetProperty("version").ToString();
        var sk = body.GetProperty("sk").GetString()!;
        if (Convert.FromBase64String(key).Length != 32 || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(sk))
            throw new CryptographicException();
        return new(key, version, sk);
    }
    public static Dictionary<string, string> Encode(Dictionary<string, string> data, XeapiPublicKey peer,
        string? sessionId = null, string? sessionKey = null, byte[]? dynamicKey = null,
        byte[]? mask = null, byte[]? privateKey = null, byte[]? nonce = null)
    {
        dynamicKey = sessionKey is null ? dynamicKey ?? RandomNumberGenerator.GetBytes(16) : Encoding.UTF8.GetBytes(sessionKey);
        mask ??= RandomNumberGenerator.GetBytes(16);
        privateKey ??= RandomNumberGenerator.GetBytes(32);
        nonce ??= RandomNumberGenerator.GetBytes(12);
        if (dynamicKey.Length is not (16 or 24 or 32) || mask.Length != 16 || privateKey.Length != 32 || nonce.Length != 12)
            throw new CryptographicException("协议参数长度无效。");
        var form = string.Join("&", data.Where(p => p.Key != "e_r").Select(p => Form(p.Key) + "=" + Form(p.Value)));
        var envelope = NeteaseCrypto.Json(new { body = Convert.ToBase64String(Encoding.UTF8.GetBytes(form)), queryString = "e_r=true" });
        var inner = Encrypt(StaticKey, Encoding.UTF8.GetBytes(envelope));
        for (var i = 0; i < inner.Length; i++) inner[i] ^= mask[i & 15];
        var b64 = Encoding.ASCII.GetBytes(Convert.ToBase64String(inner));
        var rotation = (mask[0] & 15) % b64.Length;
        var transformed = mask.Concat(b64.Skip(rotation)).Concat(b64.Take(rotation)).ToArray();
        var secret = new X25519PrivateKeyParameters(privateKey);
        var ephemeral = secret.GeneratePublicKey().GetEncoded();
        var shared = new byte[32];
        secret.GenerateSecret(new X25519PublicKeyParameters(Convert.FromBase64String(peer.PublicKey)), shared, 0);
        var prk = HMACSHA256.HashData(new byte[32], shared);
        var aesKey = HMACSHA256.HashData(prk, ephemeral.Concat(new byte[] { 1 }).ToArray())[..16];
        try
        {
            var plain = Encoding.UTF8.GetBytes(Convert.ToBase64String(dynamicKey) + "|android|" + peer.Sk);
            var encrypted = new byte[plain.Length];
            var tag = new byte[16];
            using var gcm = new AesGcm(aesKey, 16);
            gcm.Encrypt(nonce, plain, encrypted, tag);
            return new()
            {
                ["B"] = Convert.ToBase64String(Encrypt(dynamicKey, transformed)),
                ["S"] = Convert.ToBase64String(ephemeral.Concat(nonce).Concat(encrypted).Concat(tag).ToArray()),
                ["R"] = Convert.ToBase64String(Encrypt(StaticKey, Encoding.UTF8.GetBytes(peer.Version + "|" + (sessionKey is null ? "" : sessionId))))
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
            CryptographicOperations.ZeroMemory(prk);
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }
    private static string Form(string value) => Uri.EscapeDataString(value).Replace("%20", "+").Replace("%2A", "*").Replace("~", "%7E");
    private static byte[] Encrypt(byte[] key, byte[] data) { using var aes = Aes.Create(); aes.Key = key; return aes.EncryptEcb(data, PaddingMode.PKCS7); }
    private static byte[] Decrypt(byte[] key, byte[] data) { using var aes = Aes.Create(); aes.Key = key; return aes.DecryptEcb(data, PaddingMode.PKCS7); }
}
