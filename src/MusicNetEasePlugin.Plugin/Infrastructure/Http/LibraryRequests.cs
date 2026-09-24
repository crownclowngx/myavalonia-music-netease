using System.Globalization;
using System.Text.Json;
using MusicNetEasePlugin.Application.Library;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>固定提交已核对的 M3 原生端点。所有写入都是明确目标，不执行上游包装器的隐式重试。</summary>
internal static class LibraryRequests
{
    internal static DailyPlayerRequest Likes(long accountId) => new("/api/song/like/get", NeteaseProtocol.Eapi, new() { ["uid"] = accountId });
    internal static DailyPlayerRequest Like(long id, bool desired) => new("/api/radio/like", NeteaseProtocol.Weapi,
        new() { ["trackId"] = id, ["like"] = desired, ["alg"] = "itembased", ["time"] = "3" });
    internal static DailyPlayerRequest Dynamic(long id) => new("/api/playlist/detail/dynamic", NeteaseProtocol.Eapi,
        new() { ["id"] = id, ["n"] = 0, ["s"] = 0 });
    internal static DailyPlayerRequest Write(LibraryIntent raw)
    {
        var intent = LibraryEditRules.Normalize(raw);
        return intent.Kind switch
        {
            LibraryOperationKind.Subscribe => new(intent.Desired ? "/api/playlist/subscribe" : "/api/playlist/unsubscribe", NeteaseProtocol.Eapi, new() { ["id"] = intent.TargetId }),
            LibraryOperationKind.Create => new("/api/playlist/create", NeteaseProtocol.Weapi, new() { ["name"] = intent.Text, ["privacy"] = "0", ["type"] = "NORMAL" }),
            LibraryOperationKind.Rename => new("/api/playlist/update/name", NeteaseProtocol.Eapi, new() { ["id"] = intent.TargetId, ["name"] = intent.Text }),
            LibraryOperationKind.Description => new("/api/playlist/desc/update", NeteaseProtocol.Eapi, new() { ["id"] = intent.TargetId, ["desc"] = intent.Text }),
            LibraryOperationKind.AddTracks or LibraryOperationKind.RemoveTracks => new("/api/playlist/manipulate/tracks", NeteaseProtocol.Eapi,
                new() { ["pid"] = intent.TargetId, ["op"] = intent.Kind == LibraryOperationKind.AddTracks ? "add" : "del",
                    ["trackIds"] = JsonSerializer.Serialize(intent.TrackIds!.Select(id => id.ToString(CultureInfo.InvariantCulture))), ["imme"] = "true" }),
            LibraryOperationKind.Delete => new("/api/playlist/remove", NeteaseProtocol.Weapi, new() { ["ids"] = JsonSerializer.Serialize(new[] { intent.TargetId }) }),
            _ => throw new ArgumentException("不支持的歌单操作。")
        };
    }
}

/// <summary>令牌仅在基础设施内部短期存在。默认字符串隐藏内容，不能进入日志、UI 或持久化。</summary>
internal sealed record LibraryCheckToken(string Value)
{
    public override string ToString() => "[临时音乐库令牌]";
}
internal interface ILibraryCheckTokenProvider
{
    Task<LibraryCheckToken> GetAsync(CancellationToken ct);
}
