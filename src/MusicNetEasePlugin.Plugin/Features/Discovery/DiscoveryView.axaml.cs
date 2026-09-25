using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using MusicNetEasePlugin.Application.Discovery;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Features.Music;
using System.ComponentModel;
namespace MusicNetEasePlugin.Features.Discovery;
/// <summary>控件只负责命中与导航焦点；实际请求、取消和播放均由页面模型处理。</summary>
public partial class DiscoveryView : UserControl
{
    private DiscoveryWorkspace? _bound;
    public static readonly DirectProperty<DiscoveryView, DiscoveryWorkspace?> ModelProperty = AvaloniaProperty.RegisterDirect<DiscoveryView, DiscoveryWorkspace?>(nameof(Model), v => v.Model);
    public DiscoveryWorkspace? Model => DataContext as DiscoveryWorkspace;
    public DiscoveryView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { RaisePropertyChanged(ModelProperty, null, Model); if (VisualRoot is not null) Bind(); };
        KeyDown += (_, e) =>
        {
            var shell = this.FindAncestorOfType<MusicView>()?.Model;
            if (e.Key == Key.Escape && shell?.Navigation.IsOpen != true && shell?.Library?.IsOpen != true && Model?.BackCommand.CanExecute(null) == true)
            { Model.BackCommand.Execute(null); e.Handled = true; }
        };
        AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) => { if (Model is { } model && e.Source is ScrollViewer viewer) model.ScrollOffset = viewer.Offset.Y; });
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Bind(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Unbind(); base.OnDetachedFromVisualTree(e); }
    private void Bind() { Unbind(); _bound = Model; if (_bound is not null) _bound.PropertyChanged += ModelChanged; }
    private void Unbind() { if (_bound is not null) _bound.PropertyChanged -= ModelChanged; _bound = null; }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DiscoveryWorkspace.ScrollOffset) || Model is not { } model) return;
        foreach (var list in new[] { DiscoverySongs, DiscoveryCards })
            if (list.IsVisible && list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll) scroll.Offset = new(0, model.ScrollOffset);
    }
    private void OpenCard(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MusicCard card } && Model is { } model) { model.SelectedCard = card; model.OpenCardCommand.Execute(null); e.Handled = true; }
    }
}
