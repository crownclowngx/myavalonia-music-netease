using System.Globalization;
using System.Text.Json;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>音乐端点只处理音乐含义；所有 HTTP/加密复用传输边界，错误不把 CDN 拒绝误报为账号失效。</summary>
internal sealed class NeteaseMusicApi(NeteaseTransport transport, XeapiTransport xeapi, IMusicSessionAccessor sessions)
    : IMusicCatalogApi, IPlaybackResourceResolver
{
    public Task<MusicSearchPage> SearchAsync(string keyword, int offset, MusicSession session, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length > 200 || offset < 0 || offset > 30000)
            throw new ArgumentException("搜索关键词或分页无效。");
        return ExecuteAsync(session, ct, async token =>
        {
            var result = await transport.SendAsync("/api/cloudsearch/pc", new()
            { ["s"] = keyword.Trim(), ["type"] = 1, ["limit"] = 30, ["offset"] = offset, ["total"] = true },
                NeteaseProtocol.Eapi, session.Context, token).ConfigureAwait(false);
            await CommitAsync(result, session, token).ConfigureAwait(false);
            var body = result.Body.GetProperty("result");
            if (body.TryGetProperty("songs", out var value) && value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
                throw new JsonException();
            var songs = body.TryGetProperty("songs", out var array) && array.ValueKind == JsonValueKind.Array
                ? array.EnumerateArray().Take(30).Select(ParseTrack).ToArray() : [];
            var count = Number(body, "songCount");
            return new MusicSearchPage(songs, offset, songs.Length == 30 && (count <= 0 || offset + songs.Length < count));
        });
    }
    public Task<MusicTrack> DetailAsync(long id, MusicSession session, CancellationToken ct) => ExecuteAsync(session, ct, async token =>
    {
        if (id <= 0) throw new ArgumentException("歌曲身份无效。");
        var reply = await transport.SendAsync("/api/v3/song/detail",
            new() { ["c"] = "[{\"id\":" + id.ToString(CultureInfo.InvariantCulture) + "}]" },
            NeteaseProtocol.Weapi, session.Context, token).ConfigureAwait(false);
        await CommitAsync(reply, session, token).ConfigureAwait(false);
        var songs = reply.Body.GetProperty("songs");
        if (songs.ValueKind == JsonValueKind.Array && songs.GetArrayLength() == 0)
            throw new MusicException(MusicError.Unavailable, "当前歌曲资料不可用。");
        var song = songs.EnumerateArray().Select(ParseTrack).FirstOrDefault(t => t.Id == id);
        return song ?? throw new MusicException(MusicError.Protocol, "返回的歌曲详情与所选歌曲不一致。");
    });
    public Task<PlaybackResource> ResolveAsync(long id, MusicSession session, CancellationToken ct) => ExecuteAsync(session, ct, async token =>
    {
        var reply = await xeapi.SendAsync("/api/song/enhance/player/url/v1",
            new() { ["ids"] = "[" + id.ToString(CultureInfo.InvariantCulture) + "]", ["level"] = "standard", ["encodeType"] = "flac" }, session, token).ConfigureAwait(false);
        await CommitAsync(reply, session, token).ConfigureAwait(false);
        var item = reply.Body.GetProperty("data").EnumerateArray().FirstOrDefault(p => Number(p, "id") == id);
        if (item.ValueKind != JsonValueKind.Object) throw new MusicException(MusicError.Protocol, "播放资源身份不一致。");
        var address = Text(item, "url");
        var trial = item.TryGetProperty("freeTrialInfo", out var info) && info.ValueKind == JsonValueKind.Object;
        long? trialMs = trial && Number(info, "end") > Number(info, "start") ? (Number(info, "end") - Number(info, "start")) * 1000 : null;
        if (!string.IsNullOrEmpty(address) && !Uri.TryCreate(address, UriKind.Absolute, out _))
            throw new MusicException(MusicError.Protocol, "播放地址格式无效。");
        return new PlaybackResource(id, string.IsNullOrEmpty(address) ? null : new Uri(address),
            Text(item, "type") ?? "", Text(item, "level") ?? "standard", trial, trialMs, trial ? Number(info, "start") * 1000 : null);
    });
    private async Task CommitAsync(NeteaseResponse reply, MusicSession session, CancellationToken ct)
    {
        var code = NeteaseTransport.Code(reply.Body);
        if (code != 200) throw NeteaseTransport.Error(200, code);
        ct.ThrowIfCancellationRequested();
        if (!sessions.IsCurrent(session)) throw new OperationCanceledException(ct);
        await sessions.CommitAsync(session, reply.Context, ct).ConfigureAwait(false);
    }
    private async Task<T> ExecuteAsync<T>(MusicSession session, CancellationToken ct, Func<CancellationToken, Task<T>> work)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Revoked);
        try
        {
            var value = await work(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (!sessions.IsCurrent(session)) throw new OperationCanceledException(linked.Token);
            return value;
        }
        catch (AuthException ex)
        {
            if (ex.Kind == AuthError.SessionExpired) await sessions.InvalidateAsync(session).ConfigureAwait(false);
            throw new MusicException(ex.Kind switch
            {
                AuthError.SessionExpired => MusicError.SignedOut,
                AuthError.Timeout => MusicError.Timeout,
                AuthError.Network => MusicError.Network,
                AuthError.RateLimited or AuthError.VerificationRequired => MusicError.Restricted,
                _ => MusicError.Protocol
            }, ex.Message, ex.RetryAfter);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new MusicException(MusicError.Protocol, "网易音乐数据格式不完整或已经变化。"); }
    }
    internal static MusicTrack ParseTrack(JsonElement item)
    {
        var id = Number(item, "id");
        var name = Text(item, "name");
        if (id <= 0 || string.IsNullOrWhiteSpace(name)) throw new JsonException();
        var artists = item.TryGetProperty("ar", out var ar) ? ar : item.TryGetProperty("artists", out ar) ? ar : default;
        var album = item.TryGetProperty("al", out var al) ? al : item.TryGetProperty("album", out al) ? al : default;
        return new(id, name, artists.ValueKind == JsonValueKind.Array
            ? string.Join(" / ", artists.EnumerateArray().Select(a => Text(a, "name")).Where(n => !string.IsNullOrEmpty(n))) : "未知歌手",
            Text(album, "name") ?? "未知专辑", Text(album, "picUrl"), Math.Max(Number(item, "dt"), Number(item, "duration")))
        {
            ArtistRefs = artists.ValueKind == JsonValueKind.Array ? artists.EnumerateArray().Where(a => Number(a, "id") > 0)
                .Select(a => new ArtistRef(Number(a, "id"), Text(a, "name") ?? "未知歌手")).DistinctBy(a => a.Id).ToArray() : [],
            AlbumRef = Number(album, "id") > 0 ? new(Number(album, "id"), Text(album, "name") ?? "未知专辑") : null
        };
    }
    private static string? Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long Number(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;
}
