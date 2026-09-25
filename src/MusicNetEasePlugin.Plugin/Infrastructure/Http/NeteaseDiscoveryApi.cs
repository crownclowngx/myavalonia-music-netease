using System.Text.Json;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;
namespace MusicNetEasePlugin.Infrastructure.Http;
/// <summary>推荐与榜单只读适配，不替换个性来源，也不把摘要当完整歌单。</summary>
internal sealed class NeteaseDiscoveryApi(MusicRequestExecutor read) : IDiscoveryCatalogApi
{
    public Task<CatalogPage<MusicTrack>> DailyAsync(MusicSession session, CancellationToken ct) => read.ExecuteAsync(DiscoveryRequests.Daily(), session,
        json => DiscoveryJson.Page(DiscoveryJson.Required(DiscoveryJson.Required(json, "data", JsonValueKind.Object), "dailySongs", JsonValueKind.Array), DiscoveryJson.Song), ct);
    public Task<CatalogPage<MusicCard>> PlaylistsAsync(RecommendationKind kind, MusicSession session, CancellationToken ct) => read.ExecuteAsync(DiscoveryRequests.Playlists(kind), session,
        json => DiscoveryJson.Page(DiscoveryJson.Required(json, kind == RecommendationKind.Personal ? "recommend" : "result", JsonValueKind.Array), DiscoveryJson.Card), ct);
    public Task<CatalogPage<MusicCard>> ChartsAsync(MusicSession session, CancellationToken ct) => read.ExecuteAsync(DiscoveryRequests.Charts(), session,
        json => DiscoveryJson.Page(DiscoveryJson.Required(json, "list", JsonValueKind.Array), DiscoveryJson.Card), ct);
}
