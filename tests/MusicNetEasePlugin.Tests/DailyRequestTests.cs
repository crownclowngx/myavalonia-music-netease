using System.Text;
using System.Text.Json;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Infrastructure.Protocol;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class DailyRequestTests
{
    [Fact, Trait("M2", "P01")]
    public void 歌单与歌词固定协议保持长身份并以零详情获取完整顺序()
    {
        const long id = 9007199254740991;
        var lists = DailyPlayerRequests.Playlists(id, 30);
        Assert.Equal(NeteaseProtocol.Weapi, lists.Protocol);
        Assert.Equal("/api/user/playlist", lists.Path);
        Assert.Equal(id, lists.Data["uid"]); Assert.Equal(30, lists.Data["offset"]);
        var playlist = DailyPlayerRequests.Playlist(id);
        var tracks = DailyPlayerRequests.Tracks([id, 1, id]);
        var lyric = DailyPlayerRequests.Lyrics(id, false);
        var words = DailyPlayerRequests.Lyrics(id, true);
        // 期望值独立写明，不能从描述对象复制期望；再经真实 eapi 编码和解码检查线上字段。
        Assert.Equal("/api/v6/playlist/detail", playlist.Path); Assert.Equal(0, playlist.Data["n"]);
        Assert.Equal("/api/v3/song/detail", tracks.Path);
        Assert.Equal("[{\"id\":9007199254740991},{\"id\":1},{\"id\":9007199254740991}]", tracks.Data["c"]);
        Assert.Equal("/api/song/lyric", lyric.Path); Assert.Equal(-1, lyric.Data["lv"]);
        Assert.Equal("/api/song/lyric/v1", words.Path); Assert.Equal(0, words.Data["yv"]); Assert.Equal(false, words.Data["cp"]);
        foreach (var request in new[] { playlist, tracks, lyric, words })
        {
            Assert.Equal(NeteaseProtocol.Eapi, request.Protocol);
            var encoded = NeteaseRequestEncoder.Encode(request.Path, request.Data, request.Protocol, AuthContext.Create(), DateTimeOffset.UnixEpoch);
            var parts = Encoding.UTF8.GetString(NeteaseCrypto.DecodeEapi(Convert.FromHexString(encoded.Form["params"]))).Split("-36cd479b6b5-");
            Assert.Equal(request.Path, parts[0]);
            using var data = JsonDocument.Parse(parts[1]);
            if (request == tracks) Assert.Equal(tracks.Data["c"], data.RootElement.GetProperty("c").GetString());
            else Assert.Equal(id, data.RootElement.GetProperty("id").GetInt64());
        }
    }
}
