using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MusicNetEasePlugin.Features.Main;
using MusicNetEasePlugin.Plugin;
using MyAvaloniaManagement.PluginSdk;
using MusicNetEasePlugin.Features.Settings;

namespace MusicNetEasePlugin.Standalone;

public sealed partial class MainWindow : Window
{
    private readonly PreviewDocumentLifetime _lifetime = new();
    private readonly ServiceProvider _services;
    private readonly MainDocument _document;
    private readonly IServiceScope _scope;

    public MainWindow()
    {
        InitializeComponent();
        // 预览与 Host 使用完全相同的业务组合入口，只替换数据目录和 Document 关闭信号。
        var services = new ServiceCollection();
        services.AddMusicNetEasePluginServices(Program.DataDirectory);
        services.AddSingleton<IDocumentLifetime>(_lifetime);
        services.AddSingleton<MusicSettingsTool>();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _scope = _services.CreateScope();
        _document = ActivatorUtilities.CreateInstance<MainDocument>(_scope.ServiceProvider);
        DataContext = _document;
        SettingsView.DataContext = _services.GetRequiredService<MusicSettingsTool>();
        _document.InitializeAsync(new NewDocumentActivation("网易云音乐"), _lifetime.ClosingToken).GetAwaiter().GetResult();
        Closed += (_, _) =>
        {
            _lifetime.Close();
            _document.Dispose();
            _scope.Dispose();
            _services.Dispose();
            _lifetime.Dispose();
        };
    }
}
