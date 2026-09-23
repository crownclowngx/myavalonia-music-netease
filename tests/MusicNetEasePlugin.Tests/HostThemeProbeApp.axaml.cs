using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;

namespace MusicNetEasePlugin.Tests;

public sealed partial class HostThemeProbeApp : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}

public static class HostThemeEnvironment
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HostThemeProbeApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();
}
