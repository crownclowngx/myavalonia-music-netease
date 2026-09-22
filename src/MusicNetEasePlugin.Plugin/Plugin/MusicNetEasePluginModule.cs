using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.Icons;
using MusicNetEasePlugin.Constants;
using MusicNetEasePlugin.Features.Main;

namespace MusicNetEasePlugin.Plugin;

public sealed class MusicNetEasePluginModule : IPluginModule
{
    public void Configure(IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Services.AddMusicNetEasePluginServices();
        registration.UseLifecycle<MusicNetEasePluginLifecycle>();
        var asset = CommonIcons.TextCheck;
        var icon = registration.AddIcon("main-document", new VectorIconDefinition(
            asset.PathData, asset.ViewBoxWidth, asset.ViewBoxHeight));
        registration.AddDocument<MainDocument, MainView>(new DocumentDescriptor(
            PluginIds.MainDocument, "网易云音乐", "扫码登录、恢复账号与管理本地登录信息",
            "音乐", iconPath: icon));
        // 登录是页面内意图，不注册模板示例命令或抢占全局快捷键。后续播放命令有稳定语义后再接入。
    }
}
