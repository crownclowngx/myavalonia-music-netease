using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using MusicNetEasePlugin.Infrastructure.Protocol;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Application.Authentication;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class MusicProtocolTests
{
    internal static JsonDocument Fixture() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xeapi-vectors.json")));
    [Fact, Trait("M1", "P02,P03,P04")]
    public void 首次及复用会话的三段加密逐字节匹配固定上游()
    {
        using var fixture = Fixture();
        var root = fixture.RootElement;
        Assert.Equal("192556e34ed897e367be23349b633db245dd65751ac45a5c055f765bba62e462", root.GetProperty("sourceSha256").GetString());
        var peer = XeapiCrypto.DecodePublicKey(root.GetProperty("encryptedKey").GetString()!);
        Assert.Equal(root.GetProperty("peer").GetProperty("publicKey").GetString(), peer.PublicKey);
        Assert.Equal(root.GetProperty("signature").GetString(), XeapiCrypto.Sign("1700000000000", "1234567890123456"));
        byte[] Fixed(int n) => Enumerable.Range(1, n).Select(i => (byte)i).ToArray();
        foreach (var vector in root.GetProperty("vectors").EnumerateArray())
        {
            var data = vector.GetProperty("data").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
            var actual = XeapiCrypto.Encode(data, peer, vector.GetProperty("sessionId").GetString(), vector.GetProperty("sessionKey").GetString(), Fixed(16), Fixed(16), Fixed(32), Fixed(12));
            foreach (var part in new[] { "B", "S", "R" }) Assert.Equal(vector.GetProperty("expected").GetProperty(part).GetString(), actual[part]);
        }
        Assert.DoesNotContain(peer.Sk, peer.ToString());
    }

    [Fact, Trait("M1", "P05")]
    public void 播放响应接受明文及加密压缩但拒绝解压炸弹和坏密文()
    {
        var json = "{\"code\":200,\"data\":[]}"u8.ToArray();
        byte[] Encrypt(byte[] plain) { using var aes = Aes.Create(); aes.Key = Encoding.ASCII.GetBytes("e82ckenh8dichen8"); return aes.EncryptEcb(plain, PaddingMode.PKCS7); }
        byte[] Compress(byte[] plain)
        {
            using var stream = new MemoryStream();
            using (var gzip = new GZipStream(stream, CompressionLevel.SmallestSize, true)) gzip.Write(plain);
            return stream.ToArray();
        }
        foreach (var input in new[] { json, Encrypt(json), Encrypt(Compress(json)) })
            Assert.Equal(200, NeteaseTransport.Code(NeteaseTransport.Decode(input, NeteaseProtocol.Eapi)));
        Assert.Throws<AuthException>(() => NeteaseTransport.Decode([1, 2, 3], NeteaseProtocol.Eapi));
        Assert.Throws<AuthException>(() => NeteaseTransport.Decode(Encrypt(Compress(new byte[NeteaseTransport.MaximumResponseBytes + 1])), NeteaseProtocol.Eapi));
    }
}
