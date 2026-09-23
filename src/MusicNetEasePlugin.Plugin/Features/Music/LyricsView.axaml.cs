using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Animation;
using Avalonia.VisualTree;
using Avalonia.Threading;
using MusicNetEasePlugin.Infrastructure.Ui;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>自动滚动只在可见且允许跟随时发生；手动滚轮/拖动/键盘浏览中止跟随，重挂不增加订阅。</summary>
public partial class LyricsView : UserControl
{
    private LyricsWorkspace? _model;
    private ScrollViewer? _scroll;
    private long _scrollGeneration;
    public static readonly DirectProperty<LyricsView, LyricsWorkspace?> ModelProperty = AvaloniaProperty.RegisterDirect<LyricsView, LyricsWorkspace?>(nameof(Model), view => view.Model);
    public LyricsWorkspace? Model => DataContext as LyricsWorkspace;
    public ViewMotion Motion { get; }
    public LyricsView()
    {
        Motion = new(this); InitializeComponent(); DataContextChanged += (_, _) => { RaisePropertyChanged(ModelProperty, _model, Model); if (VisualRoot is not null) Bind(); };
        SizeChanged += (_, _) => { Classes.Set("compact", Bounds.Width < 700); Scroll(); };
        Motion.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ViewMotion.IsActive)) { _model?.SetVisible(Motion.IsActive); if (Motion.IsActive) Scroll(); else CancelScroll(); } else if (!Motion.IsEnabled) CancelScroll(); };
        LyricList.AddHandler(PointerWheelChangedEvent, (_, _) => StopFollowing(), RoutingStrategies.Tunnel);
        LyricList.AddHandler(PointerPressedEvent, (_, e) =>
        {
            StopFollowing();
            var item = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>();
            if (item?.DataContext is LyricRow row && _model is not null) _model.Selected = row;
        }, RoutingStrategies.Tunnel);
        LyricList.AddHandler(KeyDownEvent, (_, e) => { if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End) StopFollowing(); }, RoutingStrategies.Tunnel);
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Bind(); }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) { Unbind(); base.OnDetachedFromVisualTree(e); }
    private void Bind() { Unbind(); _model = DataContext as LyricsWorkspace; Motion.Bind(_model?.Preferences); if (_model is not null) { _model.PropertyChanged += Changed; _model.SetVisible(Motion.IsActive); } Scroll(); }
    private void Unbind() { CancelScroll(); _scroll = null; if (_model is not null) { _model.PropertyChanged -= Changed; _model.SetVisible(false); } _model = null; Motion.Bind(null); }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(LyricsWorkspace.CurrentLine) or nameof(LyricsWorkspace.ShowTranslation)) Scroll(); }
    private void StopFollowing() { CancelScroll(); _model?.StopFollowing(); }
    private void CancelScroll() { _scrollGeneration++; if (_scroll is not null) _scroll.Transitions = null; }
    private void Scroll()
    {
        CancelScroll();
        if (!IsEffectivelyVisible || _model is not { Following: true, CurrentLine: >= 0 } model) return;
        var generation = _scrollGeneration; var line = model.CurrentLine;
        _scroll ??= LyricList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        // 等抽屉完成本轮布局再请求实现目标行；在零尺寸/隐藏布局中 ScrollIntoView
        // 会留下尚未归属索引的临时容器，尤其首次展开时会与当前行重叠。
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _scrollGeneration || !IsEffectivelyVisible || model != _model || !model.Following) return;
            LyricList.UpdateLayout();
            _scroll ??= LyricList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            var previous = _scroll?.Offset ?? default;
            if (LyricList.ContainerFromIndex(line) is null) { LyricList.ScrollIntoView(line); LyricList.UpdateLayout(); }
            if (_scroll is not { } viewer || LyricList.ContainerFromIndex(line) is not Control item ||
                item.TranslatePoint(new Point(0, item.Bounds.Height / 2), viewer) is not { } center) return;
            var target = new Vector(viewer.Offset.X, Math.Clamp(viewer.Offset.Y + center.Y - viewer.Viewport.Height / 2, 0, Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height)));
            if (Motion.IsEnabled && Math.Abs(target.Y - previous.Y) < viewer.Viewport.Height * 2)
            {
                viewer.Offset = previous;
                viewer.Transitions = new Transitions { new VectorTransition { Property = ScrollViewer.OffsetProperty, Duration = TimeSpan.FromMilliseconds(160) } };
            }
            viewer.Offset = target;
        }, DispatcherPriority.Loaded);
    }
}
