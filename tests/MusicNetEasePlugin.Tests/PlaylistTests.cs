using Flurl.Http.Configuration;
using Flurl.Http.Testing;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Library;
using MusicNetEasePlugin.Infrastructure.Http;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class PlaylistTests
{
    [Fact, Trait("M2", "P01,P02,P04,P05,P07")]
    public async Task 真实歌单适配保留重复顺序并按身份补资料()
    {
        using var http = new HttpTest();
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        using var sessions = new MusicSessions();
        var api = new NeteasePlaylistApi(new(new(clients, TimeProvider.System), sessions));
        http.RespondWithJson(new { code = 200, more = true, playlist = new[] { new { id = 77L, name = "自建", trackCount = 4, creator = new { userId = 123L } } } });
        var page = await api.PlaylistsAsync(30, sessions.Capture(), default);
        Assert.Equal(31, page.NextOffset); Assert.True(page.HasMore); Assert.True(page.Items[0].IsOwned);
        Assert.Equal("/weapi/user/playlist", new Uri(http.CallLog[0].Request.Url).AbsolutePath);
        http.RespondWithJson(new { code = 200, playlist = new { id = 77L, name = "顺序", trackCount = 4, trackIds = new[] { new { id = 2L }, new { id = 1L }, new { id = 2L }, new { id = 3L } } } });
        var snapshot = await api.PlaylistAsync(77, sessions.Capture(), default);
        Assert.True(snapshot.IsComplete); Assert.Equal(new long[] { 2, 1, 2, 3 }, snapshot.TrackIds);
        http.RespondWithJson(new { code = 200, songs = new[] { new { id = 1L, name = "一" }, new { id = 2L, name = "二" }, new { id = 999L, name = "额外项" } } });
        var tracks = await api.TracksAsync(snapshot.TrackIds, sessions.Capture(), default);
        Assert.Equal(2, tracks.Count); Assert.False(tracks.ContainsKey(999)); Assert.False(tracks.ContainsKey(3));
        Assert.Equal("二", tracks[2].Name);
        Assert.All(http.CallLog.Skip(1), call => Assert.StartsWith("/eapi/", new Uri(call.Request.Url).AbsolutePath));
    }

    [Theory, InlineData(4, 3), InlineData(10001, 10001)]
    [Trait("M2", "P05,P06,P07")]
    public async Task 不完整或超限歌单不冒充播放全部(int declared, int actual)
    {
        using var http = new HttpTest(); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        http.RespondWithJson(new { code = 200, playlist = new { id = 7, name = "大歌单", trackCount = declared, trackIds = Enumerable.Range(1, actual).Select(id => new { id }) } });
        var api = new NeteasePlaylistApi(new(new(clients, TimeProvider.System), sessions));
        var result = await api.PlaylistAsync(7, sessions.Capture(), default);
        Assert.False(result.IsComplete); Assert.InRange(result.TrackIds.Count, 1, 10000); Assert.NotEmpty(result.Message);
    }

    [Theory, InlineData(301, MusicError.SignedOut, false), InlineData(429, MusicError.RateLimited, true), InlineData(404, MusicError.Protocol, true)]
    [Trait("M2", "P07,A02")]
    public async Task 内容错误和限流不会注销账号且会话失效撤销请求(int code, MusicError error, bool signedIn)
    {
        using var http = new HttpTest(); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        http.RespondWithJson(new { code, message = "MUSIC_U=secret" });
        var api = new NeteasePlaylistApi(new(new(clients, TimeProvider.System), sessions));
        var ex = await Assert.ThrowsAsync<MusicException>(() => api.PlaylistsAsync(0, sessions.Capture(), default));
        Assert.Equal(error, ex.Kind); Assert.Equal(signedIn, sessions.SignedIn); Assert.DoesNotContain("secret", ex.ToString());
    }

    [Fact, Trait("M2", "P02,P08")]
    public async Task 浏览分页使用原始偏移且重复页终止刷新可以重新开始()
    {
        using var sessions = new MusicSessions(); var api = new PlaylistFake();
        var offsets = new List<int>();
        api.Pages = (offset, _) =>
        {
            offsets.Add(offset);
            return Task.FromResult(new PlaylistPage([new(1, "一", null, 0, true), new(offset == 0 ? 2 : 3, "其他", null, 0, false)], offset + 2, true));
        };
        using var browser = new PlaylistBrowser(api, sessions, new ImmediateUi());
        await browser.LoadPlaylistsAsync(true); await browser.LoadPlaylistsAsync(false); await browser.LoadPlaylistsAsync(false);
        Assert.Equal(new[] { 0, 2, 4 }, offsets); Assert.Equal(3, browser.Playlists.Count); Assert.False(browser.HasMore);
        Assert.Contains("没有取得新", browser.Message);
        await browser.LoadPlaylistsAsync(true); Assert.Equal(2, browser.Playlists.Count); Assert.True(browser.HasMore);
    }

    [Fact, Trait("M2", "P03,P04,A02")]
    public async Task 旧歌单迟到和账号撤销不污染当前浏览()
    {
        using var sessions = new MusicSessions(); var api = new PlaylistFake();
        var first = new TaskCompletionSource<PlaylistTracks>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Detail = (id, _) => id == 1 ? first.Task : Task.FromResult(new PlaylistTracks(id, "当前", [2, 1, 2, 3], true, ""));
        api.Details = (ids, _) => Task.FromResult<IReadOnlyDictionary<long, MusicTrack>>(ids.Where(id => id != 3).Distinct().ToDictionary(id => id, MusicCatalog.Track));
        using var browser = new PlaylistBrowser(api, sessions, new ImmediateUi());
        var old = browser.OpenAsync(1); await browser.OpenAsync(2);
        first.SetResult(new(1, "迟到", [999], true, "")); await old;
        Assert.Equal(2, browser.Snapshot!.PlaylistId); Assert.Equal(new long[] { 2, 1, 2, 3 }, browser.Tracks.Select(t => t.TrackId));
        Assert.Null(browser.Tracks[3].Track); Assert.Equal(3, browser.Tracks[3].Index);
        sessions.Revoke(); Assert.Empty(browser.Tracks); Assert.Null(browser.Snapshot); Assert.False(browser.IsLoading);
        sessions.Relogin(); sessions.AccountId = 456;
        await browser.OpenAsync(2); Assert.Equal(4, browser.Tracks.Count);
    }

    [Fact, Trait("M2", "P05,P06,P08")]
    public async Task 大歌单只分页加载且缓存有界失败可重试()
    {
        using var sessions = new MusicSessions(); var api = new PlaylistFake();
        api.Detail = (id, _) => Task.FromResult(new PlaylistTracks(id, "大歌单", Enumerable.Range(1, 10000).Select(i => (long)i).ToArray(), true, ""));
        var batches = new List<int>(); var fail = false;
        api.Details = (ids, _) =>
        {
            batches.Add(ids.Count);
            if (fail) throw new MusicException(MusicError.Network, "连接中断");
            return Task.FromResult<IReadOnlyDictionary<long, MusicTrack>>(ids.ToDictionary(id => id, MusicCatalog.Track));
        };
        using var browser = new PlaylistBrowser(api, sessions, new ImmediateUi());
        await browser.OpenAsync(7); Assert.Single(batches); Assert.Equal(10000, browser.Snapshot!.TrackIds.Count); Assert.Equal(50, browser.Tracks.Count);
        for (var offset = 50; offset <= 600; offset += 50) await browser.LoadTracksAsync(offset);
        Assert.All(batches, count => Assert.InRange(count, 1, 50)); Assert.InRange(browser.CachedTracks, 1, 500);
        fail = true; await browser.LoadTracksAsync(650); Assert.Contains("连接中断", browser.Message); Assert.False(browser.IsLoading);
        fail = false; await browser.LoadTracksAsync(650); Assert.Equal(651, browser.Tracks[0].TrackId);
    }

    internal sealed class PlaylistFake : IPlaylistCatalogApi
    {
        public Func<int, CancellationToken, Task<PlaylistPage>> Pages { get; set; } = (offset, _) => Task.FromResult(new PlaylistPage([], offset, false));
        public Func<long, CancellationToken, Task<PlaylistTracks>> Detail { get; set; } = (id, _) => Task.FromResult(new PlaylistTracks(id, "歌单", [1], true, ""));
        public Func<IReadOnlyList<long>, CancellationToken, Task<IReadOnlyDictionary<long, MusicTrack>>> Details { get; set; } = (ids, _) => Task.FromResult<IReadOnlyDictionary<long, MusicTrack>>(ids.Distinct().ToDictionary(id => id, MusicCatalog.Track));
        public Task<PlaylistPage> PlaylistsAsync(int offset, MusicSession session, CancellationToken ct) => Pages(offset, ct);
        public Task<PlaylistTracks> PlaylistAsync(long id, MusicSession session, CancellationToken ct) => Detail(id, ct);
        public Task<IReadOnlyDictionary<long, MusicTrack>> TracksAsync(IReadOnlyList<long> ids, MusicSession session, CancellationToken ct) => Details(ids, ct);
    }
}
