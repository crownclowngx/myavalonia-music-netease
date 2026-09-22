using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using Flurl.Http.Configuration;
using Flurl.Http.Testing;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Infrastructure.Protocol;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class HttpTests
{
    private sealed class WaitingHandler : DelegatingHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("受控 handler 不能落到真实网络。");
        }
    }

    [Fact]
    [Trait("Scenario", "H04")]
    public async Task 取消令牌贯穿在途Flurl请求且无底层异常泄露()
    {
        var handler = new WaitingHandler();
        var cache = new FlurlClientCache().Add("netease-eapi", "https://interfacepc.music.163.com", builder => builder.AddMiddleware(() => handler));
        using var clients = new NeteaseFlurlClients(cache);
        using var cancellation = new CancellationTokenSource();
        var request = Api(clients).CreateKeyAsync(Context, cancellation.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Null(error.InnerException);
    }

    private static readonly AuthContext Context = AuthContext.Create();
    private static NeteaseAuthApi Api(NeteaseFlurlClients clients) => new(new(clients, TimeProvider.System));

    [Fact]
    [Trait("Scenario", "H01,P04,P11")]
    public async Task 获取key走Flurl表单而不是Node外壳或JSON()
    {
        using var http = new HttpTest();
        http.RespondWithJson(new { code = 200, unikey = "fixture" });
        var cache = new FlurlClientCache();
        using var clients = new NeteaseFlurlClients(cache);
        var result = await Api(clients).CreateKeyAsync(Context, default);
        Assert.Equal("fixture", result.Key);
        var call = Assert.Single(http.CallLog);
        Assert.Equal("https://interfacepc.music.163.com/eapi/login/qrcode/unikey", call.Request.Url.ToString());
        Assert.Equal(HttpMethod.Post, call.HttpRequestMessage.Method);
        Assert.Equal("application/x-www-form-urlencoded", call.HttpRequestMessage.Content!.Headers.ContentType!.MediaType);
        var field = call.RequestBody.Split('=', 2);
        Assert.Equal("params", field[0]);
        var plaintext = System.Text.Encoding.UTF8.GetString(NeteaseCrypto.DecodeEapi(Convert.FromHexString(WebUtility.UrlDecode(field[1]))));
        using var json = JsonDocument.Parse(plaintext.Split("-36cd479b6b5-")[1]);
        Assert.Equal(3, json.RootElement.GetProperty("type").GetInt32());
        Assert.False(json.RootElement.GetProperty("e_r").GetBoolean());
        Assert.Equal(Context.DeviceId, json.RootElement.GetProperty("header").GetProperty("deviceId").GetString());
        var client = clients.Web;
        clients.Dispose();
        Assert.True(client.IsDisposed);
    }

    [Theory]
    [InlineData(800, QrStatus.Expired)]
    [InlineData(801, QrStatus.WaitingForScan)]
    [InlineData(802, QrStatus.WaitingForConfirmation)]
    [InlineData(803, QrStatus.Authorized)]
    [Trait("Scenario", "H02,H11")]
    public async Task HTTP成功与扫码业务状态分开解释(int code, QrStatus expected)
    {
        using var http = new HttpTest();
        http.RespondWithJson(new { code });
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        Assert.Equal(expected, (await Api(clients).CheckQrAsync("fixture", Context, default)).Status);
    }

    [Theory]
    [InlineData(429, 429, AuthError.RateLimited)]
    [InlineData(200, 301, AuthError.SessionExpired)]
    [InlineData(200, 460, AuthError.VerificationRequired)]
    [InlineData(503, 503, AuthError.Network)]
    [InlineData(200, 999, AuthError.Protocol)]
    [Trait("Scenario", "H03,H06,H10")]
    public async Task 失败保留状态码而不泄露敏感正文(int status, int code, AuthError expected)
    {
        using var http = new HttpTest();
        http.RespondWithJson(new { code, message = "MUSIC_U=secret-fixture" }, status);
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        var error = await Assert.ThrowsAsync<AuthException>(() => Api(clients).CheckQrAsync("secret-key", Context, default));
        Assert.Equal(expected, error.Kind);
        Assert.Equal(status, error.HttpStatus);
        Assert.Equal(code, error.BusinessCode);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Null(error.InnerException);
        Assert.Single(http.CallLog);
    }

    [Fact]
    [Trait("Scenario", "H04,H09,H12")]
    public async Task 超时取消和重定向都不会误报成功()
    {
        using var http = new HttpTest();
        http.SimulateTimeout();
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        Assert.False(clients.Web.Settings.Redirects.Enabled);
        var timeout = await Assert.ThrowsAsync<AuthException>(() => Api(clients).CreateKeyAsync(Context, default));
        Assert.Equal(AuthError.Timeout, timeout.Kind);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Api(clients).CreateKeyAsync(Context, cts.Token));
        Assert.Single(http.CallLog);
    }

    [Fact]
    [Trait("Scenario", "H07,H08")]
    public void 响应Cookie按标准属性合并且旧快照不被修改()
    {
        var now = DateTimeOffset.UtcNow;
        var context = Context with { Cookies = ImmutableDictionary<string, SessionCookie>.Empty.Add("MUSIC_U", new("old")) };
        var next = NeteaseCookies.Merge(context, new Uri("https://interfacepc.music.163.com/eapi/test"),
            ["MUSIC_U=new; Path=/; Domain=.music.163.com; Expires=Wed, 01 Jan 2031 00:00:00 GMT", "__csrf=token; Path=/"], now);
        Assert.Equal("old", context.Cookie("MUSIC_U", now));
        Assert.Equal("new", next.Cookie("MUSIC_U", now));
        Assert.Equal("token", next.Cookie("__csrf", now));
        Assert.NotNull(next.Cookies["MUSIC_U"].Expires);
        var cleared = NeteaseCookies.Merge(next, new Uri("https://interfacepc.music.163.com/eapi/test"),
            ["MUSIC_U=; Max-Age=0; Path=/; Domain=.music.163.com"], now);
        Assert.False(cleared.HasAccount(now));
        Assert.Equal("new", next.Cookie("MUSIC_U", now));
    }

    [Theory]
    [InlineData("{\"code\":200,\"account\":null,\"profile\":null}", AuthError.SessionExpired)]
    [InlineData("{\"code\":200,\"account\":{\"id\":1},\"profile\":{\"userId\":2,\"nickname\":\"x\"}}", AuthError.Protocol)]
    [InlineData("{\"code\":200}", AuthError.SessionExpired)]
    [InlineData("{}", AuthError.Protocol)]
    [InlineData("{\"code\":200,\"account\":[],\"profile\":{}}", AuthError.Protocol)]
    [InlineData("{\"code\":200,\"account\":{\"id\":\"secret\"},\"profile\":{}}", AuthError.Protocol)]
    [Trait("Scenario", "P12")]
    public async Task 无效账号不能成为登录成功(string body, AuthError kind)
    {
        using var http = new HttpTest();
        http.RespondWith(body);
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        var error = await Assert.ThrowsAsync<AuthException>(() => Api(clients).CheckAccountAsync(Context, default));
        Assert.Equal(kind, error.Kind);
    }

    [Fact]
    [Trait("Scenario", "P05,P12")]
    public async Task 账号支持长ID和新增字段且不要求Node包装()
    {
        using var http = new HttpTest();
        http.RespondWithJson(new { code = "200", account = new { id = 12345678901234L }, profile = new { userId = 12345678901234L, nickname = "音乐", future = true } });
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        var result = await Api(clients).CheckAccountAsync(Context, default);
        Assert.Equal(12345678901234L, result.Account.Id);
        Assert.Equal("音乐", result.Account.Nickname);
        Assert.Equal("https://music.163.com/weapi/w/nuser/account/get", Assert.Single(http.CallLog).Request.Url.ToString());
    }

    [Fact]
    public async Task 超大响应拒绝且读取取消生效()
    {
        using var stream = new MemoryStream(new byte[16]);
        await Assert.ThrowsAsync<AuthException>(() => NeteaseTransport.ReadBoundedAsync(stream, 8, default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        stream.Position = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NeteaseTransport.ReadBoundedAsync(stream, 32, cancellation.Token));
    }

    [Fact]
    [Trait("Scenario", "H07")]
    public void 未更新Cookie保留期限而新会话Cookie不继承旧期限()
    {
        var now = DateTimeOffset.UtcNow;
        var context = Context with { Cookies = ImmutableDictionary<string, SessionCookie>.Empty.Add("MUSIC_U", new("old", now.AddDays(1))) };
        var uri = new Uri("https://interfacepc.music.163.com/eapi/test");
        var unchanged = NeteaseCookies.Merge(context, uri, [], now);
        Assert.Equal(context.Cookies["MUSIC_U"].Expires, unchanged.Cookies["MUSIC_U"].Expires);
        var updated = NeteaseCookies.Merge(context, uri, ["MUSIC_U=new; Path=/; Domain=.music.163.com"], now);
        Assert.Null(updated.Cookies["MUSIC_U"].Expires);
        Assert.Equal("new", updated.Cookie("MUSIC_U", now));
    }

    [Fact]
    [Trait("Scenario", "H05,H09,U02")]
    public async Task 限流保留重试间隔且头像请求不携带账号凭据()
    {
        using var http = new HttpTest();
        http.RespondWith("", 429, new { Retry_After = "7" });
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        var error = await Assert.ThrowsAsync<AuthException>(() => Api(clients).CreateKeyAsync(Context, default));
        Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
        http.RespondWith("image");
        var images = new AccountImageSource(clients);
        Assert.NotNull(await images.LoadAsync("http://p1.music.126.net/fixture.jpg", default));
        var call = http.CallLog.Last();
        Assert.StartsWith("https://p1.music.126.net/", call.Request.Url.ToString());
        Assert.False(call.HttpRequestMessage.Headers.Contains("Cookie"));
        Assert.DoesNotContain("csrf", call.Request.Url.ToString());
        Assert.Null(await images.LoadAsync("https://p1.music.126.net.evil.invalid/a", default));
        Assert.Equal(2, http.CallLog.Count);
        http.RespondWith("", 302, new { Location = "https://other.invalid/a" });
        Assert.Null(await images.LoadAsync("https://p1.music.126.net/other", default));
        Assert.Equal(3, http.CallLog.Count);
    }
}
