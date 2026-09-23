using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MusicNetEasePlugin.Infrastructure.Ui;

/// <summary>
/// 图片控件的请求和租约严格跟随实际可见性。虚拟化回收、隐藏、最小化、切换地址均撤销本控件请求。
/// Context 通过逻辑树继承，业务模型不需要引用 Bitmap；没有容器提供图片服务时自然显示矢量占位。
/// </summary>
public sealed class ArtworkImage : Image
{
    public static readonly AttachedProperty<ArtworkContext?> ContextProperty = AvaloniaProperty.RegisterAttached<ArtworkImage, Control, ArtworkContext?>("Context", inherits: true);
    public static ArtworkContext? GetContext(Control control) => control.GetValue(ContextProperty);
    public static void SetContext(Control control, ArtworkContext? value) => control.SetValue(ContextProperty, value);
    public static readonly StyledProperty<byte[]?> BytesProperty = AvaloniaProperty.Register<ArtworkImage, byte[]?>(nameof(Bytes));
    public static readonly StyledProperty<string?> AddressProperty = AvaloniaProperty.Register<ArtworkImage, string?>(nameof(Address));
    public byte[]? Bytes { get => GetValue(BytesProperty); set => SetValue(BytesProperty, value); }
    public string? Address { get => GetValue(AddressProperty); set => SetValue(AddressProperty, value); }
    private readonly ViewMotion _motion;
    private ArtworkContext? _context;
    private ArtworkCache.Lease? _lease;
    private CancellationTokenSource? _request;
    private long _generation;
    public ArtworkImage()
    {
        _motion = new(this);
        _motion.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ViewMotion.IsActive)) Refresh(); };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Bind(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Unbind(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContextProperty && VisualRoot is not null) Bind();
        else if (change.Property == BytesProperty || change.Property == AddressProperty) Refresh();
    }
    private void Bind()
    {
        Unbind(); _context = GetContext(this);
        if (_context is not null) { _context.Preferences.PropertyChanged += PreferenceChanged; _context.Cache.Cleared += Cleared; }
        Refresh();
    }
    private void Unbind()
    {
        if (_context is not null) { _context.Preferences.PropertyChanged -= PreferenceChanged; _context.Cache.Cleared -= Cleared; }
        _context = null; Release();
    }
    private void PreferenceChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == "ShowArtwork") Refresh(); }
    private void Cleared(object? sender, EventArgs e)
    {
        void Apply() { if (ReferenceEquals(_context?.Cache, sender)) Release(); }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }
    private void Release()
    {
        _generation++;
        try { _request?.Cancel(); } catch (ObjectDisposedException) { /* 任务已完成，UI 清理尚在队列中。 */ }
        _request = null;
        Source = null; _lease?.Dispose(); _lease = null;
    }
    private void Refresh()
    {
        Release();
        if (_context is not { } context || !_motion.IsActive || !context.Preferences.ShowArtwork) return;
        if (Bytes is { } bytes) { _lease = context.Cache.Acquire(bytes); Source = _lease?.Image; return; }
        if (string.IsNullOrWhiteSpace(Address)) return;
        var request = _request = new CancellationTokenSource();
        var token = request.Token; var generation = _generation; var address = Address;
        _ = Load();
        async Task Load()
        {
            try
            {
                var data = await context.Source.LoadAsync(address, token).ConfigureAwait(false);
                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested || generation != _generation || !ReferenceEquals(context, _context)) return;
                    _lease = context.Cache.Acquire(data); Source = _lease?.Image;
                });
            }
            catch (Exception) { /* 图片是装饰，网络和解码失败只回到占位。 */ }
            finally
            {
                Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_request, request)) _request = null; });
                request.Dispose();
            }
        }
    }
}
