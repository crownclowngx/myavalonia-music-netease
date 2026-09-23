using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MusicNetEasePlugin.Features.Main;
using MusicNetEasePlugin.Plugin;
using MyAvaloniaManagement.PluginSdk;
using MusicNetEasePlugin.Features.Settings;
using Avalonia.Styling;

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

    private void ThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox selector)
            RequestedThemeVariant = selector.SelectedIndex switch { 1 => ThemeVariant.Light, 2 => ThemeVariant.Dark, _ => ThemeVariant.Default };
    }

    private void PreviewChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 只调整预览容器；保留同一套生产 View、输入草稿和播放所有者，避免假页面掩盖 Dock 重挂问题。
        if (sender is not ComboBox selector || PreviewLayout is null) return;
        var mode = selector.SelectedIndex;
        DocumentView.IsVisible = mode < 2;
        SettingsView.IsVisible = mode != 1;
        PreviewLayout.ColumnDefinitions = new(mode == 0 ? "*,320" : "*");
        Grid.SetColumn(SettingsView, mode == 0 ? 1 : 0);
        (Width, Height) = mode switch { 1 => (540d, 500d), 2 => (320d, 720d), 3 => (660d, 310d), _ => (1180d, 740d) };
    }
}
