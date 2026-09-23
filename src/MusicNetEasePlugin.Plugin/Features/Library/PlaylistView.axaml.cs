using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MusicNetEasePlugin.Application.Library;
namespace MusicNetEasePlugin.Features.Library;
/// <summary>列表和详情始终保留原视图，返回不重载服务数据；焦点恢复到原歌单。</summary>
public sealed partial class PlaylistView : UserControl
{
    public static readonly DirectProperty<PlaylistView, PlaylistBrowser?> ModelProperty = AvaloniaProperty.RegisterDirect<PlaylistView, PlaylistBrowser?>(nameof(Model), view => view.Model);
    public PlaylistBrowser? Model => DataContext as PlaylistBrowser;
    public PlaylistView() { InitializeComponent(); DataContextChanged += (_, _) => RaisePropertyChanged(ModelProperty, null, Model); }
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
