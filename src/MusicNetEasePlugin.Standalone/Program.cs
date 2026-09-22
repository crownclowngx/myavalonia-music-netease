using Avalonia;

namespace MusicNetEasePlugin.Standalone;

internal static class Program
{
    // 默认与真实 Host 分离，又可跨启动恢复；手动联调可指定测试拥有的数据目录。
    public static string DataDirectory { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyAvaloniaManagement", "Standalone", "myavalonia.plugin.music.netease");

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--data-dir") DataDirectory = Path.GetFullPath(args[1]);
        else if (args.Length != 0) throw new ArgumentException("仅支持可选参数 --data-dir <独立数据目录>。");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
