using MusicNetEasePlugin.Application.Playback;
namespace MusicNetEasePlugin.Application.Discovery;

/// <summary>NextOffset 按原始返回推进；Complete 只在完整性有依据时为真，不能以当前可见页代替全集。</summary>
public sealed record CatalogPage<T>(IReadOnlyList<T> Items, int NextOffset = 0, bool HasMore = false, bool Complete = true);
public enum MusicCardKind { Playlist, Artist, Album }
public sealed record MusicCard(long Id, string Name, string? Cover = null, string Description = "", MusicCardKind Kind = MusicCardKind.Playlist);
public enum RecommendationKind { Personal, General }
public enum ArtistSongOrder { Hot, Time }
public sealed record ArtistProfile(long Id, string Name, string Description, string? Cover);
public sealed record AlbumContent(MusicCard Album, CatalogPage<MusicTrack> Songs);
/// <summary>发现只读端口，不授予播放、收藏或反馈权限；会话和取消沿用音乐访问边界。</summary>
public interface IDiscoveryCatalogApi
{
    Task<CatalogPage<MusicTrack>> DailyAsync(MusicSession session, CancellationToken ct);
    Task<CatalogPage<MusicCard>> PlaylistsAsync(RecommendationKind kind, MusicSession session, CancellationToken ct);
    Task<CatalogPage<MusicCard>> ChartsAsync(MusicSession session, CancellationToken ct);
}
/// <summary>作品仅按可信 ID 查询；分页和排序属于内容读取，不拥有队列。</summary>
public interface IArtistAlbumApi
{
    Task<ArtistProfile> ArtistAsync(long id, MusicSession session, CancellationToken ct);
    Task<CatalogPage<MusicTrack>> SongsAsync(long id, int offset, ArtistSongOrder order, MusicSession session, CancellationToken ct);
    Task<CatalogPage<MusicCard>> AlbumsAsync(long id, int offset, MusicSession session, CancellationToken ct);
    Task<AlbumContent> AlbumAsync(long id, MusicSession session, CancellationToken ct);
}
/// <summary>客户端预算，不代表远端承诺返回数量。修改必须同时更新边界测试。</summary>
public static class DiscoveryLimits
{
    public const int PageSize = 50, Items = 1000, Navigation = 8;
    public static void Id(long id) { if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id)); }
    public static void Offset(int offset) { if (offset < 0 || offset >= Items) throw new ArgumentOutOfRangeException(nameof(offset)); }
}
