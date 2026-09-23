using System.Text.Json;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>只读歌词接口复用账号提交保护和 1 MiB 响应上限；轨道缺失是内容状态，不是播放故障。</summary>
internal sealed class NeteaseLyricsApi(MusicRequestExecutor requests) : ILyricsApi
{
    public async Task<RawLyrics> GetAsync(long trackId, MusicSession session, CancellationToken ct)
    {
        if (trackId <= 0) throw new ArgumentOutOfRangeException(nameof(trackId));
        try { return await Read(true).ConfigureAwait(false); }
        catch (MusicException ex) when (ex.Kind is MusicError.Network or MusicError.Protocol)
        { return await Read(false).ConfigureAwait(false); } // 新轨道接口暂不可用时保留旧逐行闭环；失效/限流不重复请求。
        Task<RawLyrics> Read(bool words) => requests.ExecuteAsync(DailyPlayerRequests.Lyrics(trackId, words), session,
            body => new RawLyrics(Track(body, "lrc"), body.TryGetProperty("nolyric", out var flag) && flag.ValueKind == JsonValueKind.True,
                Track(body, "tlyric"), Track(body, "yrc"), Track(body, "ytlrc")), ct);
    }
    internal static string? Track(JsonElement body, string name) => body.TryGetProperty(name, out var track) && track.ValueKind == JsonValueKind.Object &&
        track.TryGetProperty("lyric", out var lyric) && lyric.ValueKind == JsonValueKind.String ? lyric.GetString() : null;
}
