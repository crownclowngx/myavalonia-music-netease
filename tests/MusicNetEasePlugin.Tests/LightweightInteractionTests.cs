using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Music;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class LightweightNavigationTests
{
    [Fact, Trait("V5", "N01,N02")]
    public void 队列跨断点保持单一导航且歌词返回原浏览页()
    {
        var navigation = new MusicNavigation(); navigation.Browse(MusicBrowsePage.Library);
        navigation.SetAvailableSize(1200, 650); navigation.ShowQueueCommand.Execute(null);
        Assert.True(navigation.IsQueueBeside); Assert.True(navigation.IsLibrary);
        navigation.ShowLyricsCommand.Execute(null); Assert.True(navigation.IsLyrics); Assert.True(navigation.LibrarySelected);
        // V6 用互斥抽屉替代内容页；原浏览记忆和共享播放器不变。
        navigation.SetAvailableSize(800, 550); Assert.False(navigation.IsBeside); Assert.True(navigation.IsLyrics); Assert.True(navigation.IsLibrary);
        navigation.Back(); Assert.False(navigation.IsOpen); Assert.False(navigation.IsQueue);
        Assert.True(navigation.IsLibrary);
        navigation.ShowQueueCommand.Execute(null); navigation.SetAvailableSize(1120, 549); Assert.False(navigation.IsQueuePage);
        navigation.SetAvailableSize(1120, 550); Assert.True(navigation.IsQueueBeside);
        navigation.Browse(MusicBrowsePage.History); Assert.True(navigation.IsHistory); Assert.True(navigation.IsQueueBeside);
    }

    [Fact, Trait("V5", "Q08,N06,R06")]
    public async Task 队列小操作复用行和选择且清空确认在唯一写入者校验版本()
    {
        await using var f = new PlaybackFixture();
        await f.Queue.ReplaceAsync(Enumerable.Range(1, 10000).Select(i => QueueEntry.FromTrack(MusicCatalog.Track(i))).ToArray(), 0, default);
        using var model = new QueueWorkspace(f.Queue, new ImmediateUi());
        var selected = model.Rows[10]; model.Selected = selected; var first = model.Rows[0]; var reset = 0;
        model.Rows.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) reset++; };
        model.UpCommand.Execute(null); Assert.Same(selected, model.Selected); Assert.Same(selected, model.Rows[9]);
        await f.Queue.NextAsync(false, default); Assert.Same(first, model.Rows[0]); Assert.False(first.IsCurrent); Assert.Equal(0, reset);
        model.RequestClearCommand.Execute(null); Assert.True(model.ConfirmClear);
        await f.Queue.RemoveAsync(model.Rows[^1].Id, default); await model.ConfirmClearCommand.ExecuteAsync(null);
        Assert.Equal(9999, model.Rows.Count); Assert.Contains("变化", model.Message);
        var version = f.Queue.Snapshot.QueueRevision; f.Queue.Move(model.Rows[5].Id, 1);
        await Assert.ThrowsAsync<MusicException>(() => f.Queue.ClearIfUnchangedAsync(version));
        Assert.Equal(9999, model.Rows.Count); model.RequestClearCommand.Execute(null); await model.ConfirmClearCommand.ExecuteAsync(null);
        Assert.Empty(model.Rows); Assert.Equal(PlaybackState.Stopped, f.Queue.Snapshot.Playback.State);
    }

    [Fact, Trait("V5", "Q02,Q06")]
    public async Task 当前歌曲停止与失败重试不插入重复项()
    {
        await using var f = new PlaybackFixture(); var entry = QueueEntry.FromTrack(MusicCatalog.Track(1));
        await f.Queue.PlayNowAsync(entry, default); await f.Queue.StopAsync();
        await f.Queue.PlayNowAsync(QueueEntry.FromTrack(MusicCatalog.Track(1)), default);
        Assert.Single(f.Queue.Snapshot.Entries); Assert.Equal(2, f.Audio.Opened.Count);
        await f.Queue.StopAsync(); var resolve = f.Catalog.Resource;
        f.Catalog.Resource = (_, _) => throw new MusicException(MusicError.Network, "连接失败");
        await f.Queue.PlayNowAsync(QueueEntry.FromTrack(MusicCatalog.Track(1)), default);
        Assert.Equal(PlaybackState.Failed, f.Queue.Snapshot.Playback.State); Assert.Single(f.Queue.Snapshot.Entries);
        f.Catalog.Resource = resolve; await f.Queue.PlayNowAsync(QueueEntry.FromTrack(MusicCatalog.Track(1)), default);
        Assert.Equal(PlaybackState.Playing, f.Queue.Snapshot.Playback.State); Assert.Single(f.Queue.Snapshot.Entries);
    }
}

