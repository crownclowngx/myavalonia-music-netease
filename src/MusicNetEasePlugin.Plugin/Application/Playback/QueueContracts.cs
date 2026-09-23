namespace MusicNetEasePlugin.Application.Playback;

public enum PlaybackMode { Sequential, RepeatList, RepeatOne, Shuffle }
/// <summary>同一歌曲可重复入队，因此操作必须使用 EntryId；TrackId 仅用于向网易解析资料和资源。</summary>
public sealed record QueueEntry(Guid EntryId, long TrackId, string Source, long? SourceId = null, MusicTrack? Track = null)
{
    public string Display => Track?.Display ?? $"歌曲 {TrackId}（待加载）";
    public static QueueEntry FromTrack(MusicTrack track, string source = "搜索", long? sourceId = null) => new(Guid.NewGuid(), track.Id, source, sourceId, track);
}
public sealed record PlayerSessionSnapshot(long Revision, long QueueRevision, long AccountId, long AccountEpoch,
    IReadOnlyList<QueueEntry> Entries, Guid? CurrentEntryId, PlaybackMode Mode, PlaybackSnapshot Playback,
    bool CanPrevious = false, bool CanNext = false)
{
    public static PlayerSessionSnapshot Empty { get; } = new(0, 0, 0, 0, Array.Empty<QueueEntry>(), null, PlaybackMode.Sequential, new(0, PlaybackState.Idle));
}

/// <summary>页面可用的最小共享播放器端口。接纳前观察页面取消，接纳后由账号和插件寿命拥有；不暴露媒体路径和凭据。</summary>
public interface IPlayerSession
{
    PlayerSessionSnapshot Snapshot { get; }
    event EventHandler<PlayerSessionSnapshot>? Changed;
    Task ReplaceAsync(IReadOnlyList<QueueEntry> entries, int startIndex, CancellationToken ct);
    Task PlaySingleAsync(MusicTrack track, CancellationToken ct);
    Task EnqueueAsync(IReadOnlyList<QueueEntry> entries, bool playNext, CancellationToken ct);
    Task SelectAsync(Guid entryId, CancellationToken ct);
    Task NextAsync(bool previous, CancellationToken ct);
    Task RemoveAsync(Guid entryId, CancellationToken ct);
    void Move(Guid entryId, int direction);
    void SetMode(PlaybackMode mode);
    Task ClearAsync();
    Task StopAsync();
    Task PauseAsync(bool paused, CancellationToken ct);
    Task SetVolumeAsync(int volume, CancellationToken ct);
    Task SeekAsync(Guid entryId, long generation, long positionMs, CancellationToken ct);
}
