using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Infrastructure.Ui;
namespace MusicNetEasePlugin.Features.Music;
/// <summary>队列只在首次挂载和用户明确点击时定位，不在进度通知中抢走滚动位置。</summary>
public sealed partial class QueueView : UserControl
{
    private bool _located;
    internal QueueDragGesture Drag { get; }
    private readonly ViewMotion _visibility;
    public static readonly DirectProperty<QueueView, QueueWorkspace?> ModelProperty = AvaloniaProperty.RegisterDirect<QueueView, QueueWorkspace?>(nameof(Model), view => view.Model);
    public QueueWorkspace? Model => DataContext as QueueWorkspace;
    public QueueView()
    {
        _visibility = new(this); InitializeComponent(); Drag = new(QueueList, DropIndicator);
        _visibility.PropertyChanged += (_, _) => { if (!_visibility.IsActive) Drag.Cancel(); };
        DataContextChanged += (_, _) => { _located = false; RaisePropertyChanged(ModelProperty, null, Model); if (VisualRoot is not null) Drag.Bind(Model); };
        LayoutUpdated += (_, _) => { if (!_located && IsEffectivelyVisible && Model?.Current is { } current) { _located = true; QueueList.ScrollIntoView(current); } };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Drag.Bind(Model); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Drag.Bind(null); base.OnDetachedFromVisualTree(e); }
    private void LocateCurrent(object? sender, RoutedEventArgs args) { if (Model?.Current is { } current) QueueList.ScrollIntoView(current); }
    private void BrowseSearch(object? sender, RoutedEventArgs args) { var navigation = this.FindAncestorOfType<MusicView>()?.Model?.Navigation; navigation?.Browse(MusicBrowsePage.Search); navigation?.Back(); }
}