[Collection("AvaloniaHeadless")]
public sealed class LightweightInteractionTests
{
    [Theory, InlineData(false), InlineData(true), Trait("V5", "Q01,Q08,N01,N02,N03,N04,U01,U02,U03,R06")]
    public async Task 歌曲行键盘菜单中文组合和导航在真实视图中保持一致(bool dark)
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var fixture = new DailyPlayerAcceptanceTests.Fixture(); await fixture.Initialize(); using var page = fixture.Page();
            var searches = 0;
            fixture.Player.Catalog.Search = (_, offset, _) => { searches++; return Task.FromResult(new MusicSearchPage(Enumerable.Range(1, 30).Select(i => MusicCatalog.Track(i)).ToArray(), offset, false)); };
            var window = new Window { Content = page.View, Width = 1200, Height = 720, RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
            try
            {
                window.Show(); DesktopUiTests.Pump(window); var search = page.View.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchInput");
                search.Text = "中文"; search.Focus(); var presenter = search.GetVisualDescendants().OfType<TextPresenter>().Single();
                presenter.PreeditText = "zhong"; Press(window, Key.Enter); Assert.Equal(0, searches);
                presenter.PreeditText = null; Press(window, Key.Enter); DesktopUiTests.Pump(window); Assert.Equal(1, searches);
                var list = page.View.GetVisualDescendants().OfType<SongListBox>().Single(l => l.Name == "TrackList");
                list.SelectedIndex = 1; var item = (ListBoxItem)list.ContainerFromIndex(1)!; Assert.True(item.Focus()); Press(window, Key.Enter); Assert.NotNull(page.Music.PlayCommand.ExecutionTask); await page.Music.PlayCommand.ExecutionTask!; DesktopUiTests.Pump(window);
                Assert.Equal(2, fixture.Player.Queue.Snapshot.Playback.Track!.Id); Assert.Single(fixture.Player.Audio.Opened); Assert.Equal(2, fixture.Player.Queue.Snapshot.Entries.Count);
                var row = item.GetVisualDescendants().OfType<SongRowView>().Single(); Assert.Equal(2, row.CurrentTrackId);
                Press(window, Key.F10, RawInputModifiers.Shift); DesktopUiTests.Pump(window);
                var menu = Assert.IsType<MenuFlyout>(row.FindControl<Button>("MoreButton")!.Flyout); Assert.True(menu.IsOpen);
                var append = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "加入队列"));
                Assert.Same(page.Music.AppendCommand, append.Command); Assert.True(append.IsVisible);
                await page.Music.AppendCommand.ExecuteAsync(null); Assert.Equal(3, fixture.Player.Queue.Snapshot.Entries.Count);
                menu.Hide(); DesktopUiTests.Pump(window);
                list.SelectedIndex = 2; var another = (ListBoxItem)list.ContainerFromIndex(2)!;
                var anotherRow = another.GetVisualDescendants().OfType<SongRowView>().Single();
                Assert.False(anotherRow.FindControl<PathIcon>("CurrentMarker")!.IsVisible); Assert.True(row.FindControl<PathIcon>("CurrentMarker")!.IsVisible);
                // 行内按钮的 Tunnel 选择必须先于命令。一次鼠标操作只打开一份媒体。
                var play = anotherRow.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "立即播放");
                var point = play.TranslatePoint(new Point(16, 16), window)!.Value; window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); await page.Music.PlayCommand.ExecutionTask!; DesktopUiTests.Pump(window);
                Assert.Equal(3, fixture.Player.Queue.Snapshot.Playback.Track!.Id); Assert.Equal(2, fixture.Player.Audio.Opened.Count);
                var chosen = page.Music.SelectedTrack; page.Music.ShowLyricsCommand.Execute(null); await fixture.Lyrics.Pending; DesktopUiTests.Pump(window);
                page.Music.ShowQueueCommand.Execute(null); DesktopUiTests.Pump(window); Assert.True(page.Music.Navigation.IsQueueBeside);
                Save(window, $"v5-detail-{Theme(dark)}-1200.png");
                window.Width = 800; window.Height = 600; DesktopUiTests.Pump(window); Assert.True(page.Music.Navigation.IsQueue); Assert.False(page.Music.Navigation.IsBeside);
                Save(window, $"v5-detail-{Theme(dark)}-800.png");
                page.Music.ShowLyricsCommand.Execute(null); window.Width = 520; window.Height = 420; DesktopUiTests.Pump(window);
                Assert.True(page.Music.Navigation.IsLyrics); Save(window, $"v5-detail-{Theme(dark)}-520.png");
                page.Music.Navigation.Back(); DesktopUiTests.Pump(window); Assert.Same(chosen, page.Music.SelectedTrack); Assert.Equal("中文", page.Music.Keyword);
                Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 15);
                search.Focus(); Press(window, Key.Space); Assert.Equal(PlaybackState.Playing, fixture.Player.Queue.Snapshot.Playback.State);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }

    [Fact, Trait("V5", "L01,L02,R05")]
    public async Task 隐藏歌词和播放条不投递进度刷新且回到页面立即同步()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var fixture = new DailyPlayerAcceptanceTests.Fixture(); await fixture.Initialize(); using var page = fixture.Page();
            var window = new Window { Content = page.View, Width = 800, Height = 600 };
            try
            {
                window.Show(); await page.Music.Player.ResumeCommand.ExecuteAsync(null); await fixture.Lyrics.Pending;
                page.Music.ShowLyricsCommand.Execute(null); DesktopUiTests.Pump(window);
                var changes = 0; page.Music.Lyrics!.PropertyChanged += (_, _) => changes++; page.Music.Player.PropertyChanged += (_, _) => changes++;
                page.Music.Player.Timeline.PropertyChanged += (_, _) => changes++;
                page.View.IsVisible = false; DesktopUiTests.Pump(window); changes = 0;
                for (var i = 1; i < 20; i++) fixture.Player.Audio.Emit(fixture.Player.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, i * 1000);
                DesktopUiTests.Pump(window); Assert.Equal(0, changes);
                page.View.IsVisible = true; DesktopUiTests.Pump(window); Assert.Equal(19, page.Music.Lyrics.CurrentLine); Assert.Contains("00:19", page.Music.Player.PositionText);
                Assert.Single(fixture.Player.Audio.Opened);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    { window.KeyPress(key, modifiers, default, null); window.KeyRelease(key, modifiers, default, null); }

    [Theory, InlineData(false), InlineData(true), Trait("V5", "N05,U03")]
    public async Task 账号菜单打开同一设置模型且故障提示和返回焦点可用(bool dark)
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new DailyPlayerAcceptanceTests.Fixture(); await f.Initialize(); using var page = f.Page();
            using var preferences = new MusicNetEasePlugin.Application.Appearance.UiPreferences(new MemoryUiPreferences(), new ImmediateUi());
            var runtime = new RuntimeStatus(); using var settings = new MusicNetEasePlugin.Features.Settings.MusicSettingsTool(f.Login, new MemoryVlcSettings(), new DirectoryProbe(path => new(path, [])), runtime, new ImmediateUi(), preferences);
            using var life = new MusicLifetime(); using var document = new MusicNetEasePlugin.Features.Main.MainDocument(f.Login, new ImmediateUi(), new LightweightResourceTests.ImmediateImages(_ => null), life, page.Music, preferences, settings);
            var view = new MusicNetEasePlugin.Features.Main.MainView { DataContext = document };
            var window = new Window { Content = view, Width = 800, Height = 600, RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
            try
            {
                window.Show(); DesktopUiTests.Pump(window); var account = view.FindControl<Button>("AccountMenuButton")!;
                var menu = Assert.IsType<MenuFlyout>(account.Flyout); menu.ShowAt(account); DesktopUiTests.Pump(window);
                var open = menu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "账号与播放设置"));
                open.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent)); menu.Hide();
                await settings.EnsureLoadedAsync(); DesktopUiTests.Pump(window);
                Assert.True(view.FindControl<Border>("SettingsOverlay")!.IsVisible);
                Assert.False(view.FindControl<MusicView>("MusicContent")!.IsVisible);
                Assert.Same(settings, view.GetVisualDescendants().OfType<MusicNetEasePlugin.Features.Settings.MusicSettingsView>().Single().DataContext);
                Assert.False(settings.AdvancedExpanded); runtime.LoadAttempted = true; runtime.Notify(); DesktopUiTests.Pump(window);
                Assert.True(settings.AdvancedExpanded); Save(window, $"v5-settings-{Theme(dark)}.png");
                Press(window, Key.Escape); DesktopUiTests.Pump(window); Assert.False(view.FindControl<Border>("SettingsOverlay")!.IsVisible);
                Assert.True(account.IsFocused); Assert.True(view.FindControl<MusicView>("MusicContent")!.IsVisible);
                f.Player.Catalog.Resource = (_, _) => throw new MusicException(MusicError.Network, "网络连接失败，请重试");
                await page.Music.Player.ResumeCommand.ExecuteAsync(null); DesktopUiTests.Pump(window);
                Assert.Equal("播放失败", page.Music.Player.StateText); Assert.Contains("网络连接失败", page.Music.Player.PlaybackMessage);
                var notice = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PlaybackNotice"); Assert.True(notice.IsEffectivelyVisible);
                Save(window, $"v5-failure-{Theme(dark)}.png");
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    private static string Theme(bool dark) => dark ? "dark" : "light";
    internal static void Save(Window window, string name)
    {
        var root = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS"); if (string.IsNullOrEmpty(root)) return;
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(root, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        TestEvidence.Screenshot(name, frame.PixelSize.Width, frame.PixelSize.Height);
    }
}
