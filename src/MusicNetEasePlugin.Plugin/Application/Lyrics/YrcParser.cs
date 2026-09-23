using System.Globalization;
using System.Text.RegularExpressions;

namespace MusicNetEasePlugin.Application.Lyrics;

/// <summary>
/// 网易逐字轨道：行 [起点,时长]，字 (绝对起点,时长,0)文字，单位均为毫秒。
/// 先验证整个轨道再发布；负数、越界、倒序、重叠或上限破坏时退回 LRC，不把错轴高亮当作成功。
/// 保留空格与字内括号，元信息 JSON 不参与时间轴。最多 10,000 行、100,000 字片段。
/// </summary>
internal static class YrcParser
{
    private static readonly Regex Header = new(@"^\[(\d{1,12}),(\d{1,12})\]", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Word = new(@"\((\d{1,12}),(\d{1,12}),0\)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex BrokenWord = new(@"\([+-]?\d+,", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    internal static IReadOnlyList<LyricLine>? Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (source.Length > 1024 * 1024) return null;
        var lines = new List<LyricLine>(); var totalWords = 0; var sourceLines = 0;
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } raw)
        {
            if (++sourceLines > LyricParser.MaximumLines || raw.Length > 8192) return null;
            var line = raw.Trim(); if (line.Length == 0 || line.StartsWith('{')) continue;
            var header = Header.Match(line); if (!header.Success) return null;
            var start = Number(header.Groups[1].Value); var length = Number(header.Groups[2].Value);
            if (length <= 0) return null;
            var body = line[header.Length..]; var tags = Word.Matches(body);
            if (tags.Count == 0 || tags[0].Index != 0 || (totalWords += tags.Count) > 100000) return null;
            var words = new List<LyricWord>(); var previousEnd = start;
            for (var index = 0; index < tags.Count; index++)
            {
                var tag = tags[index]; var wordStart = Number(tag.Groups[1].Value); var duration = Number(tag.Groups[2].Value);
                var textStart = tag.Index + tag.Length;
                var text = body[textStart..(index + 1 < tags.Count ? tags[index + 1].Index : body.Length)];
                if (wordStart < previousEnd || wordStart < start || wordStart + duration > start + length || BrokenWord.IsMatch(text)) return null;
                words.Add(new(wordStart, duration, text)); previousEnd = wordStart + duration;
            }
            lines.Add(new(start, string.Concat(words.Select(word => word.Text)), Words: Array.AsReadOnly(words.ToArray())));
        }
        return Array.AsReadOnly(lines.OrderBy(line => line.StartMs).ToArray());
    }
    private static long Number(string value) => long.Parse(value, CultureInfo.InvariantCulture);
}
