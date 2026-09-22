using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Infrastructure.Protocol;
using MusicNetEasePlugin.Application.Authentication;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class ProtocolTests
{
    [Fact]
    [Trait("Scenario", "P01,P02,P03")]
    public void 加密结果与固定上游原始模块生成的向量一致()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "protocol-vectors.json")));
        foreach (var vector in fixture.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var json = vector.GetProperty("json").GetString()!;
            var eapi = NeteaseCrypto.Eapi(vector.GetProperty("path").GetString()!, json);
            var weapi = NeteaseCrypto.Weapi(json, vector.GetProperty("secret").GetString());
            Assert.Equal(vector.GetProperty("eapi").GetProperty("params").GetString(), eapi["params"]);
            Assert.Equal(vector.GetProperty("weapi").GetProperty("params").GetString(), weapi["params"]);
            Assert.Equal(vector.GetProperty("weapi").GetProperty("encSecKey").GetString(), weapi["encSecKey"]);
            Assert.Equal(256, weapi["encSecKey"].Length);
            using var parsed = JsonDocument.Parse(json);
            Assert.Equal(json, NeteaseCrypto.Json(parsed.RootElement));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("abcdefghijklmn中文")]
    public void 拒绝不符合协议的测试密钥(string secret) =>
        Assert.Throws<ArgumentException>(() => NeteaseCrypto.Weapi("{}", secret));

    [Fact]
    [Trait("Scenario", "P07")]
    public void 明文及加密响应都可解释而无效密文拒绝()
    {
        var plain = Encoding.UTF8.GetBytes("{\"code\":200}");
        using var aes = Aes.Create();
        aes.Key = Encoding.ASCII.GetBytes("e82ckenh8dichen8");
        var cipher = aes.EncryptEcb(plain, PaddingMode.PKCS7);
        Assert.Equal(200, NeteaseTransport.Code(NeteaseTransport.Decode(plain, NeteaseProtocol.Eapi)));
        Assert.Equal(200, NeteaseTransport.Code(NeteaseTransport.Decode(cipher, NeteaseProtocol.Eapi)));
        Assert.Throws<AuthException>(() => NeteaseTransport.Decode([1, 2, 3], NeteaseProtocol.Eapi));
        Assert.Throws<AuthException>(() => NeteaseTransport.Decode(Encoding.UTF8.GetBytes("<html>"), NeteaseProtocol.Weapi));
    }

    [Fact]
    [Trait("Scenario", "P08,P09")]
    public void 本地二维码内容仅编码一次并输出PNG()
    {
        Assert.Equal("https://music.163.com/login?codekey=a%2Bb%20%26%25", LoginQrCode.Url("a+b &%"));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, LoginQrCode.Render("fixture").Take(8));
        Assert.Throws<ArgumentException>(() => LoginQrCode.Render(""));
        Assert.Throws<ArgumentException>(() => LoginQrCode.Render(new string('a', 2049)));
    }

    [Fact]
    [Trait("Scenario", "P03,P04,P05,P06")]
    public void 最终请求使用稳定设备和正确的逻辑端点()
    {
        var context = AuthContext.Create();
        var now = DateTimeOffset.UtcNow;
        var first = NeteaseRequestEncoder.Encode("/api/login/qrcode/unikey", new() { ["type"] = 3 }, NeteaseProtocol.Eapi, context, now);
        var second = NeteaseRequestEncoder.Encode("/api/login/qrcode/unikey", new() { ["type"] = 3 }, NeteaseProtocol.Eapi, context, now);
        Assert.Equal("/eapi/login/qrcode/unikey", first.Route);
        Assert.Contains("deviceId=" + context.DeviceId, first.Headers["Cookie"]);
        Assert.Contains("deviceId=" + context.DeviceId, second.Headers["Cookie"]);
        var weapi = NeteaseRequestEncoder.Encode("/api/w/nuser/account/get", new(), NeteaseProtocol.Weapi, context, now);
        Assert.Equal("/weapi/w/nuser/account/get", weapi.Route);
        Assert.Equal("https://music.163.com", weapi.Headers["Referer"]);
        Assert.Throws<ArgumentException>(() => NeteaseRequestEncoder.Encode("https://other.invalid", new(), NeteaseProtocol.Eapi, context, now));
    }
}
