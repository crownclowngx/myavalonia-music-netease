using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MusicNetEasePlugin.Infrastructure.Protocol;

/// <summary>
/// 按 api-enhanced 固定提交移植 eapi/weapi。它们是远端兼容协议，不用于本地凭据保护。
/// 上游来源及 MIT 许可见 THIRD-PARTY-NOTICES.md；不能把算法替换为“更常见”的填充而改变线上协议。
/// </summary>
internal static class NeteaseCrypto
{
    private const string Iv = "0102030405060708";
    private const string PresetKey = "0CoJUm6Qyw8W8jud";
    private const string EapiKey = "e82ckenh8dichen8";
    private const string PublicKey = """
        -----BEGIN PUBLIC KEY-----
        MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDgtQn2JZ34ZC28NWYpAUd98iZ37BUrX/aKzmFbt7clFSs6sXqHauqKWqdtLkF2KexO40H1YTX8z2lSgBBOAxLsvaklV8k4cBFK9snQXE9/DDaFt6Rr7iVZMldczhC0JNgTz+SHXT6CBHuX3e9SdB1Ua44oncaTWz7OBGLbCiK45wIDAQAB
        -----END PUBLIC KEY-----
        """;

    // JSON.stringify 不转义中文；选项只在协议边界使用，不修改应用/Flurl 的全局序列化。
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = 32
    };

    internal static string Json(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        // System.Text.Json 即使放宽编码仍会转义补充平面字符，而 JSON.stringify 输出原始字符。
        // 只还原真正的成对代理转义；偶数个反斜杠前缀保留，不能误改用户输入的字面量“\\u...”。
        return Regex.Replace(json,
            """(?<!\\)(?<prefix>(?:\\\\)*)\\u(?<high>[dD][89aAbB][0-9a-fA-F]{2})\\u(?<low>[dD][c-fC-F][0-9a-fA-F]{2})""",
            match => match.Groups["prefix"].Value +
                (char)Convert.ToInt32(match.Groups["high"].Value, 16) +
                (char)Convert.ToInt32(match.Groups["low"].Value, 16),
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    public static Dictionary<string, string> Eapi(string path, string json)
    {
        var digest = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes($"nobody{path}use{json}md5forencrypt")));
        var plaintext = $"{path}-36cd479b6b5-{json}-36cd479b6b5-{digest}";
        return new() { ["params"] = Convert.ToHexString(Encrypt(Encoding.UTF8.GetBytes(plaintext), EapiKey, false)) };
    }

    public static Dictionary<string, string> Weapi(string json, string? secret = null)
    {
        secret ??= RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 16);
        if (secret.Length != 16 || secret.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("weapi 密钥必须是 16 位字母数字。", nameof(secret));
        var first = Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(json), PresetKey, true));
        var second = Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(first), secret, true));

        // 网易使用裸 RSA：大端无符号整数模幂，左侧补零至模数长度。
        // RSA.Encrypt 默认的 PKCS#1/OAEP 都不符合此协议，不能直接调用替代。
        using var rsa = RSA.Create();
        rsa.ImportFromPem(PublicKey);
        var parameters = rsa.ExportParameters(false);
        var message = new BigInteger(Encoding.ASCII.GetBytes(new string(secret.Reverse().ToArray())), true, true);
        var modulus = new BigInteger(parameters.Modulus!, true, true);
        var exponent = new BigInteger(parameters.Exponent!, true, true);
        var encoded = BigInteger.ModPow(message, exponent, modulus).ToByteArray(true, true);
        var padded = new byte[parameters.Modulus!.Length];
        encoded.CopyTo(padded.AsSpan(padded.Length - encoded.Length));
        return new() { ["params"] = second, ["encSecKey"] = Convert.ToHexStringLower(padded) };
    }

    internal static byte[] DecodeEapi(ReadOnlySpan<byte> ciphertext)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.ASCII.GetBytes(EapiKey);
        return aes.DecryptEcb(ciphertext, PaddingMode.PKCS7);
    }

    private static byte[] Encrypt(byte[] plaintext, string key, bool cbc)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.ASCII.GetBytes(key);
        return cbc ? aes.EncryptCbc(plaintext, Encoding.ASCII.GetBytes(Iv), PaddingMode.PKCS7)
                   : aes.EncryptEcb(plaintext, PaddingMode.PKCS7);
    }
}
