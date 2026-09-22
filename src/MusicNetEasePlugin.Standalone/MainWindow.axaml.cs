using Avalonia.Controls;
using MusicNetEasePlugin.Features.Main;
using MyAvaloniaManagement.PluginSdk;

namespace MusicNetEasePlugin.Standalone;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var document = new MainDocument();
        document.InitializeAsync(
            new NewDocumentActivation("MusicNetEasePlugin Standalone"),
            CancellationToken.None).GetAwaiter().GetResult();
        DataContext = document;
    }
}
