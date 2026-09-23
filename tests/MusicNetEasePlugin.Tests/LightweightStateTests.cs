using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Library;
using MusicNetEasePlugin.Features.Main;
using MusicNetEasePlugin.Features.Music;
using MusicNetEasePlugin.Features.Settings;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class LightweightStateTests
{
    [Fact, Trait("V5", "N05")]
    public async Task 单一扫码入口跟随当前方式且取消旧二维码后可重新生成()
    {
        await using var login = TestLogin.Create(new(), new(), new FakeTimeProvider(), LoginOptions.Default);
        using var life = new MusicLifetime();
        using var document = new MainDocument(login, new ImmediateUi(), new LightweightResourceTests.ImmediateImages(_ => null), life);
        var first = document.LoginCommand.ExecuteAsync(null); Assert.Equal(LoginMethod.WeChat, login.Snapshot.Method);
        Assert.NotNull(document.QrImageBytes); Assert.Equal("刷新二维码", document.LoginAction);
        document.LoginMethodIndex = 1; await first; Assert.Null(document.QrImageBytes);
        var next = document.LoginCommand.ExecuteAsync(null); Assert.Equal(LoginMethod.NeteaseApp, login.Snapshot.Method);
        Assert.Contains("网易云音乐 App", document.QrInstruction); Assert.NotNull(document.QrImageBytes);
        document.CancelLoginCommand.Execute(null); await next; Assert.Null(document.QrImageBytes);
    }

    [Fact, Trait("V5", "N05")]
    public async Task 正常运行库收起诊断故障展开且不混淆保存与实际加载值()
    {
        await using var login = TestLogin.Create(new(), new(), TimeProvider.System, LoginOptions.Default);
        var runtime = new RuntimeStatus(); var store = new MemoryVlcSettings();
        using var settings = new MusicSettingsTool(login, store, new DirectoryProbe(path => new(path, [])), runtime, new ImmediateUi());
        await settings.EnsureLoadedAsync(); Assert.False(settings.AdvancedExpanded); Assert.Equal("播放库可用", settings.RuntimeSummary);
        settings.DirectoryPath = "next-runtime"; await settings.SaveCommand.ExecuteAsync(null);
        Assert.Equal("next-runtime", settings.SavedDirectory); Assert.Null(runtime.ActiveDirectory);
        runtime.LoadAttempted = true; runtime.Notify(); Assert.True(settings.AdvancedExpanded); Assert.Contains("失败", settings.RuntimeSummary);
        runtime.ActiveDirectory = "loaded-runtime"; runtime.Notify(); Assert.Contains("loaded-runtime", settings.ActiveRuntime);
        Assert.Equal("next-runtime", settings.SavedDirectory);
    }

    [Fact, Trait("V5", "P02")]
    public async Task 历史使用指定本地时区跨日刷新且浏览不会产生播放记录()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 23, 2, 0, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test-east8", TimeSpan.FromHours(8), "东八区", "东八区"));
        var store = new PlaybackPersistenceTests.MemoryStore();
        var today = new DateTimeOffset(2026, 9, 22, 22, 0, 0, TimeSpan.Zero);
        store.Data[123] = PlaybackPersistenceTests.State(123, 1, 3000) with
        { Recent = [new(1, "今天", "歌手", "专辑", 10000, today), new(2, "昨天", "歌手", "专辑", 10000, today.AddDays(-1)), new(3, "更早", "歌手", "专辑", 10000, today.AddDays(-3))] };
        await using var f = new PlaybackPersistenceTests.Fixture(store); await f.Activate();
        using var history = new HistoryWorkspace(f.Persistence, f.Queue, new ImmediateUi(), time);
        Assert.Equal(new[] { "今天", "昨天", "更早" }, history.Rows.Select(r => r.GroupTitle));
        history.Selected = history.Rows[0]; time.Advance(TimeSpan.FromDays(1)); history.RefreshDate();
        Assert.Equal(new[] { "昨天", "更早", "" }, history.Rows.Select(r => r.GroupTitle)); Assert.Equal(1, history.Selected!.TrackId);
        Assert.Equal(3, f.Persistence.Snapshot.Recent.Count); Assert.Empty(f.Audio.Opened);
    }

    [Fact, Trait("V5", "N06,R04")]
    public async Task 成功反馈替换旧延迟且隐藏和释放立即收口()
    {
        var time = new FakeTimeProvider(); using var notice = new TransientNotice(new ImmediateUi(), time);
        notice.Show("第一项"); var old = notice.Pending; time.Advance(TimeSpan.FromSeconds(2)); notice.Show("第二项");
        await old; Assert.Equal("第二项", notice.Text);
        time.Advance(TimeSpan.FromSeconds(1)); Assert.Equal("第二项", notice.Text);
        time.Advance(TimeSpan.FromSeconds(2)); await notice.Pending; Assert.Empty(notice.Text);
        notice.Show("第三项"); notice.Clear(); await notice.Pending; Assert.Empty(notice.Text);
    }

    [Fact, Trait("V5", "Q01,N01,N06")]
    public async Task 歌单过滤只针对已加载项且行播放保留队列和返回详情状态()
    {
        await using var f = new PlaybackFixture(); var api = new PlaylistTests.PlaylistFake
        { Pages = (_, _) => Task.FromResult(new PlaylistPage([new(1, "自建", null, 1, true), new(2, "收藏", null, 1, false)], 2, false)), Detail = (id, _) => Task.FromResult(new PlaylistTracks(id, "歌单", [9], true, "")) };
        using var browser = new PlaylistBrowser(api, f.Sessions, new ImmediateUi(), f.Queue);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default); await browser.LoadPlaylistsAsync(true);
        browser.FilterIndex = 2; browser.SelectedPlaylist = Assert.Single(browser.VisiblePlaylists);
        await browser.OpenCommand.ExecuteAsync(null); browser.SelectedTrack = browser.Tracks[0]; await browser.PlayNowCommand.ExecuteAsync(null);
        Assert.Equal(new long[] { 1, 9 }, f.Queue.Snapshot.Entries.Select(e => e.TrackId)); browser.BackCommand.Execute(null);
        Assert.Equal(2, browser.FilterIndex); Assert.Equal(2, browser.SelectedPlaylist.Id); Assert.Single(browser.VisiblePlaylists); Assert.Single(browser.Tracks);
        f.Sessions.Revoke(); Assert.Empty(browser.VisiblePlaylists); Assert.Empty(browser.Tracks); Assert.True(browser.IsList);
    }
}
