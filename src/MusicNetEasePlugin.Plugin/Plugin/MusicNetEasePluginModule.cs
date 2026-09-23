using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.Icons;
using MusicNetEasePlugin.Constants;
using MusicNetEasePlugin.Features.Main;
using MusicNetEasePlugin.Features.Settings;
using MyAvaloniaManagement.PluginSdk;

namespace MusicNetEasePlugin.Plugin;

public sealed class MusicNetEasePluginModule : IPluginModule
{
    public void Configure(IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Services.AddMusicNetEasePluginServices();
        registration.UseLifecycle<MusicNetEasePluginLifecycle>();
        var icon = registration.AddIcon("main-document", new VectorIconDefinition(
            "M7,3 L17,1 V13 A3,3 0 1 1 15,10 V5 L9,7 V15 A3,3 0 1 1 7,12 Z", 20, 20));
        registration.AddDocument<MainDocument, MainView>(new DocumentDescriptor(
            PluginIds.MainDocument, "网易云音乐", "扫码登录、浏览歌单与共享播放队列",
            "音乐", iconPath: icon));
        registration.AddTool<MusicSettingsTool, MusicSettingsView>(new ToolDescriptor(
            PluginIds.AccountSettings, "网易云音乐 · 账号与播放设置",
            "查看账号和配置内置或共享 LibVLC 目录", ToolDockSide.Right, ToolCloseBehavior.Hide, iconPath: icon));
        // 登录是页面内意图，不注册模板示例命令或抢占全局快捷键。后续播放命令有稳定语义后再接入。
    }
}
