using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using System.ComponentModel;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>View 拥有封面位图与可撤销的模型订阅；Dock 拆卸只释放图像，不停止歌曲或销毁模型。</summary>
public partial class MusicView : UserControl
{
    private MusicWorkspace? _model;
    private Bitmap? _cover;
    public MusicView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (VisualRoot is not null) Bind(); };
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs args)
    { base.OnAttachedToVisualTree(args); Bind(); }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs args)
    { Unbind(); SetCover(null); base.OnDetachedFromVisualTree(args); }
    private void Bind()
    {
        Unbind();
        _model = DataContext as MusicWorkspace;
        if (_model is not null) _model.PropertyChanged += ModelChanged;
        SetCover(_model?.CoverBytes);
    }
    private void Unbind() { if (_model is not null) _model.PropertyChanged -= ModelChanged; _model = null; }
    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(MusicWorkspace.CoverBytes)) SetCover(_model?.CoverBytes); }
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
