using Avalonia;
using Avalonia.Controls;
namespace MusicNetEasePlugin.Features.Music;
public partial class HistoryView : UserControl
{
    public static readonly DirectProperty<HistoryView, HistoryWorkspace?> ModelProperty = AvaloniaProperty.RegisterDirect<HistoryView, HistoryWorkspace?>(nameof(Model), view => view.Model);
    public HistoryWorkspace? Model => DataContext as HistoryWorkspace;
    public HistoryView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => RaisePropertyChanged(ModelProperty, null, Model);
        var visibility = new MusicNetEasePlugin.Infrastructure.Ui.ViewMotion(this);
        visibility.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(visibility.IsActive) && visibility.IsActive) Model?.RefreshDate(); };
    }
}
