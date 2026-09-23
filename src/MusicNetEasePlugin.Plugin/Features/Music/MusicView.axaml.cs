using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using System.ComponentModel;
using Avalonia;
using MusicNetEasePlugin.Infrastructure.Ui;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>View 拥有封面位图与可撤销的模型订阅；Dock 拆卸只释放图像，不停止歌曲或销毁模型。</summary>
public partial class MusicView : UserControl
{
    private MusicWorkspace? _model;
    private Bitmap? _cover;
    private int _widthMode = -1;
    private string? _state;
    public ViewMotion Motion { get; }
    public static readonly StyledProperty<string> TrackColumnsProperty =
        AvaloniaProperty.Register<MusicView, string>(nameof(TrackColumns), "3*,2*,3*,60");
    public string TrackColumns { get => GetValue(TrackColumnsProperty); private set => SetValue(TrackColumnsProperty, value); }
    public MusicView()
    {
        Motion = new(this);
        InitializeComponent();
        DataContextChanged += (_, _) => { if (VisualRoot is not null) Bind(); };
        SizeChanged += (_, _) => ApplyWidth();
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs args)
    { base.OnAttachedToVisualTree(args); Bind(); }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs args)
    { Unbind(); SetCover(null); base.OnDetachedFromVisualTree(args); }
    private void Bind()
    {
        Unbind();
        _model = DataContext as MusicWorkspace;
        Motion.Bind(_model?.Preferences);
        _state = _model?.StateText;
        if (_model is not null) _model.PropertyChanged += ModelChanged;
        SetCover(_model?.CoverBytes);
    }
    private void Unbind() { if (_model is not null) _model.PropertyChanged -= ModelChanged; _model = null; Motion.Bind(null); }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MusicWorkspace.CoverBytes)) SetCover(_model?.CoverBytes);
        // 进度快照也会通知 StateText，必须比较实际状态；否则每个进度回调都会重播动画。
        if (args.PropertyName == nameof(MusicWorkspace.StateText) && _state != _model?.StateText)
        { _state = _model?.StateText; Motion.FadeIn(PlaybackState); }
    }

    private void ApplyWidth()
    {
        var mode = Bounds.Width >= 900 ? 0 : Bounds.Width >= 640 ? 1 : 2;
        if (mode == _widthMode) return;
        _widthMode = mode;
        Classes.Set("compact", mode > 0);
        Classes.Set("narrow", mode == 2);
        TrackColumns = mode switch { 0 => "3*,2*,3*,60", 1 => "3*,2*,0,60", _ => "*,0,0,52" };
        TimelineLayout.ColumnDefinitions = new(mode == 0 ? "*,Auto,180" : "*,Auto");
        Grid.SetColumn(VolumeControls, mode == 0 ? 2 : 0);
        Grid.SetRow(VolumeControls, mode == 0 ? 0 : 1);
        Grid.SetColumnSpan(VolumeControls, mode == 0 ? 1 : 2);
        VolumeControls.Width = 180;
        VolumeControls.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
    }
    private void SetCover(byte[]? bytes)
    {
        Bitmap? next = null;
        try { if (bytes is not null) { using var stream = new MemoryStream(bytes); next = new Bitmap(stream); } }
        catch (Exception) { /* 损坏封面保留音符占位，不向 Host 抛出图像解码异常。 */ }
        CoverImage.Source = next;
        _cover?.Dispose();
        _cover = next;
    }
    private void SearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && DataContext is MusicWorkspace workspace)
        { workspace.SearchCommand.Execute(null); args.Handled = true; }
    }
}
