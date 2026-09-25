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
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Features.Library;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Features.Discovery;

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
        services.TryAddSingleton<AccountImageSource>();
        services.TryAddSingleton<IAccountImageSource>(p => new AccountImageCache(p.GetRequiredService<AccountImageSource>(), p.GetRequiredService<LoginCoordinator>()));
        services.TryAddSingleton<ArtworkContext>();
        // 此处只登记插件私有服务。MusicSettingsTool 已由模块的 AddTool 声明，Host 会在
        // Configure 返回后追加其 singleton 描述符，Document 可直接注入同一个实例。
        // 若在此重复登记贡献根，Host 会在构建容器前拒绝整个插件；Standalone 自行补齐即可。
        services.TryAddSingleton<ISessionProtector, CurrentUserSessionProtector>();
        services.TryAddSingleton<ILoginSessionStore>(provider =>
            new ProtectedLoginSessionStore(Path.GetFullPath(root), provider.GetRequiredService<ISessionProtector>()));
        services.TryAddSingleton<LoginCoordinator>();
        services.TryAddSingleton<ILoginUiDispatcher, LoginUiDispatcher>();
        services.TryAddSingleton<IUiPreferencesStore>(_ => new UiPreferencesStore(Path.GetFullPath(root)));
        services.TryAddSingleton<UiPreferences>();
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
        services.TryAddSingleton<MusicRequestExecutor>();
        services.TryAddSingleton<IDiscoveryCatalogApi, NeteaseDiscoveryApi>();
        services.TryAddSingleton<IArtistAlbumApi, NeteaseArtistAlbumApi>();
        services.TryAddSingleton<IRemoteHistoryApi, NeteaseRemoteHistoryApi>();
        services.TryAddSingleton<NeteasePrivateFmApi>();
        services.TryAddSingleton<IPrivateFmContentApi>(p => p.GetRequiredService<NeteasePrivateFmApi>());
        services.TryAddSingleton<IPrivateFmFeedbackApi>(p => p.GetRequiredService<NeteasePrivateFmApi>());
        services.TryAddSingleton<PrivateFmCoordinator>();
        services.TryAddScoped<DiscoveryWorkspace>();
        services.TryAddScoped<PrivateFmWorkspace>();
        services.TryAddSingleton<IPlaylistCatalogApi, NeteasePlaylistApi>();
        services.TryAddScoped<PlaylistBrowser>();
        services.TryAddSingleton<ILibraryCheckTokenProvider>(_ => new WebViewLibraryCheckTokenProvider(Path.GetFullPath(root)));
        services.TryAddSingleton<LibraryRequestExecutor>();
        services.TryAddSingleton<ILikedSongsApi, NeteaseLikedSongsApi>();
        services.TryAddSingleton<ILibraryPlaylistQuery, NeteaseLibraryPlaylistQuery>();
        services.TryAddSingleton<IPlaylistMutationApi, NeteasePlaylistMutationApi>();
        services.TryAddSingleton<MusicLibraryCoordinator>();
        services.TryAddScoped<PlaylistEditor>();
        services.TryAddSingleton<IMusicCatalogApi>(provider => provider.GetRequiredService<NeteaseMusicApi>());
        services.TryAddSingleton<IPlaybackResourceResolver>(provider => provider.GetRequiredService<NeteaseMusicApi>());
        services.TryAddSingleton<IMediaBuffer>(provider => new FlurlMediaBuffer(provider.GetRequiredService<NeteaseFlurlClients>(), Path.Combine(root, "media-buffer"), MediaLimits.Default));
        services.TryAddSingleton<IAudioOutput, LibVlcAudioOutput>();
        services.TryAddSingleton<PlaybackCoordinator>();
        services.TryAddSingleton<IPlaybackStateStore>(_ => new PlaybackStateStore(Path.GetFullPath(root)));
        services.TryAddSingleton<PlaybackPersistence>();
        services.TryAddSingleton(provider => new PlaybackQueueCoordinator(provider.GetRequiredService<IMusicSessionAccessor>(),
            provider.GetRequiredService<PlaybackCoordinator>(), provider.GetRequiredService<PlaybackPersistence>(), requireDocument: true, fm: provider.GetRequiredService<PrivateFmCoordinator>()));
        services.TryAddSingleton<IPrivateFmPlayer>(provider => provider.GetRequiredService<PlaybackQueueCoordinator>());
        services.TryAddSingleton<PlayerAccountCoordinator>();
        services.TryAddSingleton<IPlayerSession>(provider => provider.GetRequiredService<PlaybackQueueCoordinator>());
        services.TryAddSingleton<ILyricsApi, NeteaseLyricsApi>();
        services.TryAddSingleton(provider => new LyricsCoordinator(provider.GetRequiredService<IPlayerSession>(),
            provider.GetRequiredService<IMusicSessionAccessor>(), provider.GetRequiredService<ILyricsApi>(), startSuspended: true));
        services.TryAddSingleton<MusicPlaybackLifetime>();
        services.TryAddScoped(provider => new MusicPlaybackLease(provider.GetRequiredService<MusicPlaybackLifetime>(),
            provider.GetRequiredService<MyAvaloniaManagement.PluginSdk.IDocumentLifetime>()));
        // Host 在 Document Scope 中提供 Lifetime；工厂避免普通服务容器预检被要求创建一个虚假的 Document。
        services.TryAddScoped(provider =>
        {
            _ = provider.GetRequiredService<PlayerAccountCoordinator>();
            var discovery = provider.GetRequiredService<DiscoveryWorkspace>();
            discovery.Fm = provider.GetRequiredService<PrivateFmWorkspace>();
            return new MusicWorkspace(provider.GetRequiredService<IMusicCatalogApi>(),
            provider.GetRequiredService<IMusicSessionAccessor>(), provider.GetRequiredService<IPlayerSession>(),
            provider.GetRequiredService<LoginCoordinator>(), provider.GetRequiredService<ILoginUiDispatcher>(),
            provider.GetRequiredService<MyAvaloniaManagement.PluginSdk.IDocumentLifetime>(), provider.GetRequiredService<IAccountImageSource>(),
            provider.GetRequiredService<UiPreferences>(), provider.GetRequiredService<PlaylistBrowser>(), provider.GetRequiredService<LyricsCoordinator>(), provider.GetRequiredService<PlaybackPersistence>(), library: provider.GetRequiredService<PlaylistEditor>(), playbackLease: provider.GetRequiredService<MusicPlaybackLease>(), discovery: discovery);
        });
        return services;
    }
}
