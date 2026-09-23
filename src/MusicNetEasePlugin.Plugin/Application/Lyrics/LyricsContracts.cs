using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Application.Lyrics;

/// <summary>原始轨道仅存在于受限请求与解析期间；不进入播放快照或磁盘恢复文件。</summary>
public sealed record RawLyrics(string? Lrc, bool Instrumental = false, string? Translation = null, string? Yrc = null);
public interface ILyricsApi { Task<RawLyrics> GetAsync(long trackId, MusicSession session, CancellationToken ct); }
public sealed record LyricWord(long StartMs, long DurationMs, string Text);
public sealed record LyricLine(long StartMs, string Text, string? Translation = null, IReadOnlyList<LyricWord>? Words = null);
public sealed record LyricDocument(IReadOnlyList<LyricLine> Lines, string PlainText, string Status)
{
    public static LyricDocument Empty { get; } = new(Array.Empty<LyricLine>(), "", "尚未选择歌曲。");
}
public sealed record LyricsSnapshot(long Revision, long TrackId, long AccountEpoch, LyricDocument Document,
    int CurrentLine, long PositionMs, bool Loading = false, bool Failed = false, bool SyncLimited = false, string Message = "");

/// <summary>歌词只订阅媒体事实；时间查找用二分，不为每行、每个页面创建时钟。</summary>
public static class LyricTimeline
{
    public static int FindLine(IReadOnlyList<LyricLine> lines, long positionMs)
    {
        var lo = 0; var hi = lines.Count - 1;
        while (lo <= hi) { var mid = lo + (hi - lo) / 2; if (lines[mid].StartMs <= positionMs) lo = mid + 1; else hi = mid - 1; }
        return hi;
    }
}
