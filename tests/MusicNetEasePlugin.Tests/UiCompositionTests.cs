using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Flurl.Http.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Features.Main;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Plugin;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace MusicNetEasePlugin.Tests;

/// <summary>直接在专用 UI 线程渲染生产 View；测试不另造一个与真实绑定脱离的窗口。</summary>
public sealed class UiCompositionTests
{
    public sealed class TestApp : Avalonia.Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    private sealed class Lifetime : IDocumentLifetime, IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        public CancellationToken ClosingToken => _source.Token;
        public bool IsClosing => _source.IsCancellationRequested;
        public void Dispose() { _source.Cancel(); _source.Dispose(); }
    }
    private sealed class Images : IAccountImageSource
    {
        public Task<byte[]?> LoadAsync(string? address, CancellationToken cancellationToken) => Task.FromResult<byte[]?>([1, 2, 3]);
    }

    [Fact]
    [Trait("Scenario", "P09,U01,U02,U04,U05")]
    public async Task 实际页面绑定二维码和账号并可卸载重建()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiCompositionTests));
        await session.Dispatch(async () =>
        {
            var time = new FakeTimeProvider();
            var api = new FakeAuthApi { Account = (ctx, _) => Task.FromResult(new AccountCheck(new(123, "测试账号", "https://p1.music.126.net/avatar"), ctx)) };
            await using var login = TestLogin.Create(api, new MemorySessionStore(), time, LoginOptions.Default);
            using var lifetime = new Lifetime();
            using var document = new MainDocument(login, new Infrastructure.Ui.LoginUiDispatcher(), new Images(), lifetime);
            var view = new MainView { DataContext = document };
            var window = new Window { Width = 900, Height = 820, Content = view };
            try
            {
                window.Show();
                await document.InitializeAsync(new NewDocumentActivation("网易云音乐"), default);
                await login.WaitForIdleAsync();
                var run = document.StartLoginCommand.ExecuteAsync(null);
                Assert.NotNull(view.FindControl<Image>("QrImage")!.Source);
                Assert.True(view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "取消")).IsEnabled);
                var artifactRoot = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS");
                if (!string.IsNullOrEmpty(artifactRoot))
                {
                    Directory.CreateDirectory(artifactRoot);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(artifactRoot, "login-page.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                // Dock 可以临时拆卸视图，只有 Document lifetime 才有资格取消登录。
                window.Content = null;
                Assert.Null(view.FindControl<Image>("QrImage")!.Source);
                Assert.Equal(LoginStage.WaitingForScan, login.Snapshot.Stage);
                window.Content = view;
                Assert.NotNull(view.FindControl<Image>("QrImage")!.Source);
                time.Advance(TimeSpan.FromSeconds(2));
                await run;
                // 登录任务完成不等于 UI 已消费跨线程 Post；显式排空较高优先级的状态投影。
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.True(document.IsSignedIn);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "测试账号");
                Assert.Null(view.FindControl<Image>("QrImage")!.Source);
                Assert.Null(view.FindControl<Image>("AvatarImage")!.Source); // 无效图片保留占位图。
            }
            finally { window.Close(); }
            return true;
        }, default).WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    [Trait("Scenario", "H01,L11,U05,A01")]
    public void 两个容器各自复用服务且同步Scope释放会回收Client()
    {
        var firstServices = new ServiceCollection().AddMusicNetEasePluginServices(Path.GetTempPath());
        using var first = firstServices.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var second = new ServiceCollection().AddMusicNetEasePluginServices(Path.GetTempPath()).BuildServiceProvider();
        var firstLogin = first.GetRequiredService<LoginCoordinator>();
        Assert.Same(firstLogin, first.GetRequiredService<LoginCoordinator>());
        Assert.NotSame(firstLogin, second.GetRequiredService<LoginCoordinator>());
        var client = first.GetRequiredService<NeteaseFlurlClients>().Web;
        Assert.NotSame(client, second.GetRequiredService<NeteaseFlurlClients>().Web);
        Assert.NotSame(first.GetRequiredService<IFlurlClientCache>(), second.GetRequiredService<IFlurlClientCache>());
        first.Dispose();
        Assert.True(client.IsDisposed);
    }
}
