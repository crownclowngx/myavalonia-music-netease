using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>
/// 视图局部手势：明确手柄、5 DIP 阈值、插入线及边缘滚动。捕获给稳定 ListBox，避免虚拟行回收丢失身份。
/// 仅释放时提交一次，命令仍由队列协调器校验版本；计时器只在正在拖动且处于边缘时运行。
/// </summary>
internal sealed class QueueDragGesture
{
    private readonly SongListBox _list;
    private readonly Border _indicator;
    private readonly DispatcherTimer _edge = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private QueueWorkspace? _model;
    private QueueMoveIntent? _intent;
    private IPointer? _pointer;
    private Point _start, _point;
    private bool _dragging;
    private int? _target;
    private double _edgeDirection;
    internal bool IsDragging => _dragging;
    internal bool EdgeTimerRunning => _edge.IsEnabled;
    public QueueDragGesture(SongListBox list, Border indicator)
    {
        (_list, _indicator) = (list, indicator);
        list.AddHandler(InputElement.PointerPressedEvent, Press, RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.PointerMovedEvent, Move, RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.PointerReleasedEvent, Release, RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
        list.PointerCaptureLost += (_, _) => Cancel();
        _edge.Tick += (_, _) =>
        {
            if (!_dragging || _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroll) { Cancel(); return; }
            scroll.Offset = new(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + _edgeDirection * 18, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
            _list.UpdateLayout(); Preview();
        };
    }
    public void Bind(QueueWorkspace? model)
    {
        Cancel(); if (_model is not null) _model.PropertyChanged -= Changed;
        _model = model; if (_model is not null) _model.PropertyChanged += Changed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QueueWorkspace.QueueRevision) && _intent is { } intent && intent.QueueRevision != _model?.QueueRevision)
        { var model = _model; Cancel(); model?.CancelMove(); }
    }
    private void Press(object? sender, PointerPressedEventArgs e)
    {
        if (_model is null || !e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed || e.Source is not Control source) return;
        var handle = source.Name == "QueueDragHandle" ? source : source.GetVisualAncestors().OfType<Control>().FirstOrDefault(c => c.Name == "QueueDragHandle");
        if (handle?.DataContext is not QueueRow row) return;
        _list.PointerGestureIsControl = true;
        // 手柄接管完整手势，阻止平台把连续按下识别成列表的双击播放。
        e.PreventGestureRecognition();
        Cancel(); _model.Selected = row; _list.Focus();
        _intent = new(row.Id, -1, _model.QueueRevision, _model.AccountEpoch);
        _start = _point = e.GetPosition(_list); _pointer = e.Pointer; e.Pointer.Capture(_list); e.Handled = true;
    }
    private void Move(object? sender, PointerEventArgs e)
    {
        if (_intent is null) return;
        _point = e.GetPosition(_list);
        if (!_dragging && Math.Pow(_point.X - _start.X, 2) + Math.Pow(_point.Y - _start.Y, 2) >= 25) _dragging = true;
        if (_dragging) Preview(); e.Handled = true;
    }
    private void Preview()
    {
        _target = null; _indicator.IsVisible = false; _edge.Stop();
        if (_model is null || _intent is null || !_list.IsEffectivelyVisible || _point.X < 0 || _point.X > _list.Bounds.Width || _point.Y < 0 || _point.Y > _list.Bounds.Height) return;
        var items = _list.GetVisualDescendants().OfType<ListBoxItem>().Select(item => (Item: item, Top: item.TranslatePoint(default, _list)?.Y))
            .Where(pair => pair.Top is not null && pair.Top + pair.Item.Bounds.Height > 0 && pair.Top < _list.Bounds.Height).OrderBy(pair => pair.Top).ToArray();
        if (items.Length == 0) return;
        var chosen = items.FirstOrDefault(pair => _point.Y < pair.Top + pair.Item.Bounds.Height);
        if (chosen.Item is null) chosen = items[^1];
        if (chosen.Item.DataContext is not QueueRow row) return;
        var after = _point.Y >= chosen.Top + chosen.Item.Bounds.Height / 2;
        var boundary = _model.Rows.IndexOf(row) + (after ? 1 : 0);
        var original = _model.Rows.ToList().FindIndex(r => r.Id == _intent.EntryId);
        _target = Math.Clamp(boundary - (original < boundary ? 1 : 0), 0, _model.Rows.Count - 1);
        _indicator.Margin = new(0, Math.Clamp(chosen.Top!.Value + (after ? chosen.Item.Bounds.Height : 0), 0, Math.Max(0, _list.Bounds.Height - 2)), 0, 0);
        _indicator.IsVisible = true;
        _edgeDirection = _point.Y < 28 ? -1 : _point.Y > _list.Bounds.Height - 28 ? 1 : 0;
        if (_edgeDirection != 0) _edge.Start();
    }
    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        if (_intent is null) return;
        _point = e.GetPosition(_list); if (_dragging) Preview();
        var intent = _dragging && _target is { } target ? _intent with { TargetIndex = target } : null;
        Cancel(); e.Handled = true; if (intent is not null) _model?.CommitMove(intent);
    }
    private void KeyDown(object? sender, KeyEventArgs e) { if (e.Key == Key.Escape && _intent is not null) { Cancel(); e.Handled = true; } }
    public void Cancel()
    {
        _intent = null; _dragging = false; _target = null; _edge.Stop(); _indicator.IsVisible = false;
        var pointer = _pointer; _pointer = null; if (pointer?.Captured == _list) pointer.Capture(null);
    }
}
