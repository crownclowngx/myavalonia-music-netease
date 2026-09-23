using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
namespace MusicNetEasePlugin.Features.Music;
/// <summary>队列只在首次挂载和用户明确点击时定位，不在进度通知中抢走滚动位置。</summary>
public sealed partial class QueueView : UserControl
{
    private bool _located;
    public static readonly DirectProperty<QueueView, QueueWorkspace?> ModelProperty = AvaloniaProperty.RegisterDirect<QueueView, QueueWorkspace?>(nameof(Model), view => view.Model);
    public QueueWorkspace? Model => DataContext as QueueWorkspace;
    public QueueView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => { _located = false; RaisePropertyChanged(ModelProperty, null, Model); };
        LayoutUpdated += (_, _) => { if (!_located && IsEffectivelyVisible && Model?.Current is { } current) { _located = true; QueueList.ScrollIntoView(current); } };
    }
    private void LocateCurrent(object? sender, RoutedEventArgs args) { if (Model?.Current is { } current) QueueList.ScrollIntoView(current); }
}
