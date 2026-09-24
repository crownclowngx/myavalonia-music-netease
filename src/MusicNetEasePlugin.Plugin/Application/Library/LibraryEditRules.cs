using System.Buffers;
using System.Globalization;
using System.Text;

namespace MusicNetEasePlugin.Application.Library;

/// <summary>纯业务规则集中在这里，UI 和协调器复用。Unicode 标量不是 UTF-16 代码单元，emoji 不能被错误计为两个字。</summary>
public static class LibraryEditRules
{
    public const int MaximumNameLength = 100;
    public const int MaximumDescriptionLength = 1000;
    public const int MaximumTracksPerWrite = 50;
    public const int MaximumLikedSongs = 50000;
    public static bool TryId(string? value, out long id) => long.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    public static string Name(string? value)
    {
        var result = (value ?? "").Trim();
        ValidateText(result, MaximumNameLength, false);
        if (result.Length == 0) throw new ArgumentException("歌单名称不能为空。");
        return result;
    }
    public static string Description(string? value)
    {
        var result = (value ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        ValidateText(result, MaximumDescriptionLength, true);
        return result;
    }
    private static void ValidateText(string value, int limit, bool multiline)
    {
        var span = value.AsSpan(); var count = 0;
        while (!span.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(span, out var rune, out var consumed) != OperationStatus.Done)
                throw new ArgumentException("文本包含无效字符。");
            if (Rune.IsControl(rune) && !(multiline && rune.Value is 10 or 9))
                throw new ArgumentException("文本包含不允许的控制字符或换行。");
            if (++count > limit) throw new ArgumentException($"文本最多允许 {limit} 个 Unicode 字符。");
            span = span[consumed..];
        }
    }
    public static long[] Tracks(IReadOnlyList<long>? ids)
    {
        if (ids is null || ids.Count is < 1 or > MaximumTracksPerWrite || ids.Any(id => id <= 0))
            throw new ArgumentException("每次请提交 1–50 个有效歌曲 ID。");
        return ids.Distinct().ToArray();
    }
    public static LibraryIntent Normalize(LibraryIntent intent)
    {
        if (!Enum.IsDefined(intent.Kind)) throw new ArgumentException("不支持的音乐库操作。");
        if (intent.Kind != LibraryOperationKind.Create && intent.TargetId <= 0) throw new ArgumentException("目标 ID 无效。");
        if (intent.Baseline is { } baseline) intent = intent with { Baseline = baseline with { TrackIds = Array.AsReadOnly(baseline.TrackIds.ToArray()) } };
        return intent.Kind switch
        {
            LibraryOperationKind.Create or LibraryOperationKind.Rename => intent with { Text = Name(intent.Text) },
            LibraryOperationKind.Description => intent with { Text = Description(intent.Text) },
            LibraryOperationKind.AddTracks or LibraryOperationKind.RemoveTracks => intent with { TrackIds = Array.AsReadOnly(Tracks(intent.TrackIds)) },
            _ => intent
        };
    }
    public static string ResourceKey(LibraryIntent intent) => intent.Kind switch
    {
        LibraryOperationKind.Like => "song:" + intent.TargetId,
        LibraryOperationKind.Create => "create",
        _ => "playlist:" + intent.TargetId
    };
    /// <summary>草稿只比较当前要修改的字段；删除同时检查名称和更新标识，避免陈旧确认删除已经变化的目标。</summary>
    public static bool Conflicts(LibraryIntent intent, LibraryPlaylist actual) => intent.Baseline is { } baseline &&
        (baseline.Id != actual.Id || baseline.CreatorId != actual.CreatorId || baseline.Kind != actual.Kind ||
        intent.Kind switch
        {
            LibraryOperationKind.Rename => baseline.Name != actual.Name,
            LibraryOperationKind.Description => baseline.DescriptionKnown != actual.DescriptionKnown || baseline.Description != actual.Description,
            LibraryOperationKind.Delete => baseline.Name != actual.Name || baseline.UpdateTime != actual.UpdateTime || !baseline.TrackIds.SequenceEqual(actual.TrackIds),
            LibraryOperationKind.RemoveTracks => !baseline.TrackIds.SequenceEqual(actual.TrackIds),
            _ => false
        });
    public static IReadOnlyList<LibraryItemResult> Classify(IReadOnlyList<long> requested, IReadOnlyList<long> before,
        IReadOnlyList<long>? after, bool add)
    {
        var previous = before.ToHashSet(); var current = after?.ToHashSet();
        return requested.Select(id => new LibraryItemResult(id,
            previous.Contains(id) == add ? (add ? LibraryItemState.AlreadyPresent : LibraryItemState.AlreadyAbsent) :
            current is not null && current.Contains(id) == add ? (add ? LibraryItemState.Added : LibraryItemState.Removed) :
            LibraryItemState.NeedsRecheck)).ToArray();
    }
}
