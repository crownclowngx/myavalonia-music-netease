using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Infrastructure.Ui;

namespace MusicNetEasePlugin.Features.Main;

/// <summary>
/// View 只拥有位图资源。停靠移动会导致视觉树暂时分离，因此这里仅释放/重建图片，
/// 不把 DetachedFromVisualTree 当作永久关闭；真正取消由 Document Lifetime 负责。
/// </summary>
public partial class MainView : UserControl
{
    private MainDocument? _document;
    private Bitmap? _qr;
    private bool _signedIn;
    private bool? _narrow;
    public ViewMotion Motion { get; }

    public MainView()
    {
        Motion = new(this);
        Motion.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ViewMotion.IsActive)) _document?.SetVisible(Motion.IsActive); };
        InitializeComponent();
        DataContextChanged += (_, _) => { if (VisualRoot is not null) BindDocument(); };
        SizeChanged += (_, _) => ApplyWidth();
        AddHandler(KeyDownEvent, (_, e) =>
        { if (e.Key == Avalonia.Input.Key.Escape && SettingsOverlay.IsVisible) { SettingsOverlay.IsVisible = false; MusicContent.IsVisible = true; AccountMenuButton.Focus(); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Bubble);
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
        base.OnDetachedFromVisualTree(e);
    }

    private void BindDocument()
    {
        UnbindDocument();
        _document = DataContext as MainDocument;
        Motion.Bind(_document?.Preferences);
        _document?.SetVisible(Motion.IsActive);
        _signedIn = _document?.IsSignedIn == true;
        if (_document is not null) _document.PropertyChanged += OnModelChanged;
        SetImage(QrImage, ref _qr, _document?.QrImageBytes);
    }

    private void UnbindDocument()
    {
        if (_document is not null) _document.PropertyChanged -= OnModelChanged;
        _document?.SetVisible(false);
        _document = null;
        Motion.Bind(null);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainDocument.QrImageBytes))
            SetImage(QrImage, ref _qr, _document?.QrImageBytes);
        else if (args.PropertyName == nameof(MainDocument.IsSignedIn) && _signedIn != (_document?.IsSignedIn == true))
        {
            _signedIn = _document?.IsSignedIn == true;
            if (_signedIn) Motion.FadeIn(MusicContent);
        }
    }

    private Avalonia.Input.IInputElement? _returnFocus;
    private async void OpenSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _returnFocus = AccountMenuButton;
        SettingsOverlay.IsVisible = true; MusicContent.IsVisible = false;
        if (_document?.Settings is { } settings) await settings.EnsureLoadedAsync();
        SettingsOverlay.GetVisualDescendants().OfType<Button>().FirstOrDefault()?.Focus();
    }
    private void CloseSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { SettingsOverlay.IsVisible = false; MusicContent.IsVisible = true; _returnFocus?.Focus(); }

    private void ApplyWidth()
    {
        var narrow = Bounds.Width < 640;
        AccountSummary.MaxWidth = Bounds.Width >= 520 ? 160 : 70;
        if (_narrow == narrow) return;
        _narrow = narrow;
        // 只在跨过断点时重排现有元素，二维码、输入和业务模型均保留原实例。
        LoginLayout.ColumnDefinitions = new(narrow ? "*" : "224,*");
        Grid.SetColumn(LoginDetails, narrow ? 0 : 1);
        Grid.SetRow(LoginDetails, narrow ? 1 : 0);
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
