using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Discovery;
using Xunit;
namespace MusicNetEasePlugin.Tests;

public sealed class DiscoveryWorkspaceTests
{
    [Fact, Trait("V8", "A02,D06,C03")]
    public async Task 读取限流遵守等待窗口且导航证据来自实际队列和调用()
    {
        await using var f = new PlaybackFixture(); var api = new DiscoveryFake(); var time = new FakeTimeProvider(); using var page = Page(f, api, time);
        api.Daily = _ => throw new MusicException(MusicError.RateLimited, "限流", TimeSpan.FromSeconds(30));
        await page.DailyCommand.ExecuteAsync(null); await page.RefreshCommand.ExecuteAsync(null); Assert.Equal(1, api.Reads);
        time.Advance(TimeSpan.FromSeconds(30)); api.Daily = _ => Task.FromResult(new CatalogPage<MusicTrack>([DiscoveryFake.Track(7)])); await page.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, api.Reads); Assert.Equal(7, Assert.Single(page.Songs).Id); Assert.Empty(f.Audio.Opened);
        var readOpens = f.Audio.Opened.Count; await page.PlayAllCommand.ExecuteAsync(null);
        TestEvidence.Write("v8-discovery-navigation.json", new { schemaVersion = 1, realHost = false, reads = api.Reads, readMediaOpens = readOpens,
            loadedIds = page.Songs.Select(s => s.Id).ToArray(), queuedIds = f.Queue.Snapshot.Entries.Select(e => e.TrackId).ToArray(), complete = page.PlayAllText.Contains("全部") });
    }
    private static DiscoveryWorkspace Page(PlaybackFixture f, DiscoveryFake api, TimeProvider? time = null) => new(api, api, api, f.Catalog, f.Sessions, f.Queue, new ImmediateUi(), time);
    [Fact, Trait("V8", "D01,D06,P04")]
    public async Task 懒加载缓存过期与失败保留旧内容且没有媒体打开()
    {
        await using var f = new PlaybackFixture(); var api = new DiscoveryFake(); var time = new FakeTimeProvider(); using var page = Page(f, api, time);
        Assert.Equal(0, api.Reads); await page.DailyCommand.ExecuteAsync(null); Assert.Equal(2, page.Songs.Count); Assert.Contains("抓取于", page.Message);
        await page.DailyCommand.ExecuteAsync(null); Assert.Equal(1, api.Reads);
        time.Advance(TimeSpan.FromMinutes(6)); await page.DailyCommand.ExecuteAsync(null); Assert.Equal(2, api.Reads);
        api.Daily = _ => throw new MusicException(MusicError.Network, "断网"); await page.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, page.Songs.Count); Assert.Contains("旧内容", page.Message); Assert.Empty(f.Audio.Opened);
    }
    [Fact, Trait("V8", "D02,D03,D04")]
    public async Task 个性失败不会冒充通用且推荐和榜单打开真实歌单ID()
    {
        await using var f = new PlaybackFixture(); var api = new DiscoveryFake(); using var page = Page(f, api); long? opened = null;
        page.OpenPlaylist = id => { opened = id; return Task.CompletedTask; };
        api.Lists = kind => kind == RecommendationKind.Personal ? throw new MusicException(MusicError.Restricted, "账号受限") : Task.FromResult(new CatalogPage<MusicCard>([new(7, "通用")]));
        await page.PersonalCommand.ExecuteAsync(null); Assert.Empty(page.Cards); Assert.Contains("受限", page.Message); Assert.Equal(DiscoveryPage.Personal, page.Page);
        await page.GeneralCommand.ExecuteAsync(null); page.SelectedCard = Assert.Single(page.Cards); await page.OpenCardCommand.ExecuteAsync(null); Assert.Equal(7, opened);
        await page.ChartsCommand.ExecuteAsync(null); page.SelectedCard = Assert.Single(page.Cards); await page.OpenCardCommand.ExecuteAsync(null); Assert.Equal(9, opened); Assert.Empty(page.Songs); Assert.Empty(f.Audio.Opened);
    }
    [Fact, Trait("V8", "D05,W03")]
    public async Task 歌手重复分页无进展停止且切换排序重新读取首批()
    {
        await using var f = new PlaybackFixture(); var api = new DiscoveryFake(); var requests = new List<(int, ArtistSongOrder)>();
        api.Songs = (_, offset, order, _) => { requests.Add((offset, order)); return Task.FromResult(new CatalogPage<MusicTrack>([DiscoveryFake.Track(1)], offset + 50, true, false)); };
        using var page = Page(f, api); await page.NavigateAsync(DiscoveryPage.ArtistSongs, 81); Assert.True(page.HasMore);
        await page.MoreCommand.ExecuteAsync(null); Assert.Single(page.Songs); Assert.False(page.HasMore); Assert.Contains("已加载", page.PlayAllText);
        await page.SortCommand.ExecuteAsync(null); Assert.Equal((0, ArtistSongOrder.Time), requests.Last());
    }
    [Fact, Trait("V8", "W01,W02,W04,W05,W06,A04")]
    public async Task 多歌手选择到专辑并返回保持身份选择顺序和有界导航()
    {
        await using var f = new PlaybackFixture(); f.Catalog.Detail = (id, _) => Task.FromResult(DiscoveryFake.Track(id)); var api = new DiscoveryFake(); using var page = Page(f, api);
        await page.OpenWorksAsync(1); Assert.Equal(3, page.Cards.Count); page.SelectedCard = page.Cards[1]; await page.OpenCardCommand.ExecuteAsync(null); Assert.Equal(DiscoveryPage.ArtistSongs, page.Page);
        await page.ArtistAlbumsCommand.ExecuteAsync(null); page.SelectedCard = page.Cards[0]; await page.OpenCardCommand.ExecuteAsync(null);
        Assert.Equal(new long[] { 3, 1 }, page.Songs.Select(s => s.Id)); Assert.Contains("全部", page.PlayAllText);
        await page.BackCommand.ExecuteAsync(null); Assert.Equal(DiscoveryPage.ArtistAlbums, page.Page); Assert.Equal(91, page.SelectedCard!.Id);
        for (var i = 1; i <= 12; i++) await page.NavigateAsync(DiscoveryPage.Album, i);
        var backs = 0; while (page.BackCommand.CanExecute(null)) { await page.BackCommand.ExecuteAsync(null); backs++; } Assert.Equal(8, backs); Assert.Empty(f.Audio.Opened);
    }
    [Fact, Trait("V8", "P01,P02,P03,H04")]
    public async Task 发现播放和追加复用队列意图保留重复条目及来源()
    {
        await using var f = new PlaybackFixture(); var api = new DiscoveryFake(); using var page = Page(f, api);
        await page.DailyCommand.ExecuteAsync(null); page.Selected = page.Songs[0]; await page.AppendCommand.ExecuteAsync(null);
        Assert.Single(f.Queue.Snapshot.Entries); Assert.Empty(f.Audio.Opened); await page.AppendCommand.ExecuteAsync(null); Assert.Equal(2, f.Queue.Snapshot.Entries.Count);
        Assert.Equal(2, f.Queue.Snapshot.Entries.Select(e => e.EntryId).Distinct().Count()); await page.PlayCommand.ExecuteAsync(null); Assert.Single(f.Audio.Opened);
        await page.NavigateAsync(DiscoveryPage.Album, 91); await page.PlayAllCommand.ExecuteAsync(null); Assert.Equal(new long[] { 3, 1 }, f.Queue.Snapshot.Entries.Select(e => e.TrackId));
        Assert.All(f.Queue.Snapshot.Entries, e => Assert.Equal(91, e.SourceId));
    }
    [Theory, InlineData(DiscoveryPage.Recent), InlineData(DiscoveryPage.Week), InlineData(DiscoveryPage.AllTime), Trait("V8", "H01,H02,H03,H04")]
    public async Task 远端记录不写本地不去重合法记录并明确来源范围(DiscoveryPage target)
    {
        await using var f = new PlaybackFixture(); var api = new DiscoveryFake(); using var page = Page(f, api);
        await page.NavigateAsync(target); Assert.Equal(2, page.Songs.Count); Assert.Equal(page.Songs[0].Id, page.Songs[1].Id); Assert.Empty(f.Queue.Snapshot.Entries); Assert.Empty(f.Audio.Opened);
        Assert.Contains("范围", page.Message); Assert.Contains("可能延迟", page.Message);
        if (target == DiscoveryPage.Recent) Assert.Contains("未知", page.Songs[0].Detail); else Assert.DoesNotContain("次", page.Songs[0].Detail);
        TestEvidence.Write("v8-remote-history.json", new { schemaVersion = 1, realHost = false, records = page.Songs.Count, queueEntriesAfterRead = f.Queue.Snapshot.Entries.Count, mediaOpensAfterRead = f.Audio.Opened.Count, unknownFieldPreserved = !page.Songs[0].Detail.Contains("0 次") });
    }
    [Fact, Trait("V8", "C01,C02,C03,D06")]
    public async Task 旧账号与旧请求迟到结果不能覆盖新状态或释放新忙碌资格()
    {
        await using var f = new PlaybackFixture(); var api = new DiscoveryFake(); var old = new TaskCompletionSource<CatalogPage<MusicTrack>>(); api.Daily = _ => old.Task;
        using var page = Page(f, api); var loading = page.DailyCommand.ExecuteAsync(null); Assert.True(page.IsBusy);
        f.Sessions.Relogin(); api.Daily = _ => Task.FromResult(new CatalogPage<MusicTrack>([DiscoveryFake.Track(9)]));
        await page.RefreshCommand.ExecuteAsync(null); old.SetResult(new([DiscoveryFake.Track(1)])); await loading;
        Assert.Equal(9, Assert.Single(page.Songs).Id); Assert.False(page.IsBusy); Assert.Empty(f.Queue.Snapshot.Entries);
        TestEvidence.Write("v8-discovery-account.json", new { schemaVersion = 1, realHost = false, oldRows = page.Songs.Count(s => s.Id == 1),
            newRows = page.Songs.Count(s => s.Id == 9), busy = page.IsBusy, queueEntries = f.Queue.Snapshot.Entries.Count });
    }
    [Fact, Trait("V8", "C02,C04,R01")]
    public async Task 双页面浏览独立与二十轮释放后迟到UI不恢复内容()
    {
        await using var f = new PlaybackFixture(); var api = new DiscoveryFake(); using var a = Page(f, api); using var b = Page(f, api);
        await a.DailyCommand.ExecuteAsync(null); await b.ChartsCommand.ExecuteAsync(null); Assert.Equal(DiscoveryPage.Daily, a.Page); Assert.Equal(DiscoveryPage.Charts, b.Page);
        var queued = new QueuedUi(); var rounds = 0; var retainedRows = 0;
        for (var i = 0; i < 20; i++)
        {
            using var page = new DiscoveryWorkspace(api, api, api, f.Catalog, f.Sessions, f.Queue, queued);
            await page.DailyCommand.ExecuteAsync(null); page.Dispose(); queued.Flush(); Assert.Empty(page.Songs); Assert.Empty(page.Cards); rounds++; retainedRows += page.Songs.Count + page.Cards.Count;
        }
        TestEvidence.Write("v8-discovery-lifetime.json", new { schemaVersion = 1, realHost = false, rounds, queuedCallbacks = queued.Count, mediaOpens = f.Audio.Opened.Count, retainedRows });
    }
    [Fact, Trait("V8", "D01,A06")]
    public async Task 合法空推荐与协议失败具有不同页面反馈()
    {
        await using var f = new PlaybackFixture(); var api = new DiscoveryFake { Daily = _ => Task.FromResult(new CatalogPage<MusicTrack>([])) }; using var page = Page(f, api);
        await page.DailyCommand.ExecuteAsync(null); Assert.Contains("0 项", page.Message); Assert.False(page.PlayAllCommand.CanExecute(null));
        api.Daily = _ => throw new MusicException(MusicError.Protocol, "响应损坏"); await page.RefreshCommand.ExecuteAsync(null); Assert.Contains("损坏", page.Message);
    }
    private sealed class QueuedUi : MusicNetEasePlugin.Application.Authentication.ILoginUiDispatcher
    {
        private readonly Queue<Action> _actions = new(); public int Count => _actions.Count;
        public void Post(Action action) => _actions.Enqueue(action);
        public void Flush() { while (_actions.TryDequeue(out var action)) action(); }
    }
}
