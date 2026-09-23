using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Library;
using MusicNetEasePlugin.Features.Main;
using MusicNetEasePlugin.Features.Music;
using MusicNetEasePlugin.Infrastructure.Ui;
using Xunit;

namespace MusicNetEasePlugin.Tests;

/// <summary>组合生产 Document/View 和共享应用服务，仅在账号、HTTP、磁盘和声卡边界提供离线数据。</summary>
[Collection("AvaloniaHeadless")]
public sealed class DailyPlayerAcceptanceTests
{
    [Theory, InlineData(false), InlineData(true), Trait("M2", "P06,U01,U03,U04,U05,H07")]
    public async Task 生产Document三尺寸五面板与静默恢复可操作(bool dark)
    {
        await HeadlessSessions.Get(typeof(DailyPlayerAcceptanceTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); await f.Initialize(); using var page = f.Page();
            var window = new Window { Width = 800, Height = 600, Content = page.View, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show(); Pump(window); Assert.True(page.Document.IsSignedIn);
                Assert.Empty(f.Player.Audio.Opened); Assert.Equal(0, f.Player.Catalog.Resolves);
                Assert.True(page.View.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ResumeButton").IsEnabled);
                Assert.Equal("已暂停", page.Music.Player.StateText); Save(window, $"m2-restored-paused-{Theme(dark)}.png");
                await Click(page.View, "我的歌单"); Pump(window);
                page.Music.Playlists!.SelectedPlaylist = page.Music.Playlists.Playlists.Single(); await page.Music.Playlists.OpenCommand.ExecuteAsync(null); Pump(window);
                await Click(page.View, "播放全部"); await f.Lyrics.Pending; Pump(window);
                Assert.Equal(1000, f.Player.Queue.Snapshot.Entries.Count); Assert.Equal(50, page.Music.Playlists.Tracks.Count);
                f.Player.Audio.Emit(f.Player.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 1500); Pump(window);
                foreach (var (width, height, pane) in new[] { (1200, 720, "我的歌单"), (800, 600, "播放队列"), (520, 420, "歌词") })
                {
                    window.Width = width; window.Height = height; await Click(page.View, pane); Pump(window);
                    foreach (var button in page.View.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Content is string)) Inside(button, page.View);
                    var list = page.View.GetVisualDescendants().OfType<ListBox>().Single(b => b.IsEffectivelyVisible);
                    Assert.True(list.Bounds.Height > 50, $"{pane}: {list.Bounds}");
                    Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 30);
                    Inside(page.View.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "PlaybackProgress"), page.View);
                    Save(window, $"m2-document-{Theme(dark)}-{width}.png");
                }
                await Click(page.View, "最近播放"); Pump(window); Assert.Single(page.Music.History!.Rows);
                var history = page.View.GetVisualDescendants().OfType<HistoryView>().Single();
                var historyList = history.FindControl<ListBox>("HistoryList")!; historyList.SelectedIndex = 0; Pump(window);
                await page.Music.History.AppendCommand.ExecuteAsync(null); Assert.Equal(1001, f.Player.Queue.Snapshot.Entries.Count);
                await page.Music.History.PlayCommand.ExecuteAsync(null); Assert.Equal(1001, f.Player.Queue.Snapshot.Entries.Count);
                await Click(page.View, "搜索"); Pump(window); page.Music.Keyword = "晨光"; await page.Music.SearchCommand.ExecuteAsync(null); Pump(window);
                Assert.Single(page.Music.Tracks); Assert.NotSame(page.Music.NextSearchPageCommand, page.Music.Queue.NextCommand);
                window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark; Pump(window);
                Assert.Same(page.Document, page.View.DataContext); Assert.Single(f.Player.Audio.Opened);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }

