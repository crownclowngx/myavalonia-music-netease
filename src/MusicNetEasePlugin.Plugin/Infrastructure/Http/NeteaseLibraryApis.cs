using System.Collections.Immutable;
using System.Text.Json;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Http;

internal sealed class NeteaseLikedSongsApi(LibraryRequestExecutor requests) : ILikedSongsApi
{
    public Task<ImmutableHashSet<long>> ReadAsync(MusicSession session, CancellationToken ct) =>
        requests.ReadAsync(LibraryRequests.Likes(session.AccountId), session, body =>
        {
            var values = body.GetProperty("ids");
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > LibraryEditRules.MaximumLikedSongs) throw new JsonException();
            return values.EnumerateArray().Select(v => v.TryGetInt64(out var id) && id > 0 ? id : throw new JsonException()).ToImmutableHashSet();
        }, ct);
    public Task<LibraryWriteReceipt> SetAsync(long trackId, bool desired, MusicSession session, CancellationToken ct)
    {
        if (trackId <= 0) throw new ArgumentException("歌曲 ID 无效。");
        return requests.WriteAsync(LibraryRequests.Like(trackId, desired), session, _ => new(LibraryReceiptState.Accepted), false, ct);
    }
}

/// <summary>管理详情保存完整性、creator、specialType 与可缺失的 subscribed；缺字段只会缩小权限，不补猜测值。</summary>
internal sealed class NeteaseLibraryPlaylistQuery(LibraryRequestExecutor requests) : ILibraryPlaylistQuery
{
    public async Task<LibraryPlaylist> ReadAsync(long id, MusicSession session, CancellationToken ct)
    {
        if (id <= 0) throw new ArgumentException("歌单 ID 无效。");
        var playlist = await requests.ReadAsync(DailyPlayerRequests.Playlist(id), session, body => Parse(body.GetProperty("playlist"), id), ct).ConfigureAwait(false);
        if (playlist.Subscribed.HasValue || playlist.CreatorId == session.AccountId) return playlist;
        var subscribed = await requests.ReadAsync(LibraryRequests.Dynamic(id), session, body => Boolean(body, "subscribed"), ct).ConfigureAwait(false);
        return playlist with { Subscribed = subscribed };
    }
    internal static LibraryPlaylist Parse(JsonElement p, long expectedId)
    {
        if (Number(p, "id") != expectedId || Text(p, "name") is not { Length: > 0 } name) throw new JsonException();
        var ids = p.GetProperty("trackIds");
        if (ids.ValueKind != JsonValueKind.Array) throw new JsonException();
        var tracks = ids.EnumerateArray().Take(10000).Select(v => Number(v, "id") is > 0 and var id ? id : throw new JsonException()).ToArray();
        var declared = Number(p, "trackCount");
        if (declared is < 0 or > int.MaxValue) throw new JsonException();
        var creator = p.TryGetProperty("creator", out var c) ? Number(c, "userId") ?? 0 : 0;
        var special = Number(p, "specialType"); var type = Text(p, "type");
        var shared = type == "SHARED" || Boolean(p, "shared") == true ||
            p.TryGetProperty("sharedUsers", out var users) && users.ValueKind == JsonValueKind.Array && users.GetArrayLength() > 0;
        var kind = shared ? LibraryPlaylistKind.Shared : special switch
        {
            0 when type is null or "NORMAL" => LibraryPlaylistKind.Normal,
            5 => LibraryPlaylistKind.Liked,
            null => LibraryPlaylistKind.Unknown,
            _ => LibraryPlaylistKind.Other
        };
        var knownDescription = p.TryGetProperty("description", out var description) && description.ValueKind is JsonValueKind.Null or JsonValueKind.String;
        return new(expectedId, name, knownDescription ? Text(p, "description") ?? "" : null, knownDescription, creator, kind,
            Boolean(p, "subscribed"), Array.AsReadOnly(tracks), declared.HasValue && declared == ids.GetArrayLength() && ids.GetArrayLength() <= 10000,
            declared.HasValue ? (int)declared.Value : null, Number(p, "updateTime"));
    }
    internal static long? Number(JsonElement p, string key) => p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
    internal static string? Text(JsonElement p, string key) => p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    internal static bool? Boolean(JsonElement p, string key) => p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;
}

internal sealed class NeteasePlaylistMutationApi(LibraryRequestExecutor requests) : IPlaylistMutationApi
{
    public Task<LibraryWriteReceipt> WriteAsync(LibraryIntent intent, MusicSession session, CancellationToken ct) =>
        requests.WriteAsync(LibraryRequests.Write(intent), session, body =>
        {
            if (intent.Kind == LibraryOperationKind.Create)
            {
                if (!body.TryGetProperty("playlist", out var playlist) || NeteaseLibraryPlaylistQuery.Number(playlist, "id") is not > 0)
                    return new(LibraryReceiptState.Accepted, "创建请求已有成功回执，但歌单 ID 未能确认。");
                return new(LibraryReceiptState.Accepted, CreatedId: NeteaseLibraryPlaylistQuery.Number(playlist, "id"), ServerName: NeteaseLibraryPlaylistQuery.Text(playlist, "name"));
            }
            return new(LibraryReceiptState.Accepted);
        }, intent.Kind == LibraryOperationKind.Subscribe, ct);
}
