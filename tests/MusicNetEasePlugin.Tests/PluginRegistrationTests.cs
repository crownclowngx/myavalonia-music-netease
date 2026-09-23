using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MusicNetEasePlugin.Constants;
using MusicNetEasePlugin.Features.Main;
using MusicNetEasePlugin.Features.Settings;
using MusicNetEasePlugin.Plugin;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace MusicNetEasePlugin.Tests;

/// <summary>
/// 验证 SDK 的注册所有权契约，而不只验证 Microsoft DI 能否建立容器。
/// Host 在 Configure 返回后才追加贡献根；测试替身也分开收集两类描述符，
/// 避免提前把 Tool 放进私有集合，掩盖真实 Host 的启动拒绝。
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class PluginRegistrationTests
{
    [Fact, Trait("V5", "P03,U03")]
    public void 真实模块只声明贡献根而不在私有服务中重复登记()
    {
        var registration = Configure();
        Assert.Equal(3, registration.Contributions.Count);
        Assert.Contains(registration.Contributions, x => x.ServiceType == typeof(MainDocument) && x.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(registration.Contributions, x => x.ServiceType == typeof(MusicSettingsTool) && x.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(registration.Contributions, x => typeof(IPluginLifecycle).IsAssignableFrom(x.ServiceType));
        var roots = registration.Contributions.Select(x => x.ServiceType).ToHashSet();
        Assert.DoesNotContain(registration.Services, x => roots.Contains(x.ServiceType));
        Assert.DoesNotContain(registration.Services, x => x.ServiceType == typeof(IDocumentLifetime));
    }

    [Fact, Trait("V5", "P03,U03")]
    public async Task 宿主追加贡献根后不同Document共用设置且关闭页面保留草稿()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            using var directory = new TestDirectory();
            var registration = Configure();
            // 同一私有组合入口只替换临时数据根，避免测试触及真实用户账号或播放缓存。
            // Tool 和 Document 描述符来自真实模块声明，随后按 SDK 协议由宿主侧追加。
            var services = new ServiceCollection().AddMusicNetEasePluginServices(directory.Path);
            foreach (var contribution in registration.Contributions) services.Add(contribution);
            services.AddScoped<IDocumentLifetime, TestLifetime>();
            // 真实共享播放器含异步关闭链；UI 线程必须让出消息循环，不能同步阻塞其尾任务。
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            var settings = provider.GetRequiredService<MusicSettingsTool>();
            settings.DirectoryPath = "尚未保存的运行库草稿";
            using (var first = provider.CreateScope())
            using (var second = provider.CreateScope())
            {
                var firstPage = first.ServiceProvider.GetRequiredService<MainDocument>();
                var secondPage = second.ServiceProvider.GetRequiredService<MainDocument>();
                Assert.NotSame(firstPage, secondPage);
                Assert.Same(settings, firstPage.Settings);
                Assert.Same(settings, secondPage.Settings);
                Assert.NotSame(firstPage.Music, secondPage.Music);
            }
            using var reopened = provider.CreateScope();
            var page = reopened.ServiceProvider.GetRequiredService<MainDocument>();
            Assert.Same(settings, page.Settings);
            Assert.Equal("尚未保存的运行库草稿", page.Settings!.DirectoryPath);
            return true;
        }, default).WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static Registration Configure()
    {
        var registration = new Registration();
        new MusicNetEasePluginModule().Configure(registration);
        return registration;
    }

    /// <summary>只收集公开 SDK 声明，不复制 Host 内部实现，也不提前登记贡献根。</summary>
    private sealed class Registration : IPluginRegistration, IPluginIconRegistration
    {
        public PluginId PluginId => PluginIds.Plugin;
        public IServiceCollection Services { get; } = new ServiceCollection();
        public List<ServiceDescriptor> Contributions { get; } = [];
        public void UseLifecycle<T>() where T : class, IPluginLifecycle => Contributions.Add(ServiceDescriptor.Singleton(typeof(T), typeof(T)));
        public void AddDocument<T, TView>(DocumentDescriptor descriptor) where T : class, IPluginDocument where TView : Control, new()
            => Contributions.Add(ServiceDescriptor.Scoped(typeof(T), typeof(T)));
        public void AddPersistableDocument<T, TView>(DocumentDescriptor descriptor) where T : class, IPersistablePluginDocument where TView : Control, new()
            => Contributions.Add(ServiceDescriptor.Scoped(typeof(T), typeof(T)));
        public void AddTool<T, TView>(ToolDescriptor descriptor) where T : class where TView : Control, new()
            => Contributions.Add(ServiceDescriptor.Singleton(typeof(T), typeof(T)));
        public string AddIcon(string localName, VectorIconDefinition definition) => $"plugin:{PluginId.Value}/{localName}";
    }

    private sealed class TestLifetime : IDocumentLifetime, IDisposable
    {
        private readonly CancellationTokenSource _closing = new();
        public CancellationToken ClosingToken => _closing.Token;
        public bool IsClosing => _closing.IsCancellationRequested;
        public void Dispose() { _closing.Cancel(); _closing.Dispose(); }
    }
}
