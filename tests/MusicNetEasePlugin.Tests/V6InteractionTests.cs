using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Library;
using MusicNetEasePlugin.Features.Music;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class V6InteractionTests
{
    [Fact, Trait("V6", "L02,P04")]
    public async Task 歌词定位保留暂停且换曲和试听映射不可信时拒绝旧行()
    {
        await using var f = new PlaybackFixture();
        var api = new LyricsTests.LyricsFake { Get = (_, _) => Task.FromResult(new RawLyrics("[00:01]第一句\n[00:03]第二句")) };
        await using var coordinator = new LyricsCoordinator(f.Queue, f.Sessions, api);
        using var lyrics = new LyricsWorkspace(coordinator, new ImmediateUi(), player: f.Queue); lyrics.SetVisible(true);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default); await coordinator.Pending;
        await f.Queue.PauseAsync(true, default); lyrics.Selected = lyrics.Lines[1];
        Assert.True(lyrics.JumpCommand.CanExecute(null)); await lyrics.JumpCommand.ExecuteAsync(null);
        Assert.Equal(3000, Assert.Single(f.Audio.SeekPositions)); Assert.Equal(PlaybackState.Paused, f.Queue.Snapshot.Playback.State);
        Assert.Equal("第二句", lyrics.PreviewTextAt(3500));
        var old = lyrics.Selected;
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(2), default); await coordinator.Pending;
        lyrics.Selected = old; Assert.False(lyrics.JumpCommand.CanExecute(null)); await lyrics.JumpCommand.ExecuteAsync(null);
        Assert.Single(f.Audio.SeekPositions);
        f.Catalog.Resource = (id, _) => Task.FromResult(new PlaybackResource(id, new("https://m1.music.126.net/fixture"), "mp3", "standard", true, 30000, 40000));
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(3), default); await coordinator.Pending; lyrics.Selected = lyrics.Lines[0];
        Assert.False(lyrics.JumpCommand.CanExecute(null)); Assert.Empty(lyrics.PreviewTextAt(2000));
        f.Sessions.Revoke(); Assert.False(lyrics.JumpCommand.CanExecute(null)); Assert.Empty(lyrics.Lines);
    }

    [Fact, Trait("V6", "P01,P04")]
    public async Task 时间预览不提交定位且未知时长与切歌清除草稿()
    {
        await using var f = new PlaybackFixture(); using var timeline = new TimelineWorkspace(f.Queue, new ImmediateUi());
        Assert.Contains("选择歌曲", timeline.SeekReason);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        timeline.PreviewLyrics = _ => "预览歌词"; timeline.PreviewAtRatio(.5);
        Assert.Contains("00:59", timeline.PreviewText); Assert.Contains("预览歌词", timeline.PreviewText); Assert.Empty(f.Audio.SeekPositions);
        timeline.Begin(); timeline.Position = 8000; Assert.StartsWith("00:08", timeline.PreviewText);
        await timeline.CommitAsync(); Assert.Equal(8000, Assert.Single(f.Audio.SeekPositions));
        timeline.Begin(); timeline.Position = 9000;
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(2), default); await timeline.CommitAsync(); Assert.Single(f.Audio.SeekPositions);
        f.Audio.DurationMs = 0; f.Catalog.Detail = (id, _) => Task.FromResult(MusicCatalog.Track(id) with { DurationMs = 0 });
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(3) with { DurationMs = 0 }, default);
        timeline.PreviewAtRatio(.5); Assert.Contains("时长未知", timeline.PreviewText); Assert.False(timeline.CanSeek);
    }

    [Fact, Trait("V6", "P02,P03")]
    public async Task 追加结果只报告本次数量而恢复和换曲不会伪报入队()
    {
        var store = new PlaybackPersistenceTests.MemoryStore(); store.Data[123] = PlaybackPersistenceTests.State(123, 1, 3000);
        await using var f = new PlaybackPersistenceTests.Fixture(store);
        using var player = new PlayerBarWorkspace(f.Queue, new ImmediateUi(), null, null); await f.Activate();
        Assert.True(player.ShowRestoration); Assert.Contains("00:03", player.RestorationText); Assert.Empty(player.Notice.Text);
        player.DismissRestorationCommand.Execute(null); Assert.False(player.ShowRestoration); Assert.Empty(f.Audio.Opened);
        var next = await f.Queue.EnqueueAsync([QueueEntry.FromTrack(MusicCatalog.Track(2))], true, default);
        Assert.Equal(1, next.Added); Assert.Contains("当前歌曲后", next.Message);
        var append = await f.Queue.EnqueueAsync([QueueEntry.FromTrack(MusicCatalog.Track(2)), QueueEntry.FromTrack(MusicCatalog.Track(3))], false, default);
        Assert.Equal(2, append.Added); Assert.Contains("本次 2 首", append.Message); Assert.Equal(4, f.Queue.Snapshot.Entries.Count);
        Assert.Empty(player.Notice.Text); await player.ResumeCommand.ExecuteAsync(null); Assert.False(player.ShowRestoration); Assert.Single(f.Audio.Opened);
        await f.Queue.NextAsync(false, default); Assert.Empty(player.Notice.Text);
    }

    [Fact, Trait("V6", "B01,R01")]
    public async Task 延迟加载快速完成不闪烁且完成关闭后迟到回调不能复活()
    {
        var time = new FakeTimeProvider(); using var busy = new DelayedBusy(new ImmediateUi(), time);
        busy.Set(true); time.Advance(TimeSpan.FromMilliseconds(100)); Assert.False(busy.IsVisible);
        busy.Set(false); await busy.Pending; time.Advance(TimeSpan.FromSeconds(1)); Assert.False(busy.IsVisible);
        busy.Set(true); time.Advance(TimeSpan.FromMilliseconds(150)); await busy.Pending; Assert.True(busy.IsVisible);
        busy.Set(false); Assert.False(busy.IsVisible);
        busy.Set(true); busy.Dispose(); time.Advance(TimeSpan.FromSeconds(1)); await busy.Pending; Assert.False(busy.IsVisible);
    }

    [Fact, Trait("V6", "B02,P02")]
    public async Task 歌单头区分总数本页与缓存并保持翻页简介及准确追加反馈()
    {
        await using var f = new PlaybackFixture(); var api = new PlaylistTests.PlaylistFake
        {
            Detail = (id, _) => Task.FromResult(new PlaylistTracks(id, "测试歌单", Enumerable.Range(1, 105).Select(i => (long)i).ToArray(), true, "", "https://p1.music.126.net/test", "完整简介"))
        };
        using var browser = new PlaylistBrowser(api, f.Sessions, new ImmediateUi(), f.Queue);
        await browser.OpenAsync(7); Assert.Contains("共 105 首 · 本页 50 首", browser.CountText); Assert.Equal("完整简介", browser.Description); Assert.NotNull(browser.Cover);
        browser.DescriptionExpanded = true; await browser.LoadTracksAsync(100); Assert.True(browser.DescriptionExpanded);
        Assert.Contains("本页 5 首", browser.CountText); browser.SelectedTrack = browser.Tracks[0]; await browser.AppendCommand.ExecuteAsync(null);
        Assert.Contains("本次 1 首", browser.Message); await browser.OpenAsync(8); Assert.False(browser.DescriptionExpanded);
        f.Sessions.Revoke(); Assert.Null(browser.Cover); Assert.Empty(browser.CountText);
    }
}
