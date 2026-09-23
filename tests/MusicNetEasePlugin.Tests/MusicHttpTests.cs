using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flurl.Http.Configuration;
using Flurl.Http.Testing;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Infrastructure.Protocol;
using Xunit;

namespace MusicNetEasePlugin.Tests;

internal sealed class ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    public static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    public static Dictionary<string, string> Form(string value) => value.Split('&').Select(s => s.Split('=', 2))
        .ToDictionary(p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p[1]));
}

public sealed class MusicHttpTests
{
    private static NeteaseMusicApi Api(NeteaseFlurlClients clients, MusicSessions sessions)
    {
        var transport = new NeteaseTransport(clients, TimeProvider.System);
        return new(transport, new(transport, clients, TimeProvider.System, sessions), sessions);
    }
    [Fact, Trait("M1", "P01,P02,C02,C04")]
    public async Task 搜索分页参数经真实编码且详情保持长整型身份及缺省资料()
    {
        using var http = new HttpTest();
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        using var sessions = new MusicSessions();
        const long id = 9007199254740991;
        var songs = Enumerable.Range(0, 30).Select(i => new { id = id - i, name = "歌" + i });
        http.RespondWithJson(new { code = 200, result = new { songs, songCount = 61 } });
        http.RespondWithJson(new { code = 200, songs = new[] { new { id, name = "中文😃", dt = 42000 } } });
        var api = Api(clients, sessions);
        var page = await api.SearchAsync(" 中文 & + 😃 ", 30, sessions.Capture(), default);
        Assert.True(page.HasMore); Assert.Equal(30, page.Tracks.Count);
        var call = http.CallLog[0];
        Assert.Equal("/eapi/cloudsearch/pc", new Uri(call.Request.Url).AbsolutePath);
        var cipher = ScriptedHandler.Form(call.RequestBody)["params"];
        var payload = Encoding.UTF8.GetString(NeteaseCrypto.DecodeEapi(Convert.FromHexString(cipher))).Split("-36cd479b6b5-")[1];
        using var data = JsonDocument.Parse(payload);
        Assert.Equal("中文 & + 😃", data.RootElement.GetProperty("s").GetString());
        Assert.Equal(30, data.RootElement.GetProperty("offset").GetInt32());
        Assert.Equal(30, data.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal(1, data.RootElement.GetProperty("type").GetInt32());
        var detail = await api.DetailAsync(id, sessions.Capture(), default);
        Assert.Equal(id, detail.Id); Assert.Equal(42000, detail.DurationMs);
        Assert.Equal("未知歌手", detail.Artists); Assert.Equal("未知专辑", detail.Album);
        Assert.Equal("/weapi/v3/song/detail", new Uri(http.CallLog[1].Request.Url).AbsolutePath);
        Assert.Equal(new[] { "params", "encSecKey" }, ScriptedHandler.Form(http.CallLog[1].RequestBody).Keys);
    }

    [Theory, InlineData("{}"), InlineData("{\"result\":{\"songs\":\"bad\"},\"code\":200}"), InlineData("{\"songs\":[{\"id\":0,\"name\":\"x\"}],\"code\":200}")]
    [Trait("M1", "P05,C04,C06")]
    public async Task 缺失或畸形结构不会伪装成有效搜索结果(string response)
    {
        using var http = new HttpTest(); http.RespondWith(response);
        using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var error = await Assert.ThrowsAsync<MusicException>(() => Api(clients, sessions).SearchAsync("音乐", 0, sessions.Capture(), default));
        Assert.Equal(MusicError.Protocol, error.Kind); Assert.Null(error.InnerException);
    }

    [Theory, InlineData(301, MusicError.SignedOut, false), InlineData(429, MusicError.Restricted, true), InlineData(460, MusicError.Restricted, true), InlineData(500, MusicError.Protocol, true)]
    [Trait("M1", "P06,A05")]
    public async Task 仅明确会话失效退出且服务正文不泄露(int code, MusicError kind, bool signedIn)
    {
        using var http = new HttpTest(); http.RespondWithJson(new { code, message = "MUSIC_U=secret" });
        using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var error = await Assert.ThrowsAsync<MusicException>(() => Api(clients, sessions).SearchAsync("音乐", 0, sessions.Capture(), default));
        Assert.Equal(kind, error.Kind); Assert.Equal(signedIn, sessions.SignedIn);
        Assert.DoesNotContain("secret", error.ToString()); Assert.Single(http.CallLog);
    }

    [Fact, Trait("M1", "P01,P03,P04,L06")]
    public async Task 真实握手串行初始化且协议会话仅在同账号内复用()
    {
        using var fixture = MusicProtocolTests.Fixture();
        var encryptedKey = fixture.RootElement.GetProperty("encryptedKey").GetString();
        var registrations = 0; var requests = new List<Dictionary<string, string>>();
        var cache = new FlurlClientCache();
        cache.Add("netease-keys", "https://interface.music.163.com", b => b.AddMiddleware(() => new ScriptedHandler(async (request, ct) =>
        {
            registrations++;
            var form = ScriptedHandler.Form(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("/api/gorilla/anti/crawler/security/key/get", request.RequestUri!.AbsolutePath);
            Assert.Equal(XeapiCrypto.Sign(form["timestamp"], form["nonce"]), form["signature"]);
            return ScriptedHandler.Json(new { code = 200, data = new { timestamp = form["timestamp"], signature = form["signature"], encryptedData = encryptedKey } });
        })));
        cache.Add("netease-xeapi", "https://interface3.music.163.com", b => b.AddMiddleware(() => new ScriptedHandler(async (request, ct) =>
        {
            Assert.Equal("/xeapi/song/enhance/player/url/v1", request.RequestUri!.AbsolutePath);
            Assert.True(request.Headers.Contains("x-music-u"));
            var form = ScriptedHandler.Form(await request.Content!.ReadAsStringAsync(ct)); requests.Add(form);
            using var aes = Aes.Create(); aes.Key = Convert.FromHexString("ab1d5a430f6bb04a3f01e81ddd72bd916d5ce591248ac128714806d7f8fb1b84");
            var r = Encoding.UTF8.GetString(aes.DecryptEcb(Convert.FromBase64String(form["R"]), PaddingMode.PKCS7));
            Assert.Equal(registrations == 1 && requests.Count > 1 ? "fixture-v1|fixture-session" : "fixture-v1|", r);
            var reply = ScriptedHandler.Json(new { code = 200, data = new[] { new { id = 1, url = "https://m1.music.126.net/fixture", type = "mp3", level = "standard", freeTrialInfo = new { start = 30, end = 60 } } } });
            reply.Headers.Add("x-encr-ssid", "fixture-session"); reply.Headers.Add("x-encr-sskey", "session-key-1234");
            return reply;
        })));
        using var clients = new NeteaseFlurlClients(cache); using var sessions = new MusicSessions();
        var api = Api(clients, sessions);
        var results = await Task.WhenAll(api.ResolveAsync(1, sessions.Capture(), default), api.ResolveAsync(1, sessions.Capture(), default));
        Assert.Equal(1, registrations); Assert.All(results, r => { Assert.True(r.IsTrial); Assert.Equal(30000, r.TrialDurationMs); Assert.DoesNotContain("https", r.ToString()); });
        sessions.Relogin(); await api.ResolveAsync(1, sessions.Capture(), default);
        Assert.Equal(2, registrations);
    }

    [Theory, InlineData(false), InlineData(true)]
    [Trait("M1", "P03,P04")]
    public async Task 握手签名错误或取消都不能调用播放地址(bool cancel)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new FlurlClientCache().Add("netease-keys", "https://interface.music.163.com", b => b.AddMiddleware(() => new ScriptedHandler(async (_, ct) =>
        {
            entered.SetResult();
            if (cancel) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return ScriptedHandler.Json(new { code = 200, data = new { timestamp = "0", signature = Convert.ToBase64String(new byte[32]), encryptedData = "bad" } });
        })));
        cache.Add("netease-xeapi", "https://interface3.music.163.com", b => b.AddMiddleware(() => new ScriptedHandler((_, _) => throw new InvalidOperationException("不应请求地址"))));
        using var clients = new NeteaseFlurlClients(cache); using var sessions = new MusicSessions(); using var cts = new CancellationTokenSource();
        var request = Api(clients, sessions).ResolveAsync(1, sessions.Capture(), cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (cancel) { cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request); }
        else Assert.Equal(MusicError.Protocol, (await Assert.ThrowsAsync<MusicException>(() => request)).Kind);
    }
}
