using MyAvaloniaManagement.PluginSdk;

namespace MusicNetEasePlugin.Constants;

/// <summary>集中维护持久身份；显示名、页面能力和目录变化不能改变已经保存的 Dock 身份。</summary>
public static class PluginIds
{
    public static readonly PluginId Plugin = new("myavalonia.plugin.music.netease");
    public static readonly DocumentTypeId MainDocument = new("myavalonia.plugin.music.netease.document.main");
    public static readonly ToolTypeId AccountSettings = new("myavalonia.plugin.music.netease.tool.account-settings");
}
