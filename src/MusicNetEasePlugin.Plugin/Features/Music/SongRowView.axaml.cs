using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>
/// 搜索、歌单与历史共用的轻量歌曲行，只接受文字和命令，不了解业务服务。
/// 指针进入子按钮前先选中所属行，保证行内操作与键盘命令作用于同一歌曲；
/// 更多菜单复用这一组命令，不复制另一套播放规则。
/// </summary>
public partial class SongRowView : UserControl
{
    private Flyout? _details;
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<SongRowView, string>(nameof(Title), "");
    public static readonly StyledProperty<string> SubtitleProperty = AvaloniaProperty.Register<SongRowView, string>(nameof(Subtitle), "");
    public static readonly StyledProperty<string> DurationProperty = AvaloniaProperty.Register<SongRowView, string>(nameof(Duration), "");
    public static readonly StyledProperty<string> ArtistProperty = AvaloniaProperty.Register<SongRowView, string>(nameof(Artist), "");
    public static readonly StyledProperty<string> AlbumProperty = AvaloniaProperty.Register<SongRowView, string>(nameof(Album), "");
    public static readonly StyledProperty<long> TrackIdProperty = AvaloniaProperty.Register<SongRowView, long>(nameof(TrackId));
    public static readonly StyledProperty<long?> CurrentTrackIdProperty = AvaloniaProperty.Register<SongRowView, long?>(nameof(CurrentTrackId));
    public static readonly StyledProperty<bool> CurrentProperty = AvaloniaProperty.Register<SongRowView, bool>(nameof(Current));
    public static readonly StyledProperty<ICommand?> PlayCommandProperty = AvaloniaProperty.Register<SongRowView, ICommand?>(nameof(PlayCommand));
    public static readonly StyledProperty<ICommand?> NextCommandProperty = AvaloniaProperty.Register<SongRowView, ICommand?>(nameof(NextCommand));
    public static readonly StyledProperty<ICommand?> AppendCommandProperty = AvaloniaProperty.Register<SongRowView, ICommand?>(nameof(AppendCommand));
    public static readonly StyledProperty<ICommand?> FromHereCommandProperty = AvaloniaProperty.Register<SongRowView, ICommand?>(nameof(FromHereCommand));
    public static readonly StyledProperty<ICommand?> RemoveCommandProperty = AvaloniaProperty.Register<SongRowView, ICommand?>(nameof(RemoveCommand));
    public static readonly StyledProperty<ICommand?> UpCommandProperty = AvaloniaProperty.Register<SongRowView, ICommand?>(nameof(UpCommand));
    public static readonly StyledProperty<ICommand?> DownCommandProperty = AvaloniaProperty.Register<SongRowView, ICommand?>(nameof(DownCommand));
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public string Artist { get => GetValue(ArtistProperty); set => SetValue(ArtistProperty, value); }
    public string Album { get => GetValue(AlbumProperty); set => SetValue(AlbumProperty, value); }
    public long TrackId { get => GetValue(TrackIdProperty); set => SetValue(TrackIdProperty, value); }
    public long? CurrentTrackId { get => GetValue(CurrentTrackIdProperty); set => SetValue(CurrentTrackIdProperty, value); }
    public bool Current { get => GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }
    public ICommand? PlayCommand { get => GetValue(PlayCommandProperty); set => SetValue(PlayCommandProperty, value); }
    public ICommand? NextCommand { get => GetValue(NextCommandProperty); set => SetValue(NextCommandProperty, value); }
    public ICommand? AppendCommand { get => GetValue(AppendCommandProperty); set => SetValue(AppendCommandProperty, value); }
    public ICommand? FromHereCommand { get => GetValue(FromHereCommandProperty); set => SetValue(FromHereCommandProperty, value); }
    public ICommand? RemoveCommand { get => GetValue(RemoveCommandProperty); set => SetValue(RemoveCommandProperty, value); }
    public ICommand? UpCommand { get => GetValue(UpCommandProperty); set => SetValue(UpCommandProperty, value); }
    public ICommand? DownCommand { get => GetValue(DownCommandProperty); set => SetValue(DownCommandProperty, value); }
    public IRelayCommand ActivateCommand { get; }
    public SongRowView()
    {
        // 行内播放不能由全局“尚未选中”状态禁用，否则第一次点击只会选中，第二次才播放。
        // 此适配命令只处理行选择，业务语义仍完全复用页面传入的 PlayCommand。
        ActivateCommand = new RelayCommand(() => { SelectRow(); if (PlayCommand?.CanExecute(null) == true) PlayCommand.Execute(null); }, () => PlayCommand is not null);
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => { MoreButton.Flyout?.Hide(); _details?.Hide(); };
        DataContextChanged += (_, _) => _details?.Hide();
        SizeChanged += (_, _) => ApplyLayout();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == CurrentProperty || e.Property == TrackIdProperty || e.Property == CurrentTrackIdProperty)
            { CurrentMarker.IsVisible = Current || TrackId > 0 && TrackId == CurrentTrackId; Classes.Set("current", CurrentMarker.IsVisible); }
            if (e.Property == PlayCommandProperty) ActivateCommand.NotifyCanExecuteChanged();
            if (e.Property == FromHereCommandProperty) FromHereItem.IsVisible = FromHereCommand is not null;
            if (e.Property == NextCommandProperty) NextItem.IsVisible = NextCommand is not null;
            if (e.Property == AppendCommandProperty) AppendItem.IsVisible = AppendCommand is not null;
            if (e.Property == RemoveCommandProperty) RemoveItem.IsVisible = RemoveCommand is not null;
            if (e.Property == UpCommandProperty) UpItem.IsVisible = UpCommand is not null;
            if (e.Property == DownCommandProperty) DownItem.IsVisible = DownCommand is not null;
        };
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            SelectRow();
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) { OpenMenu(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        GotFocus += (_, _) => SelectRow();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { OpenMenu(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }
    public void OpenMenu() { SelectRow(); MoreButton.Flyout?.ShowAt(MoreButton); }
    private void ShowDetails(object? sender, RoutedEventArgs e)
    {
        // 只展示当前已加载资料；绑定到行属性，虚拟化重用或资料补全不会遗留上首信息。
        var content = new StackPanel { Spacing = 8, MaxWidth = 340 };
        foreach (var property in new[] { nameof(Title), nameof(Artist), nameof(Album), nameof(Subtitle) })
        {
            var text = new SelectableTextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            text.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(property) { Source = this }); content.Children.Add(text);
        }
        MoreButton.Flyout?.Hide(); _details?.Hide(); _details = new Flyout { Content = content }; _details.ShowAt(MoreButton);
    }
    private void ApplyLayout()
    {
        // 依据行自身的宽度布局；右侧队列打开后不沿用外部 Document 的宽度。
        var wide = Bounds.Width >= 760;
        // 保持三个逻辑列稳定，只改宽度。把三列换成单列会使回收容器暂时缓存第 0 列，
        // 再展开时歌名/歌手/专辑重叠；宽度切换不应改变子控件的列身份。
        TextLayout.ColumnDefinitions[0].Width = new(wide ? 3 : 1, GridUnitType.Star);
        TextLayout.ColumnDefinitions[1].Width = wide ? new(2, GridUnitType.Star) : new(0);
        TextLayout.ColumnDefinitions[2].Width = wide ? new(2, GridUnitType.Star) : new(0);
        ArtistColumn.IsVisible = wide; AlbumColumn.IsVisible = wide; DetailText.IsVisible = !wide;
        RowLayout.MinHeight = wide ? 40 : 48;
        RowLayout.ColumnDefinitions = new(string.IsNullOrEmpty(Duration) ? "16,*,0,Auto" : "16,*,42,Auto");
    }
    private void SelectRow() { if (this.FindAncestorOfType<ListBoxItem>() is { } item) item.IsSelected = true; }
}
