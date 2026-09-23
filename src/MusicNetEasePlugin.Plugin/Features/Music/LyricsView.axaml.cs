using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>自动滚动只在可见且允许跟随时发生；手动滚轮/拖动/键盘浏览中止跟随，重挂不增加订阅。</summary>
public partial class LyricsView : UserControl
{
    private LyricsWorkspace? _model;
    public LyricsView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => { if (VisualRoot is not null) Bind(); };
        LyricList.AddHandler(PointerWheelChangedEvent, (_, _) => _model?.StopFollowing(), RoutingStrategies.Tunnel);
        LyricList.AddHandler(PointerPressedEvent, (_, _) => _model?.StopFollowing(), RoutingStrategies.Tunnel);
        LyricList.AddHandler(KeyDownEvent, (_, e) => { if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End) _model?.StopFollowing(); }, RoutingStrategies.Tunnel);
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Bind(); }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) { Unbind(); base.OnDetachedFromVisualTree(e); }
    private void Bind() { Unbind(); _model = DataContext as LyricsWorkspace; if (_model is not null) _model.PropertyChanged += Changed; Scroll(); }
    private void Unbind() { if (_model is not null) _model.PropertyChanged -= Changed; _model = null; }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(LyricsWorkspace.CurrentLine)) Scroll(); }
    private void Scroll() { if (IsEffectivelyVisible && _model is { Following: true, CurrentLine: >= 0 } model) LyricList.ScrollIntoView(model.CurrentLine); }
}
