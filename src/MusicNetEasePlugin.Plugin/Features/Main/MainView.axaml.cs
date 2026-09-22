using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace MusicNetEasePlugin.Features.Main;

/// <summary>
/// View 只拥有位图资源。停靠移动会导致视觉树暂时分离，因此这里仅释放/重建图片，
/// 不把 DetachedFromVisualTree 当作永久关闭；真正取消由 Document Lifetime 负责。
/// </summary>
public partial class MainView : UserControl
{
    private MainDocument? _document;
    private Bitmap? _qr;
    private Bitmap? _avatar;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (VisualRoot is not null) BindDocument(); };
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindDocument();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        UnbindDocument();
        SetImage(QrImage, ref _qr, null);
        SetImage(AvatarImage, ref _avatar, null);
        base.OnDetachedFromVisualTree(e);
    }

    private void BindDocument()
    {
        UnbindDocument();
        _document = DataContext as MainDocument;
        if (_document is not null) _document.PropertyChanged += OnModelChanged;
        SetImage(QrImage, ref _qr, _document?.QrImageBytes);
        SetImage(AvatarImage, ref _avatar, _document?.AvatarImageBytes);
    }

    private void UnbindDocument()
    {
        if (_document is not null) _document.PropertyChanged -= OnModelChanged;
        _document = null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainDocument.QrImageBytes))
            SetImage(QrImage, ref _qr, _document?.QrImageBytes);
        else if (args.PropertyName == nameof(MainDocument.AvatarImageBytes))
            SetImage(AvatarImage, ref _avatar, _document?.AvatarImageBytes);
    }

    private static void SetImage(Image image, ref Bitmap? current, byte[]? bytes)
    {
        Bitmap? next = null;
        try { if (bytes is not null) { using var stream = new MemoryStream(bytes); next = new Bitmap(stream); } }
        catch (Exception) { /* 损坏图片保留占位；不影响账号状态。 */ }
        image.Source = next;
        current?.Dispose();
        current = next;
    }
}
