using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Flurl.Http.Configuration;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Infrastructure.Persistence;
using MusicNetEasePlugin.Infrastructure.Ui;

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
        services.TryAddSingleton<IAccountImageSource, AccountImageSource>();
        services.TryAddSingleton<ISessionProtector, CurrentUserSessionProtector>();
        services.TryAddSingleton<ILoginSessionStore>(provider =>
            new ProtectedLoginSessionStore(Path.GetFullPath(root), provider.GetRequiredService<ISessionProtector>()));
        services.TryAddSingleton<LoginCoordinator>();
        services.TryAddSingleton<ILoginUiDispatcher, LoginUiDispatcher>();
        return services;
    }
}
