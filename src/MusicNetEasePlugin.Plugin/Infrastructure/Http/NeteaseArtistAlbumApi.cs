using System.Text.Json;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;
namespace MusicNetEasePlugin.Infrastructure.Http;
/// <summary>作品身份在解析边界核验；错误对象不能进入作品导航。</summary>
internal sealed class NeteaseArtistAlbumApi(MusicRequestExecutor read) : IArtistAlbumApi
{
    public Task<ArtistProfile> ArtistAsync(long id, MusicSession session, CancellationToken ct) => read.ExecuteAsync(DiscoveryRequests.Artist(id), session, json =>
    {
        var artist = DiscoveryJson.Required(DiscoveryJson.Required(json, "data", JsonValueKind.Object), "artist", JsonValueKind.Object);
        if (DiscoveryJson.Number(artist, "id") != id) throw new JsonException();
        return new ArtistProfile(id, DiscoveryJson.Text(artist, "name"), DiscoveryJson.Text(artist, "briefDesc"), DiscoveryJson.Text(artist, "cover"));
    }, ct);
    public Task<CatalogPage<MusicTrack>> SongsAsync(long id, int offset, ArtistSongOrder order, MusicSession session, CancellationToken ct) => read.ExecuteAsync(DiscoveryRequests.Songs(id, offset, order), session,
        json => DiscoveryJson.Page(DiscoveryJson.Required(json, "songs", JsonValueKind.Array), DiscoveryJson.Song, offset, DiscoveryJson.More(json), DiscoveryLimits.PageSize), ct);
    public Task<CatalogPage<MusicCard>> AlbumsAsync(long id, int offset, MusicSession session, CancellationToken ct) => read.ExecuteAsync(DiscoveryRequests.Albums(id, offset), session,
        json => DiscoveryJson.Page(DiscoveryJson.Required(json, "hotAlbums", JsonValueKind.Array), DiscoveryJson.Card, offset, DiscoveryJson.More(json), DiscoveryLimits.PageSize), ct);
    public Task<AlbumContent> AlbumAsync(long id, MusicSession session, CancellationToken ct) => read.ExecuteAsync(DiscoveryRequests.Album(id), session, json =>
    {
        var album = DiscoveryJson.Required(json, "album", JsonValueKind.Object); var card = DiscoveryJson.Card(album);
        if (card.Id != id) throw new JsonException();
        var songs = DiscoveryJson.Page(DiscoveryJson.Required(json, "songs", JsonValueKind.Array), DiscoveryJson.Song);
        // 数量缺失时不承诺完整，仍允许明确播放已加载范围。
        return new AlbumContent(card, songs with { Complete = songs.Complete && DiscoveryJson.Number(album, "size") == songs.Items.Count });
    }, ct);
}
