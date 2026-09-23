using System.Globalization;
using System.Text.RegularExpressions;

namespace MusicNetEasePlugin.Application.Lyrics;

/// <summary>
/// 有界纯解析：最多 10,000 个时间行，不依赖 UI、网络或当前文化。多标签复制原文，稳定排序保留同刻原顺序。
/// 先读取全局 offset 再修正全部时间；无效标签忽略，纯文本保留供用户阅读，不伪造时间轴。
/// </summary>
public static class LyricParser
{
    public const int MaximumLines = 10000;
    private static readonly Regex Stamp = new(@"\[(\d{1,8}):([0-5]?\d)(?:\.(\d{1,3}))?\]", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Offset = new(@"\[offset:([+-]?\d{1,10})\]", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | RegexOptions.IgnoreCase);
    public static LyricDocument Parse(RawLyrics raw) => ParseLrc(raw.Lrc, raw.Instrumental);
    internal static LyricDocument ParseLrc(string? text, bool instrumental = false)
    {
        if (instrumental) return new(Array.Empty<LyricLine>(), "", "纯音乐，暂无歌词。");
        if (string.IsNullOrWhiteSpace(text)) return new(Array.Empty<LyricLine>(), "", "暂无歌词。");
        if (text.Length > 1024 * 1024) return new(Array.Empty<LyricLine>(), "", "歌词超过解析上限。");
        long offset = 0;
        foreach (Match match in Offset.Matches(text))
            if (long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) offset = parsed;
        var lines = new List<LyricLine>(); var plain = new List<string>(); var limited = false; var sourceLines = 0;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } source)
        {
            if (++sourceLines > MaximumLines) { limited = true; break; }
            if (source.Length > 8192) { limited = true; continue; }
            var matches = Stamp.Matches(source); var content = Stamp.Replace(source, "").Trim();
            if (matches.Count == 0)
            {
                if (content.Length > 0 && !content.StartsWith('[')) plain.Add(content);
                continue;
            }
            foreach (Match match in matches)
            {
                if (lines.Count >= MaximumLines) { limited = true; break; }
                var minutes = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var milliseconds = match.Groups[3].Success ? int.Parse(match.Groups[3].Value.PadRight(3, '0'), CultureInfo.InvariantCulture) : 0;
                lines.Add(new(Math.Max(0, minutes * 60000 + seconds * 1000 + milliseconds + offset), content));
            }
        }
        return new(Array.AsReadOnly(lines.OrderBy(line => line.StartMs).ToArray()), string.Join('\n', plain),
            limited ? "歌词部分内容超过解析上限。" : lines.Count > 0 ? "逐行歌词" : plain.Count > 0 ? "仅纯文本歌词，无同步时间。" : "暂无有效歌词。");
    }
}
