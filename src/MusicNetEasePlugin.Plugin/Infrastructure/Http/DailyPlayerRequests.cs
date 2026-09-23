using System.Text.Json;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>
/// M2 的固定只读端点描述。协议与参数由固定上游及只读探针核对，业务适配复用这里，
/// 不开放任意 URL，也不在 ViewModel 拼装协议数据。详情 n=0 只取完整 ID 顺序，曲目另分批补取。
/// </summary>
internal sealed record DailyPlayerRequest(string Path, NeteaseProtocol Protocol, Dictionary<string, object?> Data);

internal static class DailyPlayerRequests
{
    internal static DailyPlayerRequest Playlists(long accountId, int offset) => new("/api/user/playlist", NeteaseProtocol.Weapi,
        new() { ["uid"] = accountId, ["limit"] = 30, ["offset"] = offset, ["includeVideo"] = true });
    internal static DailyPlayerRequest Playlist(long id) => new("/api/v6/playlist/detail", NeteaseProtocol.Eapi,
        new() { ["id"] = id, ["n"] = 0, ["s"] = 0 });
    internal static DailyPlayerRequest Tracks(IReadOnlyList<long> ids) => new("/api/v3/song/detail", NeteaseProtocol.Eapi,
        new() { ["c"] = JsonSerializer.Serialize(ids.Select(id => new { id })) });
    internal static DailyPlayerRequest Lyrics(long id, bool wordLevel) => wordLevel
        ? new("/api/song/lyric/v1", NeteaseProtocol.Eapi, new()
        { ["id"] = id, ["cp"] = false, ["tv"] = 0, ["lv"] = 0, ["rv"] = 0, ["kv"] = 0, ["yv"] = 0, ["ytv"] = 0, ["yrv"] = 0 })
        : new("/api/song/lyric", NeteaseProtocol.Eapi, new()
        { ["id"] = id, ["tv"] = -1, ["lv"] = -1, ["rv"] = -1, ["kv"] = -1, ["_nmclfl"] = 1 });
}
