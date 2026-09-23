using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Flurl.Http.Configuration;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Infrastructure.Persistence;
using MusicNetEasePlugin.Infrastructure.Ui;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Audio;
using MusicNetEasePlugin.Infrastructure.Media;
using MusicNetEasePlugin.Features.Music;

namespace MusicNetEasePlugin.Plugin;

public static class MusicNetEasePluginServices
{
    /// <summary>登记插件自己的业务服务；Standalone 可以复用同一个组合入口。</summary>
    public static IServiceCollection AddMusicNetEasePluginServices(this IServiceCollection services, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var root = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyAvaloniaManagement", "Plugins", "myavalonia.plugin.music.netease");
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(LoginOptions.Default);
        services.TryAddSingleton<IFlurlClientCache, FlurlClientCache>();
        services.TryAddSingleton<NeteaseFlurlClients>();
        services.TryAddSingleton<NeteaseTransport>();
        services.TryAddSingleton<INeteaseAuthApi, NeteaseAuthApi>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IQrLoginProvider, NeteaseAppQrLoginProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IQrLoginProvider, WeChatQrLoginProvider>());
        services.TryAddSingleton<IAccountImageSource, AccountImageSource>();
        services.TryAddSingleton<ISessionProtector, CurrentUserSessionProtector>();
        services.TryAddSingleton<ILoginSessionStore>(provider =>
            new ProtectedLoginSessionStore(Path.GetFullPath(root), provider.GetRequiredService<ISessionProtector>()));
        services.TryAddSingleton<LoginCoordinator>();
        services.TryAddSingleton<ILoginUiDispatcher, LoginUiDispatcher>();
        services.TryAddSingleton<ILibVlcSettingsStore>(_ => new LibVlcSettingsStore(Path.GetFullPath(root)));
        services.TryAddSingleton<ILibVlcDirectoryProbe, LibVlcDirectoryProbe>();
        services.TryAddSingleton(provider => new LibVlcRuntimeResolver(
            provider.GetRequiredService<ILibVlcSettingsStore>(), provider.GetRequiredService<ILibVlcDirectoryProbe>(),
            Path.Combine(Path.GetDirectoryName(typeof(MusicNetEasePluginServices).Assembly.Location)!, "native", "win-x64", "libvlc")));
        services.TryAddSingleton<LibVlcRuntime>();
        services.TryAddSingleton<IPlaybackRuntimeStatus>(provider => provider.GetRequiredService<LibVlcRuntime>());
        services.TryAddSingleton<IMusicSessionAccessor, LoginMusicSession>();
        services.TryAddSingleton<XeapiTransport>();
        services.TryAddSingleton<NeteaseMusicApi>();
        services.TryAddSingleton<IMusicCatalogApi>(provider => provider.GetRequiredService<NeteaseMusicApi>());
        services.TryAddSingleton<IPlaybackResourceResolver>(provider => provider.GetRequiredService<NeteaseMusicApi>());
        services.TryAddSingleton<IMediaBuffer>(provider => new FlurlMediaBuffer(provider.GetRequiredService<NeteaseFlurlClients>(), Path.Combine(root, "media-buffer"), MediaLimits.Default));
        services.TryAddSingleton<IAudioOutput, LibVlcAudioOutput>();
        services.TryAddSingleton<PlaybackCoordinator>();
        // Host 在 Document Scope 中提供 Lifetime；工厂避免普通服务容器预检被要求创建一个虚假的 Document。
        services.TryAddScoped(provider => new MusicWorkspace(provider.GetRequiredService<IMusicCatalogApi>(),
            provider.GetRequiredService<IMusicSessionAccessor>(), provider.GetRequiredService<PlaybackCoordinator>(),
            provider.GetRequiredService<LoginCoordinator>(), provider.GetRequiredService<ILoginUiDispatcher>(),
            provider.GetRequiredService<MyAvaloniaManagement.PluginSdk.IDocumentLifetime>(), provider.GetRequiredService<IAccountImageSource>()));
        return services;
    }
}
