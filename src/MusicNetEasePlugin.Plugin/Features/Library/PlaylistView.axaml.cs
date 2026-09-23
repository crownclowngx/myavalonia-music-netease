using Avalonia.Controls;

namespace MusicNetEasePlugin.Features.Library;

/// <summary>只承载浏览投影；导航请求与账号撤销不由视觉树挂载事件驱动，Dock 重挂不会重新读取歌单。</summary>
public sealed partial class PlaylistView : UserControl
{
    public PlaylistView() => InitializeComponent();
}
