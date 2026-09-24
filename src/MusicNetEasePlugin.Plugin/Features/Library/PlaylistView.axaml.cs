using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Features.Music;
namespace MusicNetEasePlugin.Features.Library;
/// <summary>列表和详情始终保留原视图，返回不重载服务数据；焦点恢复到原歌单。</summary>
public sealed partial class PlaylistView : UserControl
{
    public static readonly DirectProperty<PlaylistView, PlaylistBrowser?> ModelProperty = AvaloniaProperty.RegisterDirect<PlaylistView, PlaylistBrowser?>(nameof(Model), view => view.Model);
    public PlaylistBrowser? Model => DataContext as PlaylistBrowser;
    public PlaylistView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => RaisePropertyChanged(ModelProperty, null, Model);
        // 紧凑 Dock 保留曲目区高度；封面和简介仅在宽高均足够时展开，列表实例与滚动位置不变。
        LayoutUpdated += (_, _) =>
        {
            RichPlaylistHeader.IsVisible = Bounds.Width >= 700 && Bounds.Height >= 450;
            // 窄 Document 的管理工具栏需要给歌曲行留空间。相同信息仍可从播放按钮提示、
            // 操作结果或目录页读取；不压缩行高，不在歌曲上覆盖不可命中的操作图标。
            var compactManagement = this.FindAncestorOfType<MusicView>()?.Model?.Library is not null && Bounds.Height < 300 && Model?.IsDetail == true;
            LibraryReadStatus.IsVisible = !compactManagement; PlaylistReplaceHint.IsVisible = !compactManagement;
        };
    }
    private async void OpenPlaylist(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model || sender is not Control { DataContext: MusicPlaylist item }) return;
        model.SelectedPlaylist = item; await model.OpenCommand.ExecuteAsync(null); PlaylistTrackList.Focus();
    }
    private async void PlaylistKeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && Model?.OpenCommand.CanExecute(null) == true) { e.Handled = true; await Model.OpenCommand.ExecuteAsync(null); PlaylistTrackList.Focus(); } }
    private void ReturnToPlaylists(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() =>
    { if (PlaylistList.ContainerFromIndex(PlaylistList.SelectedIndex) is { } item) item.Focus(); else PlaylistList.Focus(); });
}
