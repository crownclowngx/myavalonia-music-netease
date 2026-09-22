using MyAvaloniaManagement.PluginSdk;

namespace MusicNetEasePlugin.Constants;

/// <summary>沿用初始化时的插件和 Document 持久身份，不因页面从模板变成登录页而改名。</summary>
public static class PluginIds
{
    public static readonly PluginId Plugin = new("myavalonia.plugin.music.netease");
    public static readonly DocumentTypeId MainDocument = new("myavalonia.plugin.music.netease.document.main");
}
