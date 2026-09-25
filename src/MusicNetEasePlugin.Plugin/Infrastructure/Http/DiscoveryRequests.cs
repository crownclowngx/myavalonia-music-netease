using MusicNetEasePlugin.Application.Discovery;
namespace MusicNetEasePlugin.Infrastructure.Http;
/// <summary>固定上游 a8c781f 的请求描述；默认加密端点明确选择 eapi，不依赖外部配置。</summary>
internal static class DiscoveryRequests
{
    internal static DailyPlayerRequest Daily() => new("/api/v3/discovery/recommend/songs", NeteaseProtocol.Weapi, new());
    internal static DailyPlayerRequest Playlists(RecommendationKind kind) => kind switch
    {
        RecommendationKind.Personal => new("/api/v1/discovery/recommend/resource", NeteaseProtocol.Weapi, new()),
        RecommendationKind.General => new("/api/personalized/playlist", NeteaseProtocol.Weapi, new() { ["limit"] = 30, ["total"] = true, ["n"] = 1000 }),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    internal static DailyPlayerRequest Charts() => new("/api/toplist", NeteaseProtocol.Eapi, new());
    internal static DailyPlayerRequest Artist(long id) { DiscoveryLimits.Id(id); return new("/api/artist/head/info/get", NeteaseProtocol.Eapi, new() { ["id"] = id }); }
    internal static DailyPlayerRequest Songs(long id, int offset, ArtistSongOrder order)
    {
        DiscoveryLimits.Id(id); DiscoveryLimits.Offset(offset);
        if (!Enum.IsDefined(order)) throw new ArgumentOutOfRangeException(nameof(order));
        return new("/api/v1/artist/songs", NeteaseProtocol.Eapi, new() { ["id"] = id, ["private_cloud"] = "true", ["work_type"] = 1, ["order"] = order == ArtistSongOrder.Hot ? "hot" : "time", ["offset"] = offset, ["limit"] = DiscoveryLimits.PageSize });
    }
    internal static DailyPlayerRequest Albums(long id, int offset)
    {
        DiscoveryLimits.Id(id); DiscoveryLimits.Offset(offset);
        return new($"/api/artist/albums/{id}", NeteaseProtocol.Weapi, new() { ["offset"] = offset, ["limit"] = DiscoveryLimits.PageSize, ["total"] = true });
    }
    internal static DailyPlayerRequest Album(long id) { DiscoveryLimits.Id(id); return new($"/api/v1/album/{id}", NeteaseProtocol.Weapi, new()); }
    internal static DailyPlayerRequest Fm() => new("/api/v1/radio/get", NeteaseProtocol.Weapi, new());
    internal static DailyPlayerRequest Dislike(long id) { DiscoveryLimits.Id(id); return new("/api/radio/trash/add", NeteaseProtocol.Weapi, new() { ["songId"] = id, ["alg"] = "RT", ["time"] = 25 }); }
    internal static DailyPlayerRequest History(RemoteHistoryKind kind, long accountId)
    {
        DiscoveryLimits.Id(accountId);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        return kind == RemoteHistoryKind.Recent ? new("/api/play-record/song/list", NeteaseProtocol.Weapi, new() { ["limit"] = DiscoveryLimits.Items })
            : new("/api/v1/play/record", NeteaseProtocol.Weapi, new() { ["uid"] = accountId, ["type"] = kind == RemoteHistoryKind.Week ? 1 : 0 });
    }
}
