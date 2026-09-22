using Microsoft.Extensions.DependencyInjection;

namespace MusicNetEasePlugin.Plugin;

public static class MusicNetEasePluginServices
{
    /// <summary>登记插件自己的业务服务；Standalone 可以复用同一个组合入口。</summary>
    public static IServiceCollection AddMusicNetEasePluginServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
