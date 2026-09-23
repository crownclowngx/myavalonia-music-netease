using Flurl.Http;
using Flurl.Http.Configuration;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>命名 Client 只承载稳定配置，Cookie/设备头按请求快照设置，容器统一释放缓存。</summary>
internal sealed class NeteaseFlurlClients : IDisposable
{
    private readonly IFlurlClientCache _cache;

    public NeteaseFlurlClients(IFlurlClientCache cache)
    {
        _cache = cache;
        Web = cache.GetOrAdd("netease-web", "https://music.163.com");
        Eapi = cache.GetOrAdd("netease-eapi", "https://interfacepc.music.163.com");
        Xeapi = cache.GetOrAdd("netease-xeapi", "https://interface3.music.163.com");
        Keys = cache.GetOrAdd("netease-keys", "https://interface.music.163.com");
        Media = cache.GetOrAdd("netease-media");
        Images = cache.GetOrAdd("netease-images");
        Social = cache.GetOrAdd("netease-social", "https://music.163.com");
        WeChat = cache.GetOrAdd("netease-wechat", "https://open.weixin.qq.com");
        WeChatPoll = cache.GetOrAdd("netease-wechat-poll");
        foreach (var client in new[] { Web, Eapi, Xeapi, Keys, Media, Images, Social, WeChat, WeChatPoll })
        {
            client.Settings.Timeout = TimeSpan.FromSeconds(15);
            client.Settings.Redirects.Enabled = false;
        }
    }

    public IFlurlClient Web { get; }
    public IFlurlClient Eapi { get; }
    public IFlurlClient Xeapi { get; }
    public IFlurlClient Keys { get; }
    public IFlurlClient Media { get; }
    public IFlurlClient Images { get; }
    public IFlurlClient Social { get; }
    public IFlurlClient WeChat { get; }
    public IFlurlClient WeChatPoll { get; }

    // Flurl 4 的缓存本身不实现 IDisposable，因此由这个拥有者显式移除并释放命名客户端。
    public void Dispose()
    {
        _cache.Clear();
    }
}
