namespace MusicNetEasePlugin.Application.Playback;

/// <summary>持久化专用白名单模型，不复用含封面 URL 或播放资源的对象，防止未来新增字段意外落盘。</summary>
public sealed record StoredEntry(Guid EntryId, long TrackId, string Source, long? SourceId = null,
    string? Name = null, string? Artists = null, string? Album = null, long DurationMs = 0)
{
    public QueueEntry ToQueueEntry() => new(EntryId, TrackId, Source, SourceId,
        Name is null ? null : new(TrackId, Name, Artists ?? "", Album ?? "", null, DurationMs));
    public static StoredEntry From(QueueEntry entry) => new(entry.EntryId, entry.TrackId, Short(entry.Source, 32), entry.SourceId,
        entry.Track is null ? null : Short(entry.Track.Name, 256), Short(entry.Track?.Artists, 128), Short(entry.Track?.Album, 256), Math.Clamp(entry.Track?.DurationMs ?? 0, 0, 86400000));
    internal static string Short(string? text, int length) => text is null ? "" : text[..Math.Min(length, text.Length)];
}
public sealed record RecentTrack(long TrackId, string Name, string Artists, string Album, long DurationMs, DateTimeOffset PlayedAt)
{
    public MusicTrack ToTrack() => new(TrackId, Name, Artists, Album, null, DurationMs);
}
public sealed record PlaybackStateData(int SchemaVersion, long AccountId, long Revision, IReadOnlyList<StoredEntry> Entries,
    Guid? CurrentEntryId, PlaybackMode Mode, int Volume, long PositionMs, IReadOnlyList<RecentTrack> Recent)
{
    public static PlaybackStateData Empty(long accountId) => new(1, accountId, 0, Array.Empty<StoredEntry>(), null, PlaybackMode.Sequential, 70, 0, Array.Empty<RecentTrack>());
}
public sealed record PlaybackStateRead(PlaybackStateData? Data, string Error = "", bool ProtectedFile = false);
public interface IPlaybackStateStore
{
    Task<PlaybackStateRead> LoadAsync(long accountId, CancellationToken ct);
    Task SaveAsync(PlaybackStateData state, CancellationToken ct);
}
public sealed record PlaybackStorageSnapshot(long Revision, long AccountId, IReadOnlyList<RecentTrack> Recent, string Message = "", bool CleanupRequired = false);
