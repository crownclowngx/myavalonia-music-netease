using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Authentication;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class LoginCoordinatorTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(2);
    private static LoginCoordinator Create(FakeAuthApi api, MemorySessionStore store, FakeTimeProvider time) =>
        TestLogin.Create(api, store, time, LoginOptions.Default);

    private static Task Stage(LoginCoordinator login, LoginStage stage)
    {
        if (login.Snapshot.Stage == stage) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<LoginSnapshot>? listener = null;
        listener = (_, state) =>
        {
            if (state.Stage != stage) return;
            login.Changed -= listener;
            completion.TrySetResult();
        };
        login.Changed += listener;
        if (login.Snapshot.Stage == stage) { login.Changed -= listener; return Task.CompletedTask; }
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    [Trait("Scenario", "L01,L07")]
    public async Task 扫码确认后验证账号并安全保存一次()
    {
        var time = new FakeTimeProvider();
        var api = new FakeAuthApi();
        api.States.Enqueue(QrStatus.WaitingForConfirmation);
        api.States.Enqueue(QrStatus.Authorized);
        var store = new MemorySessionStore();
        await using var login = Create(api, store, time);
        var run = login.StartAsync(Owner, default, LoginMethod.NeteaseApp);
        Assert.Equal(LoginStage.WaitingForScan, login.Snapshot.Stage);
        var confirming = Stage(login, LoginStage.WaitingForConfirmation);
        time.Advance(Step);
        await confirming;
        Assert.Null(login.Snapshot.Account);
        time.Advance(Step);
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(LoginStage.SignedIn, login.Snapshot.Stage);
        Assert.True(login.Snapshot.Remembered);
        Assert.Null(login.Snapshot.QrImage);
        Assert.Equal(1, api.Accounts);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    [Trait("Scenario", "L02")]
    public async Task 缺少授权Cookie时不能调用账号或保存()
    {
        var time = new FakeTimeProvider();
        var api = new FakeAuthApi { Check = (_, context, _) => Task.FromResult(new QrCheck(QrStatus.Authorized, context)) };
        var store = new MemorySessionStore();
        await using var login = Create(api, store, time);
        var run = login.StartAsync(Owner, default, LoginMethod.NeteaseApp);
        time.Advance(Step);
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(LoginStage.Failed, login.Snapshot.Stage);
        Assert.Equal(0, api.Accounts);
        Assert.Null(store.Saved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Scenario", "L03")]
    public async Task 服务端过期或本地等待预算结束均停止轮询(bool serverExpiry)
    {
        var time = new FakeTimeProvider();
        var api = new FakeAuthApi();
        api.States.Enqueue(QrStatus.Expired);
        await using var login = Create(api, new(), time);
        var run = login.StartAsync(Owner, default, LoginMethod.NeteaseApp);
        time.Advance(serverExpiry ? Step : TimeSpan.FromMinutes(3));
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(LoginStage.Expired, login.Snapshot.Stage);
        Assert.Equal(1, api.Keys);
        Assert.Null(login.Snapshot.QrImage);
    }

    [Fact]
    [Trait("Scenario", "L04,L05,L06,H08")]
    public async Task 旧扫码响应晚到不能覆盖新尝试()
    {
        var time = new FakeTimeProvider();
        var pending = new TaskCompletionSource<QrCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<AuthContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeAuthApi { Check = (_, ctx, _) => { entered.TrySetResult(ctx); return pending.Task; } };
        var store = new MemorySessionStore();
        await using var login = Create(api, store, time);
        var old = login.StartAsync(Owner, default, LoginMethod.NeteaseApp);
        time.Advance(Step);
        var oldContext = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var current = login.StartAsync(Owner, default, LoginMethod.NeteaseApp);
        var waiting = Stage(login, LoginStage.WaitingForScan);
        pending.SetResult(new(QrStatus.Authorized, FakeAuthApi.Authorized(oldContext)));
        await old.WaitAsync(TimeSpan.FromSeconds(3));
        await waiting;
        Assert.Equal(Infrastructure.Protocol.LoginQrCode.Render("key-2"), login.Snapshot.QrImage);
        Assert.Null(store.Saved);
        login.Cancel(Owner);
        await current.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(LoginStage.Cancelled, login.Snapshot.Stage);
    }

    [Fact]
    [Trait("Scenario", "L08,S08")]
    public async Task 提交后取消会补偿清除且退出等待补偿完成()
    {
        var time = new FakeTimeProvider();
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemorySessionStore { AfterCommit = () => { written.SetResult(); return release.Task; } };
        await using var login = Create(new(), store, time);
        var run = login.StartAsync(Owner, default, LoginMethod.NeteaseApp);
        time.Advance(Step);
        await written.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(store.Saved);
        var logout = login.LogoutAsync(Owner, default);
        release.SetResult();
        await Task.WhenAll(run, logout).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(store.Saved);
        Assert.Null(login.Snapshot.Account);
        Assert.Equal(LoginStage.SignedOut, login.Snapshot.Stage);
    }

    [Fact]
    [Trait("Scenario", "L07")]
    public async Task 保存失败仍呈现内存登录并可单独重试保存()
    {
        var time = new FakeTimeProvider();
        var api = new FakeAuthApi();
        var store = new MemorySessionStore { FailSave = true };
        await using var login = Create(api, store, time);
        var run = login.StartAsync(Owner, default, LoginMethod.NeteaseApp);
        time.Advance(Step);
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.NotNull(login.Snapshot.Account);
        Assert.False(login.Snapshot.Remembered);
        store.FailSave = false;
        await login.RetrySaveAsync(Owner, default);
        Assert.True(login.Snapshot.Remembered);
        Assert.Equal(1, api.Keys);
        Assert.Equal(1, api.Accounts);
    }

    [Theory]
    [InlineData(AuthError.Network)]
    [InlineData(AuthError.SessionExpired)]
    [Trait("Scenario", "S03,S04,S05")]
    public async Task 恢复检查失败不会假登录或通过refresh续期(AuthError error)
    {
        var store = new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()) };
        var api = new FakeAuthApi { Account = (_, _) => throw new AuthException(error, "测试恢复失败") };
        await using var login = Create(api, store, new());
        await login.RestoreAsync(Owner, default);
        Assert.Null(login.Snapshot.Account);
        Assert.Equal(LoginStage.Failed, login.Snapshot.Stage);
        Assert.NotNull(store.Saved);
        Assert.Equal(0, api.Keys);
        Assert.Equal(1, api.Accounts);
    }

    [Fact]
    [Trait("Scenario", "S03")]
    public async Task 已有会话只检查账号并同步服务端新会话()
    {
        var store = new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()) };
        var api = new FakeAuthApi();
        await using var login = Create(api, store, new());
        await Task.WhenAll(login.RestoreAsync(Owner, default), login.RestoreAsync(Guid.NewGuid(), default));
        Assert.Equal(LoginStage.SignedIn, login.Snapshot.Stage);
        Assert.Equal(1, api.Accounts);
        Assert.Equal(0, api.Keys);
    }

    [Fact]
    [Trait("Scenario", "L09")]
    public async Task 远端退出失败与本地清理失败分别反馈()
    {
        var store = new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()) };
        var api = new FakeAuthApi { LogoutFailure = new(AuthError.Network, "网络失败") };
        await using var login = Create(api, store, new());
        await login.RestoreAsync(Owner, default);
        await login.LogoutAsync(Owner, default);
        Assert.Null(store.Saved);
        Assert.Equal(LoginStage.SignedOut, login.Snapshot.Stage);
        Assert.Contains("远端退出未确认", login.Snapshot.Message);
        store.FailClear = true;
        await login.LogoutAsync(Owner, default);
        Assert.True(login.Snapshot.CleanupRequired);
        Assert.Equal(LoginStage.Failed, login.Snapshot.Stage);
    }

    [Fact]
    [Trait("Scenario", "L11,L12,U03,U04")]
    public async Task 非拥有者不能取消且停止等待任务结束()
    {
        var time = new FakeTimeProvider();
        var api = new FakeAuthApi();
        var login = Create(api, new(), time);
        var run = login.StartAsync(Owner, default, LoginMethod.NeteaseApp);
        login.Cancel(Guid.NewGuid());
        Assert.Equal(LoginStage.WaitingForScan, login.Snapshot.Stage);
        await login.DisposeAsync();
        Assert.True(run.IsCompleted);
        Assert.Equal(0, api.Checks);
        await login.DisposeAsync();
        await login.StartAsync(Owner, default, LoginMethod.NeteaseApp);
        Assert.Equal(1, api.Keys);
    }

    [Fact]
    [Trait("Scenario", "L12")]
    public async Task 展示订阅者抛错不会阻塞流程收口()
    {
        var time = new FakeTimeProvider();
        await using var login = Create(new(), new(), time);
        login.Changed += (_, _) => throw new InvalidOperationException("fixture");
        var run = login.StartAsync(Owner, default, LoginMethod.NeteaseApp);
        time.Advance(Step);
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(LoginStage.SignedIn, login.Snapshot.Stage);
    }

    [Fact]
    [Trait("Scenario", "W02,W05")]
    public async Task 手机拒绝授权会清除二维码并停止而不是持续等待()
    {
        var time = new FakeTimeProvider();
        var api = new FakeAuthApi();
        api.States.Enqueue(QrStatus.Denied);
        var store = new MemorySessionStore();
        await using var login = Create(api, store, time);
        var run = login.StartAsync(Owner, default);
        time.Advance(Step);
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(LoginStage.Cancelled, login.Snapshot.Stage);
        Assert.Null(login.Snapshot.QrImage);
        Assert.Equal(0, api.Accounts);
        Assert.Equal(0, store.Saves);
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(1, api.Checks);
    }
}
