using MusicNetEasePlugin.Application.Playback;
namespace MusicNetEasePlugin.Application.Discovery;
public enum RemoteHistoryKind { Recent, Week, AllTime }
/// <summary>分值与次数分别保存，未知时间保持 null，不以抓取时间冒充听歌时间。</summary>
public sealed record RemoteListeningRecord(MusicTrack Track, DateTimeOffset? PlayedAt = null, long? PlayCount = null, long? Score = null);
public interface IRemoteHistoryApi
{
    Task<CatalogPage<RemoteListeningRecord>> ReadAsync(RemoteHistoryKind kind, MusicSession session, CancellationToken ct);
}
