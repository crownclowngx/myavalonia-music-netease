using Flurl;
using Flurl.Http.Configuration;
using Flurl.Http.Testing;
using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Infrastructure.Protocol;
using Xunit;

namespace MusicNetEasePlugin.Tests;

/// <summary>微信边界测试使用真实 Flurl 请求和生产解析器，只替换网络响应；不会发送真实授权票据。</summary>
public sealed class WeChatLoginTests
{
    private const string State = "fixture state+&/=";
    private const string Uuid = "fixture_uuid";
    private static readonly byte[] Png = LoginQrCode.Render("fixture");
    private static string Authorization(string callback = WeChatLoginProtocol.Callback) =>
        new Url("https://open.weixin.qq.com/connect/qrconnect")
            .SetQueryParams(new { appid = "fixture_app", redirect_uri = callback, scope = "snsapi_login", response_type = "code", state = State });

    private static void Prepare(HttpTest http, bool modern = true)
    {
        http.RespondWith("", 302, new Dictionary<string, string>
        {
            ["Location"] = Authorization(), ["Set-Cookie"] = "NETEASE_NONCE=nonce-fixture; Path=/; Secure; HttpOnly"
        });
        http.RespondWith($"<img class='logo' src='/logo.png'><img class='qrcode' src='/connect/qrcode/{Uuid}'><script>window.usenewdomain = {modern.ToString().ToLowerInvariant()};</script>");
        http.RespondWith(() => new ByteArrayContent(Png));
    }

    private static void Confirm(HttpTest http) => http.RespondWith("window.wx_errcode=405;window.wx_code='code_fixture';");

    [Fact]
    [Trait("Scenario", "W01,W02,W03,W04")]
    public async Task 微信扫码确认回调合并会话并严格隔离域Cookie()
    {
        using var http = new HttpTest();
        Prepare(http);
        http.RespondWith("window.wx_errcode=404;");
        http.RespondWith("window.wx_errcode=408;");
        Confirm(http);
        http.RespondWith("ok", cookies: new { MUSIC_U = "session-fixture", __csrf = "csrf-fixture" });
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        using var attempt = await new WeChatQrLoginProvider(clients, TimeProvider.System)
            .CreateAsync(FakeAuthApi.Authorized(AuthContext.Create()), default);
        Assert.Equal(Png, attempt.Image);
        Assert.Equal(QrStatus.WaitingForConfirmation, (await attempt.CheckAsync(default)).Status);
        Assert.Equal(QrStatus.WaitingForConfirmation, (await attempt.CheckAsync(default)).Status);
        var authorized = await attempt.CheckAsync(default);
        Assert.Equal(QrStatus.Authorized, authorized.Status);
        Assert.Equal("session-fixture", authorized.Context.Cookie("MUSIC_U", DateTimeOffset.UtcNow));
        Assert.Equal("csrf-fixture", authorized.Context.Cookie("__csrf", DateTimeOffset.UtcNow));
        Assert.Equal(authorized, await attempt.CheckAsync(default));
        Assert.Equal(7, http.CallLog.Count); // 已完成尝试再次读取不能重复兑换一次性 code。
        Assert.False(http.CallLog[0].HttpRequestMessage.Headers.Contains("Cookie"));
        var callback = http.CallLog.Last().Request;
        Assert.Equal(WeChatLoginProtocol.Callback, callback.Url.ToString().Split('?')[0]);
        Assert.Equal(State, callback.Url.QueryParams.FirstOrDefault("state"));
        Assert.Equal("code_fixture", callback.Url.QueryParams.FirstOrDefault("code"));
        Assert.Contains("NETEASE_NONCE=nonce-fixture", callback.Headers.FirstOrDefault("Cookie"));
        Assert.DoesNotContain("test-cookie", callback.Headers.FirstOrDefault("Cookie"));
        foreach (var call in http.CallLog.Where(call => new Uri(call.Request.Url).Host.EndsWith("weixin.qq.com", StringComparison.Ordinal)))
            Assert.False(call.HttpRequestMessage.Headers.Contains("Cookie"));
        Assert.Equal("404", http.CallLog[5].Request.Url.QueryParams.FirstOrDefault("last")?.ToString());
        Assert.Equal(TimeSpan.FromSeconds(35), http.CallLog[3].Request.Settings.Timeout);
        Assert.False(clients.Social.Settings.Redirects.Enabled);
        Assert.False(clients.WeChat.Settings.Redirects.Enabled);
    }

