using System.Text.Json;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;
namespace MusicNetEasePlugin.Infrastructure.Http;
/// <summary>保持服务端顺序和重复记录，无本地历史写入及上报依赖。</summary>
internal sealed class NeteaseRemoteHistoryApi(MusicRequestExecutor read) : IRemoteHistoryApi
{
    public Task<CatalogPage<RemoteListeningRecord>> ReadAsync(RemoteHistoryKind kind, MusicSession session, CancellationToken ct) => read.ExecuteAsync(DiscoveryRequests.History(kind, session.AccountId), session, json =>
    {
        var recent = kind == RemoteHistoryKind.Recent;
        var array = recent ? DiscoveryJson.Required(DiscoveryJson.Required(json, "data", JsonValueKind.Object), "list", JsonValueKind.Array)
            : DiscoveryJson.Required(json, kind == RemoteHistoryKind.Week ? "weekData" : "allData", JsonValueKind.Array);
        return DiscoveryJson.Page(array, item =>
        {
            var song = DiscoveryJson.Song(DiscoveryJson.Required(item, recent ? "data" : "song", JsonValueKind.Object));
            var millis = DiscoveryJson.Number(item, "playTime");
            DateTimeOffset? played = millis is > 0 and <= 253402300799999 ? DateTimeOffset.FromUnixTimeMilliseconds(millis.Value) : null;
            return new RemoteListeningRecord(song, recent ? played : null, recent ? null : DiscoveryJson.Number(item, "playCount"), recent ? null : DiscoveryJson.Number(item, "score"));
        }) with { Complete = false };
    }, ct);
}