    [Fact, Trait("M2", "U01,U02,U04,U05,A03,A04,C06")]
    public async Task 双Document独立浏览共享控制关闭再开及二十轮重挂无残留()
    {
        await HeadlessSessions.Get(typeof(DailyPlayerAcceptanceTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); await f.Initialize(); using var a = f.Page(); using var b = f.Page();
            var first = new Window { Width = 800, Height = 600, Content = a.View }; var second = new Window { Width = 800, Height = 600, Content = b.View };
            var weak = new List<WeakReference>();
            try
            {
                first.Show(); second.Show(); await Click(a.View, "继续"); Pump(first); Pump(second);
                await Click(a.View, "我的歌单"); await Click(b.View, "播放队列"); Pump(first); Pump(second);
                Assert.True(a.Music.Navigation.IsLibrary); Assert.True(b.Music.Navigation.IsQueue); Assert.Empty(b.Music.Playlists!.Playlists);
                Assert.Equal(a.Music.Player.CurrentTrack, b.Music.Player.CurrentTrack);
                await Click(b.View, "暂停"); Pump(first); Pump(second); Assert.Equal("已暂停", a.Music.Player.StateText);
                // 真 ComboBox 键盘输入改变共享模式，分页命令与上一首/下一首不混淆。
                var combo = b.View.GetVisualDescendants().OfType<ComboBox>().Single(c => c.IsEffectivelyVisible);
                Assert.True(combo.Focus()); second.KeyPress(Key.Down, RawInputModifiers.None, default, null); second.KeyRelease(Key.Down, RawInputModifiers.None, default, null); Pump(second); Pump(first);
                Assert.Equal(1, b.Music.Queue.ModeIndex); Assert.Equal(b.Music.Queue.ModeIndex, a.Music.Queue.ModeIndex);
                await Click(a.View, "继续"); first.Close(); a.Dispose(); second.Close(); b.Dispose();
                Assert.Equal(PlaybackState.Playing, f.Player.Queue.Snapshot.Playback.State); Assert.Single(f.Player.Audio.Opened);
                var host = new Window { Width = 800, Height = 600 }; host.Show();
                try { for (var i = 0; i < 20; i++) weak.Add(MountAndClose(host, f)); }
                finally { host.Close(); }
                Pump(second); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Assert.All(weak, reference => Assert.False(reference.IsAlive)); Assert.Single(f.Player.Audio.Opened);
                await f.Player.Queue.DisposeAsync(); await f.Lyrics.DisposeAsync(); await f.Player.Persistence.DisposeAsync();
                Assert.Null(f.Player.Audio.Current);
                WriteJson("m2-lifetime.json", new { schemaVersion = 1, environment = "Avalonia Headless / Skia, production Document and application services", realHost = false,
                    mountedDocuments = 22, detachedViews = weak.Count, retainedViews = weak.Count(w => w.IsAlive), audioOpens = f.Player.Audio.Opened.Count,
                    stoppedAfterShutdown = f.Player.Audio.Current is null, noAutomaticResume = true });
            }
            finally { first.Close(); second.Close(); }
            return true;
        }, default);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference MountAndClose(Window window, Fixture fixture)
    {
        using var page = fixture.Page(); window.Content = page.View; Pump(window);
        Assert.Equal("播放中", page.Music.Player.StateText); Assert.Single(fixture.Player.Audio.Opened);
        window.Content = null; Pump(window); return new(page.View);
    }
    private static void Pump(Window window) => DesktopUiTests.Pump(window);
    private static string Theme(bool dark) => dark ? "dark" : "light";
    private static async Task Click(Control root, string content)
    {
        var button = root.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && (Equals(b.Content, content) || Avalonia.Automation.AutomationProperties.GetName(b) == content));
        Assert.True(button.IsEnabled, content); Assert.NotNull(button.Command);
        if (button.Command is IAsyncRelayCommand asyncCommand) await asyncCommand.ExecuteAsync(button.CommandParameter);
        else button.Command.Execute(button.CommandParameter);
    }
    private static void Inside(Control control, Visual root)
    {
        var point = control.TranslatePoint(default, root)!.Value;
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, $"{control}: zero bounds {control.Bounds}");
        Assert.True(point.X >= -1 && point.Y >= -1 && point.X + control.Bounds.Width <= root.Bounds.Width + 1 && point.Y + control.Bounds.Height <= root.Bounds.Height + 1, $"{control}: {point} / {control.Bounds.Size} outside {root.Bounds.Size}");
    }
    private static void Save(Window window, string name)
    {
        var root = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS"); if (string.IsNullOrEmpty(root)) return;
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(root, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        TestEvidence.Screenshot(name, frame.PixelSize.Width, frame.PixelSize.Height);
    }
    private static void WriteJson(string name, object value)
    {
        TestEvidence.Write(name, value);
    }
    internal sealed class Fixture : IAsyncDisposable
    {
        internal readonly PlaybackPersistenceTests.Fixture Player;
        internal readonly LoginCoordinator Login = TestLogin.Create(new(), new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()) }, TimeProvider.System, LoginOptions.Default);
        internal readonly LyricsCoordinator Lyrics;
        internal readonly PlaylistTests.PlaylistFake Playlists = new();
        internal Fixture()
        {
            var store = new PlaybackPersistenceTests.MemoryStore(); store.Data[123] = PlaybackPersistenceTests.State(123, 1, 3000); Player = new(store);
            Lyrics = new(Player.Queue, Player.Sessions, new LyricsTests.LyricsFake { Get = (_, _) => Task.FromResult(new RawLyrics(string.Join('\n', Enumerable.Range(0, 1000).Select(i => $"[{i / 60:00}:{i % 60:00}]晨光里的旋律 · 第 {i + 1} 行")))) });
            Playlists.Pages = (_, _) => Task.FromResult(new PlaylistPage([new(7, "日常聆听 · 一千首测试曲目", null, 1000, true)], 1, false));
            Playlists.Detail = (id, _) => Task.FromResult(new PlaylistTracks(id, "日常聆听", Enumerable.Range(1, 1000).Select(i => (long)i).ToArray(), true, ""));
        }
        internal async Task Initialize() { await Login.RestoreAsync(Guid.NewGuid(), default); await Player.Activate(); }
        internal Page Page() => new(this);
        public async ValueTask DisposeAsync() { await Lyrics.DisposeAsync(); await Player.DisposeAsync(); await Login.DisposeAsync(); }
    }
    internal sealed class Page : IDisposable
    {
        private readonly MusicLifetime _life = new();
        internal MusicWorkspace Music { get; } internal MainDocument Document { get; } internal MainView View { get; }
        internal Page(Fixture f)
        {
            var ui = new LoginUiDispatcher(); var browser = new PlaylistBrowser(f.Playlists, f.Player.Sessions, ui, f.Player.Queue);
            Music = new(f.Player.Catalog, f.Player.Sessions, f.Player.Queue, f.Login, ui, _life, playlists: browser, lyrics: f.Lyrics, persistence: f.Player.Persistence);
            Document = new(f.Login, ui, new NoImages(), _life, Music); View = new() { DataContext = Document };
        }
        private bool _closed;
        public void Dispose() { if (_closed) return; _closed = true; Document.Dispose(); Music.Dispose(); _life.Dispose(); }
    }
    private sealed class NoImages : IAccountImageSource
    { public Task<byte[]?> LoadAsync(string? address, CancellationToken ct) => Task.FromResult<byte[]?>(null); }
}