    [Theory]
    [InlineData(408, QrStatus.WaitingForScan)]
    [InlineData(404, QrStatus.WaitingForConfirmation)]
    [InlineData(403, QrStatus.Denied)]
    [InlineData(402, QrStatus.Expired)]
    [Trait("Scenario", "W02")]
    public async Task 微信状态不会误用网易App业务码(int code, QrStatus expected)
    {
        using var http = new HttpTest();
        Prepare(http, modern: false);
        http.RespondWith($"window.wx_errcode={code};");
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        using var attempt = await new WeChatQrLoginProvider(clients, TimeProvider.System).CreateAsync(AuthContext.Create(), default);
        var check = await attempt.CheckAsync(default);
        Assert.Equal(expected, check.Status);
        Assert.False(check.Context.HasAccount(DateTimeOffset.UtcNow));
        Assert.Equal("long.open.weixin.qq.com", new Uri(http.CallLog.Last().Request.Url).Host);
        Assert.Equal(4, http.CallLog.Count);
    }

    [Fact]
    [Trait("Scenario", "W03,W05")]
    public async Task 微信确认后仍须通过网易账号核验才能保存()
    {
        using var http = new HttpTest();
        Prepare(http);
        Confirm(http);
        http.RespondWith("", cookies: new { MUSIC_U = "candidate-fixture" });
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        var entered = new TaskCompletionSource<AuthContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AccountCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeAuthApi { Account = (ctx, _) => { entered.SetResult(ctx); return release.Task; } };
        var store = new MemorySessionStore();
        var time = new FakeTimeProvider();
        await using var login = new LoginCoordinator(api, store, time, LoginOptions.Default,
            [new WeChatQrLoginProvider(clients, time), new NeteaseAppQrLoginProvider(api)]);
        var run = login.StartAsync(Guid.NewGuid(), default);
        Assert.Equal(LoginMethod.WeChat, login.Snapshot.Method);
        Assert.Equal(LoginStage.WaitingForScan, login.Snapshot.Stage);
        time.Advance(TimeSpan.FromSeconds(2));
        var candidate = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(LoginStage.Verifying, login.Snapshot.Stage);
        Assert.Null(login.Snapshot.Account);
        Assert.Equal(0, store.Saves);
        Assert.Equal(0, api.Keys);
        Assert.Equal(0, api.Checks);
        release.SetResult(new(new(123, "已核验", null), candidate));
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(LoginStage.SignedIn, login.Snapshot.Stage);
        Assert.Equal(1, store.Saves);
        Assert.Equal(1, api.Accounts);
        Assert.Null(login.Snapshot.QrImage);
    }

    [Fact]
    [Trait("Scenario", "W03,W06")]
    public async Task 网易回调同源跳转后才能取得Cookie()
    {
        using var http = new HttpTest();
        Prepare(http);
        Confirm(http);
        http.RespondWith("", 302, new { Location = "/callback/complete" });
        http.RespondWith("", cookies: new { MUSIC_U = "session-fixture" });
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        using var attempt = await new WeChatQrLoginProvider(clients, TimeProvider.System).CreateAsync(AuthContext.Create(), default);
        Assert.Equal(QrStatus.Authorized, (await attempt.CheckAsync(default)).Status);
        Assert.Equal("https://music.163.com/callback/complete", http.CallLog.Last().Request.Url.ToString());
    }

    [Theory]
    [InlineData("https://evil.invalid/callback")]
    [InlineData("http://music.163.com/callback")]
    [InlineData("https://music.163.com.evil.invalid/callback")]
    [InlineData("https://music.163.com:444/callback")]
    [Trait("Scenario", "W06")]
    public async Task 回调拒绝外域明文和异常端口跳转(string location)
    {
        using var http = new HttpTest();
        Prepare(http);
        Confirm(http);
        http.RespondWith("", 302, new { Location = location });
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        using var attempt = await new WeChatQrLoginProvider(clients, TimeProvider.System).CreateAsync(AuthContext.Create(), default);
        var error = await Assert.ThrowsAsync<AuthException>(() => attempt.CheckAsync(default));
        Assert.Equal(AuthError.Protocol, error.Kind);
        Assert.Equal(5, http.CallLog.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Scenario", "W07")]
    public async Task 回调失败或需绑定时明确终止且不会重放授权码(bool timeout)
    {
        using var http = new HttpTest();
        Prepare(http);
        Confirm(http);
        if (timeout) http.SimulateTimeout();
        else http.RespondWith("<html>需要绑定账号 MUSIC_U=secret-fixture</html>");
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        using var attempt = await new WeChatQrLoginProvider(clients, TimeProvider.System).CreateAsync(AuthContext.Create(), default);
        var error = await Assert.ThrowsAsync<AuthException>(() => attempt.CheckAsync(default));
        Assert.Equal(timeout ? AuthError.Protocol : AuthError.VerificationRequired, error.Kind);
        Assert.DoesNotContain("secret-fixture", error.ToString());
        Assert.Null(error.InnerException);
        await Assert.ThrowsAsync<AuthException>(() => attempt.CheckAsync(default));
        Assert.Equal(5, http.CallLog.Count);
    }

    [Fact]
    [Trait("Scenario", "W04,W08")]
    public async Task 取消与释放后不能继续发送授权请求()
    {
        using var http = new HttpTest();
        Prepare(http);
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        var provider = new WeChatQrLoginProvider(clients, TimeProvider.System);
        using var attempt = await provider.CreateAsync(AuthContext.Create(), default);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt.CheckAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CreateAsync(AuthContext.Create(), cancellation.Token));
        attempt.Dispose();
        Assert.Empty(attempt.Image);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => attempt.CheckAsync(default));
        Assert.Equal(3, http.CallLog.Count);
    }

