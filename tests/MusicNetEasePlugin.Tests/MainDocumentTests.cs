using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Features.Main;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class MainDocumentTests
{
    private sealed class Lifetime : IDocumentLifetime, IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        public CancellationToken ClosingToken => _source.Token;
        public bool IsClosing => _source.IsCancellationRequested;
        public void Close() => _source.Cancel();
        public void Dispose() => _source.Dispose();
    }
    private sealed class Dispatcher : ILoginUiDispatcher { public void Post(Action action) => action(); }
    private sealed class Images : IAccountImageSource
    {
        public Task<byte[]?> LoadAsync(string? address, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }

    [Fact]
    [Trait("Scenario", "U01,U06")]
    public async Task 初始化保留Host标题且未登录页面可开始扫码()
    {
        await using var login = new LoginCoordinator(new FakeAuthApi(), new MemorySessionStore(), new FakeTimeProvider(), LoginOptions.Default);
        using var lifetime = new Lifetime();
        using var document = new MainDocument(login, new Dispatcher(), new Images(), lifetime);
        await document.InitializeAsync(new NewDocumentActivation("测试标题"), default);
        await login.WaitForIdleAsync();
        Assert.Equal("测试标题", document.Presentation.Title);
        Assert.False(document.IsSignedIn);
        Assert.True(document.StartLoginCommand.CanExecute(null));
        Assert.False(document.CancelLoginCommand.CanExecute(null));
    }

    [Fact]
    [Trait("Scenario", "U01,U03,U04,U06")]
    public async Task 两个页面共享登录而只有拥有者可取消其尝试()
    {
        var time = new FakeTimeProvider();
        var api = new FakeAuthApi();
        await using var login = new LoginCoordinator(api, new MemorySessionStore(), time, LoginOptions.Default);
        using var firstLifetime = new Lifetime();
        using var secondLifetime = new Lifetime();
        using var first = new MainDocument(login, new Dispatcher(), new Images(), firstLifetime);
        using var second = new MainDocument(login, new Dispatcher(), new Images(), secondLifetime);
        var run = first.StartLoginCommand.ExecuteAsync(null);
        Assert.NotNull(first.QrImageBytes);
        Assert.NotNull(second.QrImageBytes);
        Assert.True(first.CancelLoginCommand.CanExecute(null));
        Assert.False(second.CancelLoginCommand.CanExecute(null));
        secondLifetime.Close();
        Assert.Equal(LoginStage.WaitingForScan, login.Snapshot.Stage);
        firstLifetime.Close();
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(LoginStage.Cancelled, login.Snapshot.Stage);
        Assert.Equal(0, api.Checks);
        Assert.False(first.StartLoginCommand.CanExecute(null));
    }

    [Fact]
    [Trait("Scenario", "U03,U06")]
    public async Task 关闭页面不退出已提交的共享账号()
    {
        var store = new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()) };
        await using var login = new LoginCoordinator(new FakeAuthApi(), store, TimeProvider.System, LoginOptions.Default);
        using var lifetime = new Lifetime();
        using var document = new MainDocument(login, new Dispatcher(), new Images(), lifetime);
        await document.InitializeAsync(new NewDocumentActivation("账号"), default);
        await login.WaitForIdleAsync();
        Assert.True(document.IsSignedIn);
        Assert.Equal("测试账号", document.AccountName);
        lifetime.Close();
        document.Dispose();
        Assert.NotNull(login.Snapshot.Account);
        Assert.NotNull(store.Saved);
    }

    [Fact]
    [Trait("Scenario", "U06")]
    public async Task 已取消的Document初始化不发起网络请求()
    {
        var api = new FakeAuthApi();
        await using var login = new LoginCoordinator(api, new MemorySessionStore(), TimeProvider.System, LoginOptions.Default);
        using var lifetime = new Lifetime();
        using var document = new MainDocument(login, new Dispatcher(), new Images(), lifetime);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await document.InitializeAsync(new NewDocumentActivation("未创建"), cancellation.Token));
        Assert.Equal(0, api.Accounts);
        Assert.Equal(0, api.Keys);
    }
}
