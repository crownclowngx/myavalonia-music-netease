using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace MusicNetEasePlugin.Features.Library;

/// <summary>歌曲行的轻量投影控件。只订阅 Document 编辑模型，回收容器换 ID 后重新投影，绝不逐行读取远端喜欢集合。</summary>
public partial class SongLibraryActionsView : UserControl
{
    public static readonly StyledProperty<PlaylistEditor?> EditorProperty = AvaloniaProperty.Register<SongLibraryActionsView, PlaylistEditor?>(nameof(Editor));
    public static readonly StyledProperty<long> TrackIdProperty = AvaloniaProperty.Register<SongLibraryActionsView, long>(nameof(TrackId));
    public PlaylistEditor? Editor { get => GetValue(EditorProperty); set => SetValue(EditorProperty, value); }
    public long TrackId { get => GetValue(TrackIdProperty); set => SetValue(TrackIdProperty, value); }
    private PlaylistEditor? _editor;
    public SongLibraryActionsView()
    {
        InitializeComponent();
        PropertyChanged += (_, e) => { if (e.Property == EditorProperty && VisualRoot is not null) Bind(); else if (e.Property == TrackIdProperty) Refresh(); };
        LikeSongButton.Click += (_, e) => { if (Editor?.LikeCommand.CanExecute(TrackId) == true) Editor.LikeCommand.Execute(TrackId); e.Handled = true; };
        AddSongButton.Click += (_, e) => { if (Editor?.AddPanelCommand.CanExecute(TrackId) == true) Editor.AddPanelCommand.Execute(TrackId); e.Handled = true; };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Bind(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Unbind(); base.OnDetachedFromVisualTree(e); }
    private void Bind() { Unbind(); _editor = Editor; if (_editor is not null) _editor.PropertyChanged += Changed; Refresh(); }
    private void Unbind() { if (_editor is not null) _editor.PropertyChanged -= Changed; _editor = null; }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(PlaylistEditor.Snapshot)) Refresh(); }
    private void Refresh()
    {
        ActionsPanel.IsVisible = Editor is not null && TrackId > 0;
        var state = Editor?.Snapshot.IsLiked(TrackId);
        var title = state switch { true => "取消喜欢", false => "喜欢这首歌曲", null => "喜欢状态待加载，请重新读取音乐库" };
        LikeSongButton.Content = state switch { true => "♥", false => "♡", null => "?" };
        ToolTip.SetTip(LikeSongButton, title); AutomationProperties.SetName(LikeSongButton, title);
        LikeSongButton.IsEnabled = Editor?.LikeCommand.CanExecute(TrackId) == true;
        AddSongButton.IsEnabled = Editor?.AddPanelCommand.CanExecute(TrackId) == true;
    }
}