    [Theory]
    [InlineData("window.wx_errcode=405;")]
    [InlineData("window.wx_errcode=405;window.wx_code='secret';evil();")]
    [InlineData("window.wx_errcode=405;window.wx_code='quote\\'injection';")]
    [InlineData("<html>captcha</html>")]
    [Trait("Scenario", "W06,W07")]
    public void 脚本只读取有限赋值且不执行任意JavaScript(string script)
    {
        var error = Assert.Throws<AuthException>(() => WeChatLoginProtocol.ParsePoll(script));
        Assert.Equal(AuthError.Protocol, error.Kind);
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    [Trait("Scenario", "W06")]
    public void 授权入口和二维码来源严格限定官方目标()
    {
        Assert.Throws<AuthException>(() => WeChatLoginProtocol.ParseAuthorization(Authorization("https://evil.invalid/")));
        Assert.Throws<AuthException>(() => WeChatLoginProtocol.ParseAuthorization(Authorization().Replace("open.weixin.qq.com", "evil.invalid")));
        Assert.Throws<AuthException>(() => WeChatLoginProtocol.ParseAuthorization(Authorization() + "&state=duplicate"));
        Assert.Throws<AuthException>(() => WeChatLoginProtocol.ParsePage("<img src='https://evil.invalid/connect/qrcode/fixture'>"));
        Assert.Throws<AuthException>(() => WeChatLoginProtocol.ParsePage("<img src='https://open.weixin.qq.com/connect/qrcode/fixture?redirect=1'>"));
        Assert.Equal(State, WeChatLoginProtocol.ParseAuthorization(Authorization()).State);
    }

    private sealed class WaitingHandler : DelegatingHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("测试请求不能进入真实网络。");
        }
    }

    [Fact]
    [Trait("Scenario", "W08")]
    public async Task 用户取消能够中止正在等待的微信长轮询()
    {
        var handler = new WaitingHandler();
        var cache = new FlurlClientCache().Add("netease-wechat-poll", configure: builder => builder.AddMiddleware(() => handler));
        using var clients = new NeteaseFlurlClients(cache);
        using var cancellation = new CancellationTokenSource();
        IQrLoginAttempt attempt;
        using (var http = new HttpTest())
        {
            Prepare(http);
            attempt = await new WeChatQrLoginProvider(clients, TimeProvider.System).CreateAsync(AuthContext.Create(), default);
        }
        using (attempt)
        {
            var check = attempt.CheckAsync(cancellation.Token);
            await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Null(error.InnerException);
        }
    }

    [Theory]
    [InlineData("window.wx_errcode=500;")]
    [InlineData("window.wx_errcode=405;")]
    [Trait("Scenario", "W07")]
    public async Task 未知状态和缺少授权码不能请求网易回调(string script)
    {
        using var http = new HttpTest();
        Prepare(http);
        http.RespondWith(script);
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        using var attempt = await new WeChatQrLoginProvider(clients, TimeProvider.System).CreateAsync(AuthContext.Create(), default);
        var error = await Assert.ThrowsAsync<AuthException>(() => attempt.CheckAsync(default));
        Assert.Equal(AuthError.Protocol, error.Kind);
        Assert.Equal(4, http.CallLog.Count);
    }

    [Fact]
    [Trait("Scenario", "W04")]
    public async Task 同一Provider的两个尝试不共享Cookie()
    {
        using var http = new HttpTest();
        Prepare(http);
        Prepare(http);
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        var provider = new WeChatQrLoginProvider(clients, TimeProvider.System);
        using var first = await provider.CreateAsync(AuthContext.Create(), default);
        using var second = await provider.CreateAsync(AuthContext.Create(), default);
        Assert.False(http.CallLog[3].HttpRequestMessage.Headers.Contains("Cookie"));
        Assert.NotSame(first, second);
    }
}
