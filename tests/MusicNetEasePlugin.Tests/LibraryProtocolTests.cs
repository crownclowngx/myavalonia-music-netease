using System.Text;
using System.Text.Json;
using Flurl.Http.Configuration;
using Flurl.Http.Testing;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Infrastructure.Protocol;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class LibraryProtocolTests
{
    [Theory, InlineData(301), InlineData(401), InlineData(429), InlineData(460)]
    public async Task 明确拒绝码保留分类且只有会话失效撤销账号(int code)
    {
        using var http = new HttpTest(); http.RespondWithJson(new { code }); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var result = await new NeteaseLikedSongsApi(new(new(clients, TimeProvider.System), sessions, new LibraryTokenFake())).SetAsync(1, true, sessions.Capture(), default);
        Assert.Equal(LibraryReceiptState.Rejected, result.State); Assert.True(result.StopRecheck); Assert.Equal(code is not (301 or 401), sessions.SignedIn); Assert.Single(http.CallLog);
    }
    [Theory, InlineData(false), InlineData(true)]
    public async Task Fake和真实适配共用喜欢确认无变化及取消契约(bool real)
    {
        using var http = new HttpTest(); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var fake = new LibraryFake();
        ILikedSongsApi likes = real ? new NeteaseLikedSongsApi(new(new(clients, TimeProvider.System), sessions, new LibraryTokenFake())) : fake;
        http.RespondWithJson(new { code = 200, ids = Array.Empty<long>() }); http.RespondWithJson(new { code = 200 });
        http.RespondWithJson(new { code = 200, ids = new long[] { 7 } }); http.RespondWithJson(new { code = 200, ids = new long[] { 7 } });
        await using var library = new MusicLibraryCoordinator(sessions, likes, fake, fake);
        Assert.Null(library.Snapshot.Likes); Assert.Equal(LibraryOutcome.Confirmed, (await library.SetLikedAsync(7, true)).Outcome); Assert.True(library.Snapshot.IsLiked(7));
        Assert.True((await library.SetLikedAsync(7, true)).NoChange);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); Assert.Equal(LibraryOutcome.NotSent, (await library.SetLikedAsync(7, false, cancelled.Token)).Outcome);
        Assert.Equal(1, real ? http.CallLog.Count(c => new Uri(c.Request.Url).AbsolutePath == "/weapi/radio/like") : fake.Writes.Count);
    }

    [Fact]
    public async Task HTTP限流保留RetryAfter且不会换端点重试()
    {
        using var http = new HttpTest(); http.RespondWith("{}", 429, new { Retry_After = "30" });
        using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var result = await new NeteaseLikedSongsApi(new(new(clients, TimeProvider.System), sessions, new LibraryTokenFake())).SetAsync(7, true, sessions.Capture(), default);
        Assert.Equal(LibraryReceiptState.Uncertain, result.State); Assert.True(result.StopRecheck); Assert.Equal(TimeSpan.FromSeconds(30), result.RetryAfter); Assert.Single(http.CallLog);
    }
    internal static LibraryIntent Intent(LibraryOperationKind kind) => new(kind, kind == LibraryOperationKind.Create ? 0 : 7, true, "中文😃", [3, 4]);
    public static IEnumerable<object[]> Replies => Enum.GetValues<LibraryOperationKind>().SelectMany(kind => new[]
    {
        new object[] { kind, "{\"code\":200,\"playlist\":{\"id\":99,\"name\":\"规范名称\"}}", LibraryReceiptState.Accepted },
        new object[] { kind, "{\"code\":403,\"message\":\"MUSIC_U=secret\"}", LibraryReceiptState.Rejected },
        new object[] { kind, "broken MUSIC_U=secret", LibraryReceiptState.Uncertain },
        new object[] { kind, "{\"code\":512}", LibraryReceiptState.Uncertain }
    });

    [Theory, MemberData(nameof(Replies))]
    public async Task 每个写端点成功拒绝损坏和未知均只发一次且不泄露正文(LibraryOperationKind kind, string body, LibraryReceiptState expected)
    {
        using var http = new HttpTest(); http.RespondWith(body);
        using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var token = new LibraryTokenFake(); var requests = new LibraryRequestExecutor(new(clients, TimeProvider.System), sessions, token);
        var result = kind == LibraryOperationKind.Like
            ? await new NeteaseLikedSongsApi(requests).SetAsync(7, true, sessions.Capture(), default)
            : await new NeteasePlaylistMutationApi(requests).WriteAsync(Intent(kind), sessions.Capture(), default);
        Assert.Equal(expected, result.State); Assert.Single(http.CallLog); Assert.DoesNotContain("secret", result.ToString());
        Assert.Equal(kind == LibraryOperationKind.Subscribe ? 1 : 0, token.Calls);
        if (kind == LibraryOperationKind.Create && expected == LibraryReceiptState.Accepted) { Assert.Equal(99, result.CreatedId); Assert.Equal("规范名称", result.ServerName); }
    }

    [Fact]
    public async Task 原生端点参数编码和临时收藏令牌一致且无包装服务回退()
    {
        Assert.Equal(NeteaseProtocol.Weapi, LibraryRequests.Like(1, false).Protocol);
        Assert.Equal(false, LibraryRequests.Like(1, false).Data["like"]);
        Assert.Equal("itembased", LibraryRequests.Like(1, false).Data["alg"]);
        Assert.Equal(123L, LibraryRequests.Likes(123).Data["uid"]);
        var expected = new[] { "/api/playlist/subscribe", "/api/playlist/create", "/api/playlist/update/name", "/api/playlist/desc/update", "/api/playlist/manipulate/tracks", "/api/playlist/manipulate/tracks", "/api/playlist/remove" };
        Assert.Equal(expected, Enum.GetValues<LibraryOperationKind>().Skip(1).Select(k => LibraryRequests.Write(Intent(k)).Path));
        Assert.Equal("/api/playlist/unsubscribe", LibraryRequests.Write(new(LibraryOperationKind.Subscribe, 7, false)).Path);
        var create = LibraryRequests.Write(Intent(LibraryOperationKind.Create)); Assert.Equal("NORMAL", create.Data["type"]); Assert.Equal("0", create.Data["privacy"]);
        var tracks = LibraryRequests.Write(Intent(LibraryOperationKind.RemoveTracks)); Assert.Equal("del", tracks.Data["op"]); Assert.Equal("[\"3\",\"4\"]", tracks.Data["trackIds"]);
        Assert.Equal("[7]", LibraryRequests.Write(Intent(LibraryOperationKind.Delete)).Data["ids"]);
        using var http = new HttpTest(); http.RespondWithJson(new { code = 200 });
        using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var tokens = new LibraryTokenFake(); var api = new NeteasePlaylistMutationApi(new(new(clients, TimeProvider.System), sessions, tokens));
        await api.WriteAsync(Intent(LibraryOperationKind.Subscribe), sessions.Capture(), default);
        var call = Assert.Single(http.CallLog); Assert.Equal("/eapi/playlist/subscribe", new Uri(call.Request.Url).AbsolutePath);
        using var data = JsonDocument.Parse(Encoding.UTF8.GetString(NeteaseCrypto.DecodeEapi(Convert.FromHexString(ScriptedHandler.Form(call.RequestBody)["params"]))).Split("-36cd479b6b5-")[1]);
        Assert.Equal("fresh-test-token", data.RootElement.GetProperty("checkToken").GetString());
        Assert.Equal("fresh-test-token", data.RootElement.GetProperty("header").GetProperty("X-antiCheatToken").GetString());
        Assert.DoesNotContain("fresh-test-token", new LibraryCheckToken("fresh-test-token").ToString());
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task 令牌失败或取得前取消不发收藏请求(bool cancel)
    {
        using var http = new HttpTest(); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var tokens = new LibraryTokenFake { Get = _ => cancel ? Task.FromCanceled<LibraryCheckToken>(new(true)) : throw new LibraryReadException(LibraryReadError.Restricted, "无法取得令牌") };
        var result = await new NeteasePlaylistMutationApi(new(new(clients, TimeProvider.System), sessions, tokens)).WriteAsync(Intent(LibraryOperationKind.Subscribe), sessions.Capture(), default);
        Assert.Equal(LibraryReceiptState.NotSent, result.State); Assert.Empty(http.CallLog);
    }

    [Theory, InlineData("{\"code\":200,\"ids\":[]}", 0), InlineData("{\"code\":200,\"ids\":[1,1,2]}", 2), InlineData("{\"code\":200}", -1), InlineData("{\"code\":200,\"ids\":[0]}", -1), InlineData("{\"code\":403}", -1), InlineData("broken", -1)]
    public async Task 喜欢读取严格区分有效空重复和不完整响应(string body, int count)
    {
        using var http = new HttpTest(); http.RespondWith(body); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var api = new NeteaseLikedSongsApi(new(new(clients, TimeProvider.System), sessions, new LibraryTokenFake()));
        if (count >= 0) Assert.Equal(count, (await api.ReadAsync(sessions.Capture(), default)).Count);
        else await Assert.ThrowsAsync<LibraryReadException>(() => api.ReadAsync(sessions.Capture(), default));
        Assert.Single(http.CallLog);
    }

    [Fact]
    public async Task 喜欢数量和字节超预算不可当成全量集合()
    {
        using var http = new HttpTest(); http.RespondWithJson(new { code = 200, ids = Enumerable.Range(1, 50001) }); http.RespondWith(new string(' ', NeteaseTransport.MaximumResponseBytes + 1));
        using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var api = new NeteaseLikedSongsApi(new(new(clients, TimeProvider.System), sessions, new LibraryTokenFake()));
        for (var i = 0; i < 2; i++) Assert.Equal(LibraryReadError.Protocol, (await Assert.ThrowsAsync<LibraryReadException>(() => api.ReadAsync(sessions.Capture(), default))).Kind);
    }

    [Theory, InlineData("{}"), InlineData("{\"subscribed\":true}"), InlineData("{\"subscribed\":false}")]
    public async Task 收藏使用动态真实字段且缺失保持未知(string dynamicBody)
    {
        using var http = new HttpTest();
        http.RespondWithJson(new { code = 200, playlist = new { id = 7, name = "他人的", creator = new { userId = 456 }, trackIds = Array.Empty<object>(), trackCount = 0 } });
        var dynamicData = JsonSerializer.Deserialize<Dictionary<string, object>>(dynamicBody)!; dynamicData["code"] = 200; http.RespondWithJson(dynamicData);
        using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var p = await new NeteaseLibraryPlaylistQuery(new(new(clients, TimeProvider.System), sessions, new LibraryTokenFake())).ReadAsync(7, sessions.Capture(), default);
        Assert.Equal(dynamicBody == "{}" ? null : (bool?)dynamicBody.Contains("true"), p.Subscribed); Assert.False(p.CanEdit(123)); Assert.Equal(LibraryPlaylistKind.Unknown, p.Kind); Assert.False(p.DescriptionKnown);
        Assert.Equal(2, http.CallLog.Count); Assert.EndsWith("/eapi/playlist/detail/dynamic", http.CallLog[1].Request.Url.ToString());
    }

    [Theory, InlineData(0, false, 2, 2, LibraryPlaylistKind.Normal), InlineData(5, false, 2, 2, LibraryPlaylistKind.Liked), InlineData(0, true, 2, 2, LibraryPlaylistKind.Shared), InlineData(0, false, 10001, 10001, LibraryPlaylistKind.Normal), InlineData(0, false, 2, 3, LibraryPlaylistKind.Normal)]
    public void 详情权限与全量快照必须来自明确字段(int special, bool shared, int actual, int declared, LibraryPlaylistKind kind)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { id = 7, name = "自己的", creator = new { userId = 123 }, specialType = special, shared, trackIds = Enumerable.Range(1, actual).Select(id => new { id }), trackCount = declared, description = (string?)null }));
        var p = NeteaseLibraryPlaylistQuery.Parse(json.RootElement, 7);
        Assert.Equal(kind, p.Kind); Assert.Equal(kind == LibraryPlaylistKind.Normal, p.CanEdit(123)); Assert.Equal(actual <= 10000 && actual == declared, p.IsComplete); Assert.True(p.DescriptionKnown); Assert.Equal("", p.Description);
    }

    [Fact]
    public async Task 成功回执后的凭据保存失败仍保留受理事实()
    {
        using var http = new HttpTest(); http.RespondWithJson(new { code = 200 }); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var session = new MusicSessions();
        var access = new StorageFailure(session); var api = new NeteaseLikedSongsApi(new(new(clients, TimeProvider.System), access, new LibraryTokenFake()));
        var result = await api.SetAsync(1, true, access.Capture(), default);
        Assert.Equal(LibraryReceiptState.Accepted, result.State); Assert.True(result.CredentialSaveFailed); Assert.Single(http.CallLog);
    }
    private sealed class StorageFailure(MusicSessions inner) : IMusicSessionAccessor
    {
        public MusicSession Capture() => inner.Capture(); public bool IsCurrent(MusicSession s) => inner.IsCurrent(s);
        public Task CommitAsync(MusicSession s, AuthContext c, CancellationToken ct) => throw new AuthException(AuthError.Storage, "保存失败");
        public Task InvalidateAsync(MusicSession s) => inner.InvalidateAsync(s);
    }
}
