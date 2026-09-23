using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Application.Library;

/// <summary>远端歌单只读摘要；自建与收藏以当前核验账号和 creator 身份比较，不推测修改权限。</summary>
public sealed record MusicPlaylist(long Id, string Name, string? Cover, int TrackCount, bool IsOwned)
{
    public string Summary => $"{(IsOwned ? "自建" : "收藏")} · {TrackCount} 首";
}
public sealed record PlaylistPage(IReadOnlyList<MusicPlaylist> Items, int NextOffset, bool HasMore);
/// <summary>一次完整 ID 顺序快照；重复 ID 保留位置，资料缺失不会改变索引。超限/不完整不能提供播放全部。</summary>
public sealed record PlaylistTracks(long PlaylistId, string Name, IReadOnlyList<long> TrackIds, bool IsComplete, string Message);
public interface IPlaylistCatalogApi
{
    Task<PlaylistPage> PlaylistsAsync(int offset, MusicSession session, CancellationToken ct);
    Task<PlaylistTracks> PlaylistAsync(long id, MusicSession session, CancellationToken ct);
    Task<IReadOnlyDictionary<long, MusicTrack>> TracksAsync(IReadOnlyList<long> ids, MusicSession session, CancellationToken ct);
}
