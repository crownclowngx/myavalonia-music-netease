using System.Text.Json;
using Flurl.Http.Configuration;
using Flurl.Http.Testing;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Http;
using Xunit;
namespace MusicNetEasePlugin.Tests;

public sealed class DiscoveryProtocolTests
{
    [Theory, InlineData(0), InlineData(1), InlineData(2), Trait("V8", "A02,F08")]
    public async Task FM反馈保存失败保留成功回执损坏和超时保留未知(int failure)
    {
        using var http = new HttpTest(); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var accessor = new CommitFailure(sessions); var transport = new NeteaseTransport(clients, TimeProvider.System);
        var api = new NeteasePrivateFmApi(new(transport, accessor), transport, accessor);
        if (failure == 0) http.RespondWithJson(new { code = 200 }); else if (failure == 1) http.RespondWith("{broken"); else http.SimulateTimeout();
        var result = await api.DislikeAsync(7, sessions.Capture(), default);
        Assert.Equal(failure == 0 ? FmFeedbackState.Accepted : FmFeedbackState.Uncertain, result.State);
        Assert.Equal(failure == 0, result.CredentialSaveFailed); Assert.Single(http.CallLog);
    }
    private sealed class CommitFailure(MusicSessions inner) : IMusicSessionAccessor
    {
        public MusicSession Capture() => inner.Capture();
        public bool IsCurrent(MusicSession session) => inner.IsCurrent(session);
        public Task CommitAsync(MusicSession session, AuthContext context, CancellationToken ct) => throw new AuthException(AuthError.Storage, "保存失败");
        public Task InvalidateAsync(MusicSession session) => inner.InvalidateAsync(session);
    }
    [Theory, InlineData(true), InlineData(false), Trait("V8", "A03,W02,W05")]
    public async Task 作品响应身份必须匹配且不完整专辑不冒充全集(bool artist)
    {
        using var http = new HttpTest(); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var api = new NeteaseArtistAlbumApi(new(new(clients, TimeProvider.System), sessions));
        http.RespondWithJson(artist ? (object)new { code = 200, data = new { artist = new { id = 99 } } } : new { code = 200, album = new { id = 99 }, songs = Array.Empty<object>() });
        await Assert.ThrowsAsync<MusicException>(() => artist ? (Task)api.ArtistAsync(81, sessions.Capture(), default) : api.AlbumAsync(91, sessions.Capture(), default));
        http.RespondWithJson(new { code = 200, album = new { id = 91, size = 2 }, songs = new[] { new { id = 1, name = "一首" } } });
        var album = await api.AlbumAsync(91, sessions.Capture(), default); Assert.Single(album.Songs.Items); Assert.False(album.Songs.Complete);
    }
    [Fact, Trait("V8", "A01,A03")]
    public void 固定端点参数和非法身份在发送前验证()
    {
        Assert.Equal("/api/v3/discovery/recommend/songs", DiscoveryRequests.Daily().Path);
        Assert.Equal("/api/v1/discovery/recommend/resource", DiscoveryRequests.Playlists(RecommendationKind.Personal).Path);
        Assert.Equal(30, DiscoveryRequests.Playlists(RecommendationKind.General).Data["limit"]);
        Assert.Equal("/api/toplist", DiscoveryRequests.Charts().Path);
        Assert.Equal(NeteaseProtocol.Eapi, DiscoveryRequests.Artist(81).Protocol);
        Assert.Equal("/api/artist/albums/81", DiscoveryRequests.Albums(81, 50).Path);
        Assert.Equal("/api/v1/album/91", DiscoveryRequests.Album(91).Path);
        var songs = DiscoveryRequests.Songs(81, 50, ArtistSongOrder.Time);
        Assert.Equal("/api/v1/artist/songs", songs.Path); Assert.Equal("time", songs.Data["order"]); Assert.Equal("true", songs.Data["private_cloud"]); Assert.Equal(50, songs.Data["limit"]);
        Assert.Equal("/api/v1/radio/get", DiscoveryRequests.Fm().Path);
        var trash = DiscoveryRequests.Dislike(7); Assert.Equal("/api/radio/trash/add", trash.Path); Assert.Equal(7L, trash.Data["songId"]); Assert.Equal("RT", trash.Data["alg"]);
        Assert.Equal("/api/play-record/song/list", DiscoveryRequests.History(RemoteHistoryKind.Recent, 123).Path);
        Assert.Equal(1, DiscoveryRequests.History(RemoteHistoryKind.Week, 123).Data["type"]); Assert.Equal(0, DiscoveryRequests.History(RemoteHistoryKind.AllTime, 123).Data["type"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => DiscoveryRequests.Artist(0)); Assert.Throws<ArgumentOutOfRangeException>(() => DiscoveryRequests.Album(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiscoveryRequests.Songs(1, 1000, ArtistSongOrder.Hot)); Assert.Throws<ArgumentOutOfRangeException>(() => DiscoveryRequests.Songs(1, -1, ArtistSongOrder.Hot));
    }
    public static IEnumerable<object[]> Endpoints() => Enumerable.Range(0, 12).SelectMany(endpoint => new[] { 0, 1, 2 }.Select(response => new object[] { endpoint, response }));
    [Theory, MemberData(nameof(Endpoints)), Trait("V8", "A01,A02,A03,A06,D01,D02,D04,W02,W05,H01,H02,H03")]
    public async Task 全部只读端点成功拒绝损坏分别处理且只请求一次(int endpoint, int response)
    {
        using var http = new HttpTest(); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var read = new MusicRequestExecutor(new(clients, TimeProvider.System), sessions);
        var discovery = new NeteaseDiscoveryApi(read); var works = new NeteaseArtistAlbumApi(read); var history = new NeteaseRemoteHistoryApi(read); var fm = new NeteasePrivateFmApi(read, new(clients, TimeProvider.System), sessions);
        var track = new { id = 1, name = "曲目", ar = new[] { new { id = 81, name = "歌手" } }, al = new { id = 91, name = "专辑" } };
        var card = new { id = 91, name = "专辑", size = 1 };
        object body = endpoint switch
        {
            0 => new { code = 200, data = new { dailySongs = new[] { track } } }, 1 => new { code = 200, recommend = new[] { card } },
            2 => new { code = 200, result = new[] { card } }, 3 => new { code = 200, list = new[] { card } },
            4 => new { code = 200, data = new { artist = new { id = 81, name = "歌手" } } }, 5 => new { code = 200, songs = new[] { track }, more = false },
            6 => new { code = 200, hotAlbums = new[] { card }, more = false }, 7 => new { code = 200, album = card, songs = new[] { track } },
            8 => new { code = 200, data = new[] { track } }, 9 => new { code = 200, data = new { list = new[] { new { data = track, playTime = 1700000000000L } } } },
            10 => new { code = 200, weekData = new[] { new { song = track, score = 9 } } }, _ => new { code = 200, allData = new[] { new { song = track, playCount = 3 } } }
        };
        http.RespondWithJson(response == 0 ? body : response == 1 ? new { code = 403 } : new { code = 200 });
        Task Query() => endpoint switch
        {
            0 => discovery.DailyAsync(sessions.Capture(), default), 1 => discovery.PlaylistsAsync(RecommendationKind.Personal, sessions.Capture(), default),
            2 => discovery.PlaylistsAsync(RecommendationKind.General, sessions.Capture(), default), 3 => discovery.ChartsAsync(sessions.Capture(), default),
            4 => works.ArtistAsync(81, sessions.Capture(), default), 5 => works.SongsAsync(81, 0, ArtistSongOrder.Hot, sessions.Capture(), default),
            6 => works.AlbumsAsync(81, 0, sessions.Capture(), default), 7 => works.AlbumAsync(91, sessions.Capture(), default),
            8 => fm.ReadAsync(sessions.Capture(), default), 9 => history.ReadAsync(RemoteHistoryKind.Recent, sessions.Capture(), default),
            10 => history.ReadAsync(RemoteHistoryKind.Week, sessions.Capture(), default), _ => history.ReadAsync(RemoteHistoryKind.AllTime, sessions.Capture(), default)
        };
        if (response == 0) { await Query(); Assert.Equal(1, sessions.Commits); } else { await Assert.ThrowsAsync<MusicException>(Query); Assert.Equal(0, sessions.Commits); }
        Assert.Single(http.CallLog); Assert.True(sessions.SignedIn);
    }
    [Theory, InlineData(200, FmFeedbackState.Accepted), InlineData(403, FmFeedbackState.Rejected), InlineData(429, FmFeedbackState.Rejected), InlineData(777, FmFeedbackState.Uncertain), Trait("V8", "A02,F08")]
    public async Task FM反馈回执不确定与拒绝不重试(int code, FmFeedbackState expected)
    {
        using var http = new HttpTest(); http.RespondWithJson(new { code }); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions(); var transport = new NeteaseTransport(clients, TimeProvider.System);
        var api = new NeteasePrivateFmApi(new(transport, sessions), transport, sessions);
        Assert.Equal(expected, (await api.DislikeAsync(7, sessions.Capture(), default)).State); Assert.Single(http.CallLog);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); Assert.Equal(FmFeedbackState.NotSent, (await api.DislikeAsync(7, sessions.Capture(), cancel.Token)).State); Assert.Single(http.CallLog);
    }
    [Fact, Trait("V8", "A04,A05,W01")]
    public void 多歌手身份保持顺序旧资料仍可读取且未知身份不臆造()
    {
        using var json = JsonDocument.Parse("{\"id\":1,\"name\":\"歌\",\"ar\":[{\"id\":81,\"name\":\"同名\"},{\"id\":82,\"name\":\"同名\"}],\"al\":{\"id\":91,\"name\":\"专辑\"}}");
        var track = NeteaseMusicApi.ParseTrack(json.RootElement); Assert.Equal(new long[] { 81, 82 }, track.ArtistRefs.Select(a => a.Id)); Assert.Equal(91, track.AlbumRef!.Id);
        var old = JsonSerializer.Deserialize<MusicTrack>("{\"Id\":1,\"Name\":\"歌\",\"Artists\":\"旧歌手\",\"Album\":\"旧专辑\",\"DurationMs\":0}")!;
        Assert.Empty(old.ArtistRefs); Assert.Null(old.AlbumRef); Assert.Equal("旧歌手", old.Artists);
    }
    [Fact, Trait("V8", "D05,W03,W05,H03")]
    public void 分页按原始数量推进超预算和未知专辑完整性保守处理()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Enumerable.Range(1, 60).Select(id => new { id, name = "歌" })));
        var page = DiscoveryJson.Page(json.RootElement, DiscoveryJson.Song, 950, true, 50);
        Assert.Equal(50, page.Items.Count); Assert.Equal(1010, page.NextOffset); Assert.False(page.HasMore); Assert.False(page.Complete);
        using var empty = JsonDocument.Parse("[]"); Assert.False(DiscoveryJson.Page(empty.RootElement, DiscoveryJson.Song, 10, true).HasMore);
    }
}
