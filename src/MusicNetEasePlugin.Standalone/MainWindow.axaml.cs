using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MusicNetEasePlugin.Features.Main;
using MusicNetEasePlugin.Plugin;
using MyAvaloniaManagement.PluginSdk;

namespace MusicNetEasePlugin.Standalone;

public sealed partial class MainWindow : Window
{
    private readonly PreviewDocumentLifetime _lifetime = new();
    private readonly ServiceProvider _services;
    private readonly MainDocument _document;

    public MainWindow()
    {
        InitializeComponent();
        // 预览与 Host 使用完全相同的业务组合入口，只替换数据目录和 Document 关闭信号。
        var services = new ServiceCollection();
        services.AddMusicNetEasePluginServices(Program.DataDirectory);
        services.AddSingleton<IDocumentLifetime>(_lifetime);
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _document = ActivatorUtilities.CreateInstance<MainDocument>(_services);
        DataContext = _document;
        _document.InitializeAsync(new NewDocumentActivation("网易云音乐"), _lifetime.ClosingToken).GetAwaiter().GetResult();
        Closed += (_, _) =>
        {
            _lifetime.Close();
            _document.Dispose();
            _services.Dispose();
            _lifetime.Dispose();
        };
    }
}
