using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Discovery;
using MusicNetEasePlugin.Features.Library;
using MusicNetEasePlugin.Features.Music;
using MusicNetEasePlugin.Infrastructure.Persistence;
using MusicNetEasePlugin.Infrastructure.Ui;
using MusicNetEasePlugin.Plugin;
using Xunit;
namespace MusicNetEasePlugin.Tests;

/// <summary>在生产音乐外壳中实际命中按钮、渲染布局；HTTP 和声卡替换，不创建替代产品界面。</summary>
[Collection("AvaloniaHeadless")]
public sealed class DiscoveryUiTests
{
    [Fact, Trait("V8", "W04,W06,R01")]
    public async Task 长列表作品返回恢复实际视口且不逐行读取()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); f.Api.Daily = _ => Task.FromResult(new CatalogPage<MusicTrack>(Enumerable.Range(1, 200).Select(i => DiscoveryFake.Track(i)).ToArray()));
            using var page = f.Page(); var window = new Window { Width = 800, Height = 600, Content = page.View }; window.Show();
            try
            {
                await Click(page.View, "发现"); Pump(window); var view = page.View.GetVisualDescendants().OfType<DiscoveryView>().Single();
                var list = view.FindControl<ListBox>("DiscoverySongs")!; var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
                scroll.Offset = new(0, 800); Pump(window); var expected = scroll.Offset.Y; Assert.True(expected > 100);
                await page.Discovery.NavigateAsync(DiscoveryPage.Album, 91); Pump(window); await page.Discovery.BackCommand.ExecuteAsync(null); Pump(window);
                Assert.InRange(Math.Abs(scroll.Offset.Y - expected), 0, 1); Assert.Equal(2, f.Api.Reads); Assert.Empty(f.Playback.Audio.Opened);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    [Fact, Trait("V8", "U01,U02,U03,U04,P04,D03,W01")]
    public async Task 生产发现菜单作品返回与FM按钮不混淆播放和反馈意图()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); using var page = f.Page(); var window = new Window { Width = 800, Height = 600, Content = page.View }; window.Show();
            try
            {
                await Click(page.View, "发现"); Pump(window); Assert.Equal(2, page.Discovery.Songs.Count); Assert.Empty(f.Playback.Audio.Opened);
                page.Discovery.Selected = page.Discovery.Songs[0]; await Click(page.View, "播放全部 · 2 首"); Assert.Equal(2, f.Playback.Queue.Snapshot.Entries.Count);
                var opens = f.Playback.Audio.Opened.Count;
                var songRow = page.View.GetVisualDescendants().OfType<SongRowView>().First(row => row.IsEffectivelyVisible); songRow.OpenMenu(); Pump(window);
                var menu = (MenuFlyout)songRow.FindControl<Button>("MoreButton")!.Flyout!;
                var worksItem = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "查看歌手 / 专辑"));
                worksItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await page.Model.OpenTrackWorksCommand.ExecutionTask!;
                Pump(window); Assert.Equal(3, page.Discovery.Cards.Count);
                page.Discovery.SelectedCard = page.Discovery.Cards[0]; await page.Discovery.OpenCardCommand.ExecuteAsync(null); Pump(window); Assert.True(page.Discovery.IsArtist);
                await Click(page.View, "返回"); Pump(window); Assert.Equal(DiscoveryPage.Works, page.Discovery.Page); Assert.Equal(opens, f.Playback.Audio.Opened.Count);
                page.Discovery.SelectedCard = page.Discovery.Cards[0]; await page.Discovery.OpenCardCommand.ExecuteAsync(null); Pump(window);
                var back = page.View.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && Equals(b.Content, "返回")); Assert.True(back.Focus());
                page.Model.ShowQueueCommand.Execute(null); Pump(window); LibraryUiTests.PressKey(window, Key.Escape);
                Assert.False(page.Model.Navigation.IsOpen); Assert.True(page.Discovery.IsArtist); Assert.True(back.Focus());
                LibraryUiTests.PressKey(window, Key.Escape); Assert.Equal(DiscoveryPage.Works, page.Discovery.Page);
                await Click(page.View, "私人 FM"); await Click(page.View, "开始私人 FM"); Pump(window); Assert.NotEqual(Guid.Empty, f.Playback.Queue.Snapshot.FmSessionId);
                await Click(page.View, "下一首"); Assert.Empty(f.Playback.Api.Writes); f.Playback.Api.Feedback = (_, _) => Task.FromResult(new FmFeedbackResult(FmFeedbackState.Uncertain, "反馈结果未确认，未自动重发。"));
                await Click(page.View, "不喜欢这首"); Pump(window); Assert.Single(f.Playback.Api.Writes); Assert.Contains("未确认", page.Discovery.Fm!.Message);
                var button = page.View.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DislikeFmButton"); Assert.True(button.Focus()); Assert.Same(button, window.FocusManager!.GetFocusedElement());
                Assert.False(page.Model.Queue.CanEdit); await Click(page.View, "结束 FM"); Pump(window); Assert.True(page.Model.Queue.CanEdit);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    [Fact, Trait("V8", "U01,U03,U05,R01")]
    public async Task 五页面三尺寸双色两密度真实布局及十六张截图()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); using var page = f.Page(); var window = new Window { Content = page.View }; window.Show(); var rows = new List<object>(); var screenshots = new HashSet<string>();
            try
            {
                foreach (var dark in new[] { false, true }) foreach (var (width, height) in new[] { (1200, 720), (800, 600), (520, 420) }) foreach (var comfortable in new[] { false, true })
                {
                    window.Width = width; window.Height = height; window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; f.Preferences.ComfortableDensity = comfortable;
                    foreach (var target in new[] { DiscoveryPage.Daily, DiscoveryPage.ArtistSongs, DiscoveryPage.Album, DiscoveryPage.Fm, DiscoveryPage.Recent })
                    {
                        page.Model.Navigation.Browse(MusicBrowsePage.Discovery); await page.Discovery.NavigateAsync(target, target == DiscoveryPage.ArtistSongs ? 81 : target == DiscoveryPage.Album ? 91 : 0); Pump(window);
                        var view = page.View.GetVisualDescendants().OfType<DiscoveryView>().Single();
                        var controls = view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Content is not null).ToArray(); Assert.NotEmpty(controls);
                        var overflow = controls.Select(b => Math.Max(0, (b.TranslatePoint(default, view)?.X ?? 0) + b.Bounds.Width - view.Bounds.Width)).DefaultIfEmpty().Max();
                        Assert.InRange(overflow, 0, 1); Assert.All(controls, b => Assert.True(b.Bounds.Width > 0 && b.Bounds.Height > 0));
                        if (target != DiscoveryPage.Fm) Assert.True(view.FindControl<ListBox>("DiscoverySongs")!.Bounds.Height > 40);
                        rows.Add(new { page = target.ToString(), width, height, dark, comfortable, horizontalOverflow = Math.Max(0, overflow - 1), buttons = controls.Length });
                        var theme = dark ? "dark" : "light";
                        string? name = target switch
                        {
                            DiscoveryPage.Daily when !comfortable => $"v8-discovery-{theme}-{width}.png",
                            DiscoveryPage.ArtistSongs when width == 800 && comfortable => $"v8-artist-{theme}.png",
                            DiscoveryPage.Album when width == 800 && comfortable => $"v8-album-{theme}.png",
                            DiscoveryPage.Fm when width == 800 && !comfortable => $"v8-fm-{theme}.png",
                            DiscoveryPage.Recent when width == 520 && !comfortable => $"v8-remote-history-{theme}.png", _ => null
                        };
                        if (name is not null)
                        {
                            if (target == DiscoveryPage.Fm) { await f.Playback.Queue.StartFmAsync(default); Pump(window); }
                            Save(window, name); screenshots.Add(name);
                            if (target == DiscoveryPage.Fm) { await f.Playback.Queue.ClearAsync(); Pump(window); }
                        }
                    }
                }
                foreach (var dark in new[] { false, true })
                {
                    window.Width = 800; window.Height = 600; window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; await page.Discovery.NavigateAsync(DiscoveryPage.Fm);
                    await f.Playback.Queue.StartFmAsync(default); f.Playback.Api.Feedback = (_, _) => Task.FromResult(new FmFeedbackResult(FmFeedbackState.Uncertain, "反馈结果未确认，未自动重发。"));
                    await page.Discovery.Fm!.DislikeCommand.ExecuteAsync(null); Pump(window); var name = $"v8-fm-uncertain-{(dark ? "dark" : "light")}.png"; Save(window, name); screenshots.Add(name);
                    f.Playback.Queue.EndFm();
                }
                Assert.Equal(60, rows.Count); Assert.Equal(16, screenshots.Count);
                TestEvidence.Write("v8-discovery-layout.json", new { schemaVersion = 1, realHost = false, screenshots = screenshots.Count, combinations = rows });
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    [Fact, Trait("V8", "R02,R03")]
    public void 服务注册保持共享FM及播放器与独立浏览模型()
    {
        using var directory = new TestDirectory(); var services = new ServiceCollection(); services.AddMusicNetEasePluginServices(directory.Path);
        using var provider = services.BuildServiceProvider(); using var a = provider.CreateScope(); using var b = provider.CreateScope();
        Assert.Same(a.ServiceProvider.GetRequiredService<PrivateFmCoordinator>(), b.ServiceProvider.GetRequiredService<PrivateFmCoordinator>());
        Assert.Same(provider.GetRequiredService<IPlayerSession>(), provider.GetRequiredService<IPrivateFmPlayer>());
        Assert.NotSame(a.ServiceProvider.GetRequiredService<DiscoveryWorkspace>(), b.ServiceProvider.GetRequiredService<DiscoveryWorkspace>());
    }
    private static async Task Click(Control root, string content)
    {
        var button = root.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && Equals(b.Content, content)); Assert.True(button.IsEffectivelyEnabled, content);
        await LibraryUiTests.Click(button);
    }
    private static void Pump(Window w) => DesktopUiTests.Pump(w);
    private static void Save(Window window, string name)
    {
        var root = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS"); if (string.IsNullOrEmpty(root)) return;
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(root, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); TestEvidence.Screenshot(name, frame.PixelSize.Width, frame.PixelSize.Height);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public FmFixture Playback { get; } = new(); public DiscoveryFake Api { get; } = new();
        public LoginCoordinator Login { get; } = TestLogin.Create(new(), new MemorySessionStore(), TimeProvider.System, LoginOptions.Default);
        private readonly TestDirectory _dir = new(); public UiPreferences Preferences { get; }
        public Fixture() { Preferences = new(new UiPreferencesStore(_dir.Path), new LoginUiDispatcher()) { ReduceMotion = true }; Playback.Catalog.Detail = (id, _) => Task.FromResult(DiscoveryFake.Track(id)); }
        public Page Page() => new(this);
        public async ValueTask DisposeAsync() { Preferences.Dispose(); await Playback.DisposeAsync(); await Login.DisposeAsync(); _dir.Dispose(); }
    }
    private sealed class Page : IDisposable
    {
        private readonly MusicLifetime _life = new(); public DiscoveryWorkspace Discovery { get; } public MusicWorkspace Model { get; } public MusicView View { get; }
        public Page(Fixture f)
        {
            var ui = new LoginUiDispatcher(); Discovery = new(f.Api, f.Api, f.Api, f.Playback.Catalog, f.Playback.Sessions, f.Playback.Queue, ui)
            { Fm = new(f.Playback.Fm, f.Playback.Queue, f.Playback.Queue, ui) };
            Model = new(f.Playback.Catalog, f.Playback.Sessions, f.Playback.Queue, f.Login, ui, _life, preferences: f.Preferences, discovery: Discovery);
            View = new() { DataContext = Model };
        }
        public void Dispose() { Model.Dispose(); _life.Dispose(); }
    }
}
