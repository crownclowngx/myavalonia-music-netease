namespace MusicNetEasePlugin.Application.Playback;

public enum PlaybackMode { Sequential, RepeatList, RepeatOne, Shuffle }
/// <summary>一次已接纳的追加结果；UI 不再观察总数变化猜测操作来源和意图。</summary>
public sealed record QueueAdditionResult(int Added, bool PlayNext, long AccountEpoch)
{
    public string Message => Added == 0 ? "没有新增歌曲" : PlayNext ? $"将在当前歌曲后播放 · 本次 {Added} 首" : $"已加入队列 · 本次 {Added} 首";
}
public sealed record RestoredQueue(Guid Id, int Count, long PositionMs);
public sealed record QueueUndoInfo(Guid Id, string Description);
/// <summary>拖动从开始到释放携带同一身份与版本，不能用 TrackId 或过期行下标替代。</summary>
public sealed record QueueMoveIntent(Guid EntryId, int TargetIndex, long QueueRevision, long AccountEpoch);
/// <summary>同一歌曲可重复入队，因此操作必须使用 EntryId；TrackId 仅用于向网易解析资料和资源。</summary>
public sealed record QueueEntry(Guid EntryId, long TrackId, string Source, long? SourceId = null, MusicTrack? Track = null)
{
    public string Display => Track?.Display ?? $"歌曲 {TrackId}（待加载）";
    public static QueueEntry FromTrack(MusicTrack track, string source = "搜索", long? sourceId = null) => new(Guid.NewGuid(), track.Id, source, sourceId, track);
}
public sealed record PlayerSessionSnapshot(long Revision, long QueueRevision, long AccountId, long AccountEpoch,
    IReadOnlyList<QueueEntry> Entries, Guid? CurrentEntryId, PlaybackMode Mode, PlaybackSnapshot Playback,
    bool CanPrevious = false, bool CanNext = false, RestoredQueue? Restoration = null, QueueUndoInfo? Undo = null)
{
    public static PlayerSessionSnapshot Empty { get; } = new(0, 0, 0, 0, Array.Empty<QueueEntry>(), null, PlaybackMode.Sequential, new(0, PlaybackState.Idle));
}

/// <summary>页面可用的最小共享播放器端口。接纳后由账号和音乐 Document 共享租约拥有；最后租约归还时释放播放资源，不暴露媒体路径和凭据。</summary>
public interface IPlayerSession
{
    PlayerSessionSnapshot Snapshot { get; }
    event EventHandler<PlayerSessionSnapshot>? Changed;
    Task ReplaceAsync(IReadOnlyList<QueueEntry> entries, int startIndex, CancellationToken ct);
    Task PlaySingleAsync(MusicTrack track, CancellationToken ct);
    /// <summary>明确的单曲试听意图：保留原队列，在当前项后插入并播放；已是当前曲目则继续。</summary>
    Task PlayNowAsync(QueueEntry entry, CancellationToken ct);
    Task<QueueAdditionResult> EnqueueAsync(IReadOnlyList<QueueEntry> entries, bool playNext, CancellationToken ct);
    Task SelectAsync(Guid entryId, CancellationToken ct);
    Task NextAsync(bool previous, CancellationToken ct);
    Task RemoveAsync(Guid entryId, CancellationToken ct);
    void Move(Guid entryId, int direction);
    bool MoveTo(QueueMoveIntent intent);
    bool UndoQueueChange(Guid undoId, long accountEpoch);
    void SetMode(PlaybackMode mode);
    Task ClearAsync();
    Task ClearIfUnchangedAsync(long expectedQueueRevision);
    Task StopAsync();
    Task PauseAsync(bool paused, CancellationToken ct);
    Task SetVolumeAsync(int volume, CancellationToken ct);
    Task SeekAsync(Guid entryId, long generation, long positionMs, CancellationToken ct);
}
