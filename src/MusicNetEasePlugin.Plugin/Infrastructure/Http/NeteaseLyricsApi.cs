using System.Text.Json;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>只读歌词接口复用账号提交保护和 1 MiB 响应上限；轨道缺失是内容状态，不是播放故障。</summary>
internal sealed class NeteaseLyricsApi(MusicRequestExecutor requests) : ILyricsApi
{
    public Task<RawLyrics> GetAsync(long trackId, MusicSession session, CancellationToken ct)
    {
        if (trackId <= 0) throw new ArgumentOutOfRangeException(nameof(trackId));
        return requests.ExecuteAsync(DailyPlayerRequests.Lyrics(trackId, false), session,
            body => new RawLyrics(Track(body, "lrc"), body.TryGetProperty("nolyric", out var flag) && flag.ValueKind == JsonValueKind.True), ct);
    }
    internal static string? Track(JsonElement body, string name) => body.TryGetProperty(name, out var track) && track.ValueKind == JsonValueKind.Object &&
        track.TryGetProperty("lyric", out var lyric) && lyric.ValueKind == JsonValueKind.String ? lyric.GetString() : null;
}
