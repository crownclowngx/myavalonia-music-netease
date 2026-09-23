using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Main;
using MusicNetEasePlugin.Features.Music;
using MusicNetEasePlugin.Features.Settings;
using MusicNetEasePlugin.Infrastructure.Ui;
using Xunit;
using System.Diagnostics;
using System.Text.Json;

namespace MusicNetEasePlugin.Tests;

/// <summary>
/// V3 使用真实 View 验证有限高度、双色资源和生命周期。只替换账号/音频/磁盘边界，
/// 不复制页面 XAML；截图用于人工审阅，断言负责操作范围与状态正确性。
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class DesktopUiTests
{
    public static AppBuilder BuildAvaloniaApp() => UiCompositionTests.BuildAvaloniaApp();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 真实Document主题尺寸与播放区域验证(bool dark)
    {
        var session = HeadlessSessions.Get(typeof(DesktopUiTests));
        // 返回值选中可等待的异步重载，避免 async void 提前结束测试并吞掉断言。
        await session.Dispatch(async () =>
        {
            await using var fixture = new Fixture();
            var view = new MainView { DataContext = fixture.Document };
            var window = new Window { Content = view, Width = 1200, Height = 720, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show(); await fixture.SignInAsync();
                await fixture.Music.SearchAsync(0, "桌面音乐"); Pump(window);
                fixture.Music.SelectedTrack = fixture.Music.Tracks[0];
                await fixture.Music.PlayCommand.ExecuteAsync(null); Pump(window);
                fixture.Preferences.ReduceMotion = true; await fixture.Preferences.PendingSave; Pump(window);
                var music = view.FindControl<MusicView>("MusicContent")!;
                Assert.Equal(Color.Parse(dark ? "#1E1E1E" : "#FFFFFF"), Brush(view.Background));
                double wideHeight = 0;
                foreach (var size in new[] { (1200, 720), (800, 600), (520, 420) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; Pump(window);
                    foreach (var name in new[] { "SearchInput", "SearchButton", "PlaySelectedButton", "PauseButton", "StopButton", "VolumeSlider", "PlaybackProgress" })
                        AssertInside(music.FindControl<Control>(name)!, music);
                    var volume = music.FindControl<Slider>("VolumeSlider")!;
                    foreach (var thumb in volume.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>())
                        AssertInside(thumb, volume);
                    var list = music.FindControl<ListBox>("TrackList")!;
                    Assert.True(list.Bounds.Height > 50, $"{size}: 结果区域高度不足 {list.Bounds.Height}");
                    if (size.Item1 == 1200) wideHeight = list.Bounds.Height;
                    else Assert.True(wideHeight > list.Bounds.Height);
                    Assert.Same(fixture.Music.Tracks[0], fixture.Music.SelectedTrack);
                    Assert.Single(fixture.Playback.Audio.Opened);
                    Save(window, $"v3-document-{(dark ? "dark" : "light")}-{size.Item1}.png");
                }
                // 原地切换主题，不能重建模型、播放条或选择；固定值不能蒙混通过动态资源检查。
                window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark; Pump(window);
                Assert.Equal(Color.Parse(dark ? "#FFFFFF" : "#1E1E1E"), Brush(view.Background));
                Assert.Same(fixture.Document, view.DataContext);
                Assert.Equal(PlaybackState.Playing, fixture.Playback.Player.Snapshot.State);
                Assert.Single(fixture.Playback.Audio.Opened);
                await fixture.Music.PauseCommand.ExecuteAsync(null); Pump(window);
                Assert.True(music.FindControl<Button>("ResumeButton")!.IsVisible);
                Assert.False(music.FindControl<Button>("PauseButton")!.IsVisible);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 真实Tool宽窄布局和主题切换保留草稿(bool dark)
    {
        var session = HeadlessSessions.Get(typeof(DesktopUiTests));
        // 返回值选中可等待的异步重载，避免 async void 提前结束测试并吞掉断言。
        await session.Dispatch(async () =>
        {
            await using var fixture = new Fixture();
            var view = new MusicSettingsView { DataContext = fixture.Settings };
            var window = new Window { Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show(); await fixture.Settings.EnsureLoadedAsync();
                fixture.Settings.DirectoryPath = @"D:\非常长的公共运行库目录\不会立即加载\LibVLC\尚未保存";
                fixture.Preferences.ReduceMotion = true; await fixture.Preferences.PendingSave;
                foreach (var size in new[] { (280, 900), (320, 900), (420, 900), (640, 240) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; Pump(window);
                    var input = view.FindControl<TextBox>("DirectoryInput")!;
                    Assert.True(input.Bounds.Width > 120);
                    // 竖向滚动允许内容超过低矮 Tool，高度不够也不能出现整页横向溢出。
                    foreach (var name in new[] { "DirectoryInput", "SaveDirectoryButton", "CheckDirectoryButton", "ReduceMotionToggle" })
                    {
                        var control = view.FindControl<Control>(name)!;
                        var point = control.TranslatePoint(default, view)!.Value;
                        Assert.InRange(point.X, 0, size.Item1);
                        Assert.True(point.X + control.Bounds.Width <= size.Item1 + 1);
                    }
                    Save(window, $"v3-tool-{(dark ? "dark" : "light")}-{size.Item1}.png");
                }
                Assert.Equal(Color.Parse(dark ? "#252525" : "#F5F5F5"), Brush(view.Background));
                window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark; Pump(window);
                Assert.Equal(Color.Parse(dark ? "#F5F5F5" : "#252525"), Brush(view.Background));
                Assert.Contains("尚未保存", fixture.Settings.DirectoryPath);
                Assert.False(fixture.Runtime.LoadAttempted);
                Assert.Equal(1, fixture.VlcSettings.Loads);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 登录区深浅主题保留二维码白底与操作(bool dark)
    {
        var session = HeadlessSessions.Get(typeof(DesktopUiTests));
        // 返回值选中可等待的异步重载，避免 async void 提前结束测试并吞掉断言。
        await session.Dispatch(async () =>
        {
            await using var fixture = new Fixture();
            var view = new MainView { DataContext = fixture.Document };
            var window = new Window { Width = 800, Height = 520, Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show(); Pump(window);
                Assert.Equal(Colors.White, Brush(view.FindControl<Border>("QrSurface")!.Background));
                AssertInside(view.FindControl<Border>("QrSurface")!, view);
                AssertInside(view.FindControl<StackPanel>("LoginDetails")!, view);
                Assert.False(view.FindControl<MusicView>("MusicContent")!.IsEffectivelyVisible);
                Save(window, $"v3-login-{(dark ? "dark" : "light")}.png");
                window.Width = 520; window.Height = 420; Pump(window);
                Assert.Equal(1, Grid.GetRow(view.FindControl<StackPanel>("LoginDetails")!));
                Assert.True(view.FindControl<Border>("QrSurface")!.Bounds.Width >= 200);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }

    [Fact]
    public async Task 动效随祖先隐藏最小化偏好和重挂收口且不停止歌曲()
    {
        var session = HeadlessSessions.Get(typeof(DesktopUiTests));
        // 返回值选中可等待的异步重载，避免 async void 提前结束测试并吞掉断言。
        await session.Dispatch(async () =>
        {
            await using var fixture = new Fixture();
            var music = new MusicView { DataContext = fixture.Music };
            var tool = new MusicSettingsView { DataContext = fixture.Settings };
            var parent = new Grid { ColumnDefinitions = new("*,320") }; Grid.SetColumn(tool, 1);
            parent.Children.Add(music); parent.Children.Add(tool);
            var window = new Window { Width = 1200, Height = 720, Content = parent };
            try
            {
                window.Show(); await fixture.Settings.EnsureLoadedAsync();
                await fixture.Music.SearchAsync(0, "测试"); Pump(window);
                fixture.Music.SelectedTrack = fixture.Music.Tracks[0]; await fixture.Music.PlayCommand.ExecuteAsync(null); Pump(window);
                fixture.Settings.DirectoryPath = "保留草稿";
                Assert.True(music.Motion.IsEnabled); Assert.True(tool.Motion.IsEnabled);
                var button = music.FindControl<Button>("SearchButton")!;
                Assert.NotNull(button.Transitions);
                for (var cycle = 0; cycle < 20; cycle++)
                {
                    music.Motion.FadeIn(music.FindControl<Control>("PlaybackState")!);
                    parent.IsVisible = false; Pump(window);
                    Assert.False(music.Motion.IsEnabled); Assert.False(music.Motion.HasActiveFade);
                    Assert.DoesNotContain(button.Transitions ?? [], transition => transition is Avalonia.Animation.BrushTransition);
                    parent.IsVisible = true; Pump(window);
                    window.Content = null; Pump(window);
                    Assert.False(music.Motion.IsEnabled);
                    window.Content = parent; Pump(window);
                }
                window.WindowState = WindowState.Minimized; Pump(window);
                Assert.False(music.Motion.IsEnabled); Assert.False(tool.Motion.IsEnabled);
                window.WindowState = WindowState.Normal; Pump(window);
                tool.FindControl<CheckBox>("ReduceMotionToggle")!.IsChecked = true;
                await fixture.Preferences.PendingSave; Pump(window);
                Assert.False(music.Motion.IsEnabled); Assert.False(tool.Motion.IsEnabled); Assert.DoesNotContain(button.Transitions ?? [], transition => transition is Avalonia.Animation.BrushTransition);
                fixture.Preferences.ReduceMotion = false; await fixture.Preferences.PendingSave; Pump(window);
                Assert.True(music.Motion.IsEnabled);
                window.RequestedThemeVariant = ThemeVariant.Dark; Pump(window);
                Assert.True(music.Motion.IsEnabled); Assert.False(music.Motion.HasActiveFade);
                Assert.Equal("保留草稿", fixture.Settings.DirectoryPath);
                Assert.Single(fixture.Playback.Audio.Opened);
                Assert.Equal(PlaybackState.Playing, fixture.Playback.Player.Snapshot.State);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }

    [Fact]
    public async Task 动效开关成本对照与已脱离视图可回收()
    {
        var session = HeadlessSessions.Get(typeof(DesktopUiTests));
        // 返回值选中可等待的异步重载，避免 async void 提前结束测试并吞掉断言。
        await session.Dispatch(async () =>
        {
            await using var fixture = new Fixture();
            var view = new MusicView { DataContext = fixture.Music };
            var window = new Window { Content = view, Width = 900, Height = 600 };
            var samples = new List<object>();
            try
            {
                window.Show(); await fixture.Music.SearchAsync(0, "本地固定数据"); Pump(window);
                fixture.Music.SelectedTrack = fixture.Music.Tracks[0];
                foreach (var reduced in new[] { false, true })
                {
                    fixture.Preferences.ReduceMotion = reduced; await fixture.Preferences.PendingSave; Pump(window);
                    foreach (var scenario in new[] { "idle", "playing", "hidden" })
                    {
                        view.IsVisible = scenario != "hidden";
                        if (scenario == "idle") await fixture.Playback.Player.StopAsync();
                        if (scenario == "playing") await fixture.Music.PlayCommand.ExecuteAsync(null);
                        Pump(window);
                        // 等真实动画完成，避免把过渡尾帧当作空闲成本；有界等待不引入生产轮询。
                        var settling = Stopwatch.StartNew();
                        while (view.Motion.HasActiveFade && settling.Elapsed < TimeSpan.FromSeconds(2))
                        { await Task.Delay(20); Pump(window); }
                        Assert.False(view.Motion.HasActiveFade);
                        using var process = Process.GetCurrentProcess();
                        var cpu = process.TotalProcessorTime;
                        var allocated = GC.GetTotalAllocatedBytes();
                        var watch = Stopwatch.StartNew();
                        await Task.Delay(500); Pump(window);
                        process.Refresh();
                        samples.Add(new { reducedMotion = reduced, scenario, wallMilliseconds = watch.Elapsed.TotalMilliseconds,
                            cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                            allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, activeFadeAfter = view.Motion.HasActiveFade });
                        Assert.False(view.Motion.HasActiveFade);
                    }
                }
                window.Content = null; Pump(window);
                var weak = new List<WeakReference>();
                for (var i = 0; i < 20; i++) weak.AddRange(CreateAndDetachViews(window, fixture));
                await Task.Delay(50); Pump(window);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Assert.All(weak, reference => Assert.False(reference.IsAlive));
                var root = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS");
                if (!string.IsNullOrWhiteSpace(root))
                {
                    Directory.CreateDirectory(root);
                    await File.WriteAllTextAsync(Path.Combine(root, "v3-motion-observation.json"), JsonSerializer.Serialize(new
                    { schemaVersion = 1, environment = "Avalonia Headless / Skia, fixed offline data", samples,
                        detachedViews = weak.Count, retainedViews = weak.Count(x => x.IsAlive), realHost = false, frameTimeMeasured = false }, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            finally { window.Close(); }
            return true;
        }, default);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateAndDetachViews(Window window, Fixture fixture)
    {
        var music = new MusicView { DataContext = fixture.Music };
        var settings = new MusicSettingsView { DataContext = fixture.Settings };
        var grid = new Grid { ColumnDefinitions = new("*,320") };
        Grid.SetColumn(settings, 1); grid.Children.Add(music); grid.Children.Add(settings);
        window.Content = grid; Pump(window);
        window.Content = null; Pump(window);
        return [new(music), new(settings)];
    }

    [Fact]
    public async Task 宿主同款主题组合下菜单绑定和局部资源独立()
    {
        var session = HeadlessSessions.Get(typeof(HostThemeEnvironment));
        // 返回值选中可等待的异步重载，避免 async void 提前结束测试并吞掉断言。
        await session.Dispatch(async () =>
        {
            await using var fixture = new Fixture();
            var app = Avalonia.Application.Current!;
            var count = app.Resources.Count;
            var view = new MainView { DataContext = fixture.Document };
            var window = new Window { Width = 1000, Height = 600, Content = view, RequestedThemeVariant = ThemeVariant.Dark };
            try
            {
                window.Show(); await fixture.SignInAsync(); await fixture.Music.SearchAsync(0, "测试"); Pump(window);
                Assert.Equal(Color.Parse("#1E1E1E"), Brush(view.Background));
                Assert.Equal(count, app.Resources.Count);
                var button = view.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "账号"));
                var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
                flyout.ShowAt(button); Pump(window);
                var logout = flyout.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "退出 / 清除本地登录信息"));
                Assert.Same(fixture.Document.LogoutCommand, logout.Command);
                Save(window, "v3-host-theme-dark-composition.png");
                flyout.Hide();
                // 同一 View 在窗口间移动后继续继承新窗口主题；不是实际 Dock 拖拽验收。
                window.Content = null;
                var other = new Window { Width = 1000, Height = 600, Content = view, RequestedThemeVariant = ThemeVariant.Light };
                try { other.Show(); Pump(other); Assert.Equal(Colors.White, Brush(view.Background)); }
                finally { other.Close(); }
            }
            finally { window.Close(); }
            return true;
        }, default);
    }

    private static Color Brush(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static void AssertInside(Control control, Visual parent)
    {
        Assert.True(control.IsEffectivelyVisible, control.Name);
        var position = control.TranslatePoint(default, parent)!.Value;
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, control.Name);
        Assert.True(position.X >= -1 && position.Y >= -1 && position.X + control.Bounds.Width <= parent.Bounds.Width + 1 && position.Y + control.Bounds.Height <= parent.Bounds.Height + 1,
            $"{control.Name}: {position}, {control.Bounds.Size} 越过 {parent.Bounds.Size}");
    }
    internal static void Pump(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Save(Window window, string name)
    {
        var root = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(root)) return;
        Directory.CreateDirectory(root);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(root, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly PlaybackFixture Playback = new();
        internal readonly LoginCoordinator Login = TestLogin.Create(new(), new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()) }, TimeProvider.System, LoginOptions.Default);
        internal readonly MusicLifetime Lifetime = new();
        internal readonly UiPreferences Preferences;
        internal readonly MusicWorkspace Music;
        internal readonly MainDocument Document;
        internal readonly MusicSettingsTool Settings;
        internal readonly MemoryVlcSettings VlcSettings = new();
        internal readonly RuntimeStatus Runtime = new();
        internal Fixture()
        {
            var dispatcher = new LoginUiDispatcher();
            Preferences = new(new MemoryUiPreferences(), dispatcher);
            Playback.Catalog.Search = (_, offset, _) => Task.FromResult(new MusicSearchPage(
                Enumerable.Range(1, 30).Select(i => new MusicTrack(i, $"夜空中的旋律 {i:00}", "桌面音乐测试歌手", "留给清晨的专辑", null, 223000 + i * 1000)).ToArray(), offset, false));
            Music = new(Playback.Catalog, Playback.Sessions, Playback.Queue, Login, dispatcher, Lifetime, preferences: Preferences);
            Document = new(Login, dispatcher, new NoImages(), Lifetime, Music, Preferences);
            Settings = new(Login, VlcSettings, new DirectoryProbe(path => new(path, [])), Runtime, dispatcher, Preferences);
        }
        internal async Task SignInAsync() { await Login.RestoreAsync(Guid.NewGuid(), default); Dispatcher.UIThread.RunJobs(); }
        public async ValueTask DisposeAsync()
        { Document.Dispose(); Music.Dispose(); Settings.Dispose(); Preferences.Dispose(); Lifetime.Dispose(); await Login.DisposeAsync(); await Playback.DisposeAsync(); }
    }
    private sealed class NoImages : IAccountImageSource
    { public Task<byte[]?> LoadAsync(string? address, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null); }
}
