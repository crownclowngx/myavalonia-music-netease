using System.Text.Json;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>只读歌单适配；保留服务端 ID 顺序与完整性，批量资料只映射请求 ID，缺失交给浏览层显示占位。</summary>
internal sealed class NeteasePlaylistApi(MusicRequestExecutor requests) : IPlaylistCatalogApi
{
    public Task<PlaylistPage> PlaylistsAsync(int offset, MusicSession session, CancellationToken ct)
    {
        if (session.AccountId <= 0 || offset is < 0 or >= 1000) throw new ArgumentOutOfRangeException(nameof(offset));
        return requests.ExecuteAsync(DailyPlayerRequests.Playlists(session.AccountId, offset), session, body =>
        {
            var source = body.GetProperty("playlist");
            if (source.ValueKind != JsonValueKind.Array || source.GetArrayLength() > 30) throw new JsonException();
            var items = source.EnumerateArray().Select(p => new MusicPlaylist(PositiveId(p), RequiredText(p, "name"),
                Text(p, "coverImgUrl"), checked((int)Math.Max(0, Number(p, "trackCount"))),
                p.TryGetProperty("creator", out var creator) && Number(creator, "userId") == session.AccountId)).ToArray();
            var next = offset + items.Length;
            return new PlaylistPage(items, next, items.Length > 0 && next < 1000 && body.TryGetProperty("more", out var more) && more.ValueKind == JsonValueKind.True);
        }, ct);
    }
    public Task<PlaylistTracks> PlaylistAsync(long id, MusicSession session, CancellationToken ct)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        return requests.ExecuteAsync(DailyPlayerRequests.Playlist(id), session, body =>
        {
            var p = body.GetProperty("playlist");
            if (PositiveId(p) != id) throw new JsonException();
            var ids = p.GetProperty("trackIds");
            if (ids.ValueKind != JsonValueKind.Array) throw new JsonException();
            var sourceCount = ids.GetArrayLength();
            var values = ids.EnumerateArray().Take(10000).Select(PositiveId).ToArray();
            var complete = sourceCount <= 10000 && sourceCount == Number(p, "trackCount");
            return new PlaylistTracks(id, RequiredText(p, "name"), Array.AsReadOnly(values), complete,
                complete ? "" : "歌单曲目列表不完整或超过 10,000 首限制；可浏览已知曲目，暂不能播放全部。");
        }, ct);
    }
    public Task<IReadOnlyDictionary<long, MusicTrack>> TracksAsync(IReadOnlyList<long> ids, MusicSession session, CancellationToken ct)
    {
        if (ids.Count is < 1 or > 50 || ids.Any(id => id <= 0)) throw new ArgumentException("每批需要 1–50 个有效歌曲 ID。", nameof(ids));
        var requested = ids.ToHashSet();
        return requests.ExecuteAsync<IReadOnlyDictionary<long, MusicTrack>>(DailyPlayerRequests.Tracks(ids), session, body =>
        {
            var songs = body.GetProperty("songs");
            if (songs.ValueKind != JsonValueKind.Array || songs.GetArrayLength() > 100) throw new JsonException();
            var result = new Dictionary<long, MusicTrack>();
            foreach (var item in songs.EnumerateArray())
            {
                if (!requested.Contains(Number(item, "id"))) continue;
                try { var track = NeteaseMusicApi.ParseTrack(item); result.TryAdd(track.Id, track); }
                catch (JsonException) { /* 单项资料损坏保留其原始 ID 槽位，其余资料仍然可用。 */ }
            }
            return result;
        }, ct);
    }
    private static long PositiveId(JsonElement p) => Number(p, "id") is > 0 and var id ? id : throw new JsonException();
    private static string RequiredText(JsonElement p, string key) => Text(p, key) is { Length: > 0 } text ? text : throw new JsonException();
    private static string? Text(JsonElement p, string key) => p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long Number(JsonElement p, string key) => p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var value) && value.TryGetInt64(out var number) ? number : 0;
}
