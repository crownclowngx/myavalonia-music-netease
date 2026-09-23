using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using MusicNetEasePlugin.Infrastructure.Ui;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>
/// 固定播放条只处理布局、定位手势和封面呈现。播放命令属于 PlayerBarWorkspace，
/// 进度松手提交规则属于 TimelineWorkspace；Dock 拆卸只释放本视图资源，不停止歌曲。
/// </summary>
public partial class PlaybackBarView : UserControl
{
    private MusicWorkspace? _model;
    private bool? _compact;
    public ViewMotion Motion { get; }
    internal void FocusPanelButton(bool queue) => (queue ? QueueToggle : LyricsToggle).Focus();
    public PlaybackBarView()
    {
        Motion = new(this);
        InitializeComponent();
        PlaybackProgress.AddHandler(PointerPressedEvent, (_, _) => _model?.Player.Timeline.Begin(), RoutingStrategies.Tunnel);
        PlaybackProgress.AddHandler(PointerReleasedEvent, async (_, _) => { if (_model is { } m) await m.Player.Timeline.CommitAsync(); }, RoutingStrategies.Tunnel);
        PlaybackProgress.PointerCaptureLost += (_, _) => _model?.Player.Timeline.Cancel();
        PlaybackProgress.AddHandler(KeyDownEvent, (_, e) => { if (SeekKey(e.Key)) _model?.Player.Timeline.Begin(); if (e.Key == Key.Escape) _model?.Player.Timeline.Cancel(); }, RoutingStrategies.Tunnel);
        PlaybackProgress.AddHandler(KeyUpEvent, async (_, e) => { if (SeekKey(e.Key) && _model is { } m) await m.Player.Timeline.CommitAsync(); }, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => { if (VisualRoot is not null) Bind(); };
        SizeChanged += (_, _) => Layout();
        Motion.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ViewMotion.IsActive)) _model?.Player.SetVisible(Motion.IsActive); };
    }
    private static bool SeekKey(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Bind(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Unbind(); base.OnDetachedFromVisualTree(e); }
    private void Bind()
    {
        Unbind(); _model = DataContext as MusicWorkspace; Motion.Bind(_model?.Preferences);
        if (_model is not null) { _model.Player.PropertyChanged += Changed; _model.Player.SetVisible(Motion.IsActive); }
    }
    private void Unbind()
    {
        if (_model is not null) { _model.Player.PropertyChanged -= Changed; _model.Player.SetVisible(false); _model.Player.Timeline.Cancel(); }
        _model = null; Motion.Bind(null);
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerBarWorkspace.StateText)) Motion.FadeIn(PlaybackState);
    }
    private void Layout()
    {
        WideVolume.IsVisible = Bounds.Width >= 1120;
        var compact = Bounds.Width < 850;
        if (_compact == compact) return;
        _compact = compact;
        Grid.SetColumn(AuxiliaryControls, compact ? 0 : 2);
        Grid.SetRow(AuxiliaryControls, compact ? 2 : 0);
        Grid.SetColumnSpan(AuxiliaryControls, compact ? 3 : 1);
        PlaybackLayout.RowDefinitions = new("Auto,26,Auto,Auto");
    }
}
