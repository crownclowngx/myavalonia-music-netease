namespace MusicNetEasePlugin.Application.Playback;

/// <summary>
/// 容器内唯一队列写入者。短锁登记导航意图，单曲执行器负责 IO 与资源尾任务。
/// AttemptId 区分同一条目重复播放，终态只消费一次；页面的令牌在接纳后不再拥有歌曲。
/// </summary>
public sealed class PlaybackQueueCoordinator : IPlayerSession, IAsyncDisposable, IDisposable
{
    private readonly IMusicSessionAccessor _sessions;
    private readonly PlaybackCoordinator _single;
    private readonly QueueNavigator _order;
    private readonly PlaybackPersistence? _persistence;
    private long _resumePosition;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _closing = new();
    private CancellationTokenRegistration _revocation;
    private CancellationToken _accountToken;
    private Guid _attempt;
    private bool _terminalConsumed;
    private bool _closed;
    private readonly HashSet<Guid> _failedCandidates = [];
    private PlayerSessionSnapshot _snapshot = PlayerSessionSnapshot.Empty;
    private long _lastSingleRevision = -1;
    private Task _background = Task.CompletedTask;
    private Task? _shutdown;
    // 单步逆操作仅存必要结构；不保存整个播放器快照，也不引入命令栈或多级撤销框架。
    private sealed record QueueUndo(QueueUndoInfo Info, long Epoch, long Revision, Guid EntryId, int Index, QueueNavigator.RemovedEntry? Removed);
    private QueueUndo? _undo;
    public PlaybackQueueCoordinator(IMusicSessionAccessor sessions, PlaybackCoordinator single, PlaybackPersistence? persistence = null)
        : this(sessions, single, new QueueNavigator(), persistence) { }
    internal PlaybackQueueCoordinator(IMusicSessionAccessor sessions, PlaybackCoordinator single, QueueNavigator order, PlaybackPersistence? persistence = null)
    {
        (_sessions, _single, _order) = (sessions, single, order);
        _persistence = persistence;
        _single.Changed += SingleChanged;
    }
    public PlayerSessionSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    public event EventHandler<PlayerSessionSnapshot>? Changed;
    public Task PlaySingleAsync(MusicTrack track, CancellationToken ct) => ReplaceAsync([QueueEntry.FromTrack(track)], 0, ct);
    /// <summary>
    /// “插入、选中、启动”在唯一写入者的同一短锁中接纳，页面不能自行拼接三个异步命令。
    /// 同曲只与当前项比较：队列其他位置仍有独立 EntryId，不擅自合并用户安排的重复歌曲。
    /// 音频启动继续由单曲执行器在锁外完成，沿用旧尝试取消、账号撤销和有限失败推进规则。
    /// </summary>
    public Task PlayNowAsync(QueueEntry entry, CancellationToken ct)
    {
        Validate([entry]);
        var session = Capture(ct);
        Task work;
        var inserted = false;
        lock (_sync)
        {
            BindAccount(session);
            if (_order.Current?.TrackId == entry.TrackId)
            {
                work = _snapshot.Playback.State is PlaybackState.Playing or PlaybackState.Loading
                    ? Task.CompletedTask : PauseAsync(false, ct);
            }
            else
            {
                if (_order.Entries.Count >= 10000 || _order.Entries.Any(item => item.EntryId == entry.EntryId))
                    throw new MusicException(MusicError.Storage, "队列已满或条目标识重复，请整理队列后重试。");
                _order.InsertAndSelect(entry);
                _failedCandidates.Clear();
                PublishOrder();
                work = StartCurrent();
                inserted = true;
            }
        }
        Notify(explicitReplacement: inserted);
        return work;
    }
    public Task ReplaceAsync(IReadOnlyList<QueueEntry> entries, int startIndex, CancellationToken ct)
    {
        Validate(entries);
        if (entries.Count == 0 || startIndex < 0 || startIndex >= entries.Count) throw new ArgumentOutOfRangeException(nameof(startIndex));
        var session = Capture(ct); Task work;
        lock (_sync)
        {
            BindAccount(session); _order.Replace(entries, startIndex); _failedCandidates.Clear();
            PublishOrder(); work = StartCurrent();
        }
        Notify(explicitReplacement: true); return work;
    }
    public Task<QueueAdditionResult> EnqueueAsync(IReadOnlyList<QueueEntry> entries, bool playNext, CancellationToken ct)
    {
        Validate(entries); var session = Capture(ct);
        lock (_sync)
        {
            BindAccount(session);
            if (entries.Count == 0) return Task.FromResult(new QueueAdditionResult(0, playNext, session.Epoch));
            var existingIds = _order.Entries.Select(entry => entry.EntryId).ToHashSet();
            if (_order.Entries.Count + entries.Count > 10000 || entries.Any(e => existingIds.Contains(e.EntryId)))
                throw new MusicException(MusicError.Storage, "队列超过 10,000 项或包含重复条目标识。");
            var empty = _order.Current is null;
            _order.Add(entries, playNext); PublishOrder();
            if (empty) SetPending(PlaybackState.Stopped);
        }
        Notify(explicitReplacement: true); return Task.FromResult(new QueueAdditionResult(entries.Count, playNext, session.Epoch));
    }
    public Task SelectAsync(Guid entryId, CancellationToken ct)
    {
        var session = Capture(ct); Task work = Task.CompletedTask;
        lock (_sync)
        {
            BindAccount(session);
            if (_order.Select(entryId)) { _failedCandidates.Clear(); PublishOrder(); work = StartCurrent(); }
        }
        Notify(); return work;
    }
    public Task NextAsync(bool previous, CancellationToken ct)
    {
        var session = Capture(ct); Task work = Task.CompletedTask;
        lock (_sync)
        {
            BindAccount(session);
            if (previous ? !_order.CanPrevious : !_order.CanNext) return work;
            var state = _snapshot.Playback.State;
            if ((previous ? _order.Previous() : _order.Next(false)) is not null)
            {
                _failedCandidates.Clear(); PublishOrder();
                work = state is PlaybackState.Playing or PlaybackState.Loading ? StartCurrent() : StopToPending(state == PlaybackState.Paused ? state : PlaybackState.Stopped);
            }
        }
        Notify(); return work;
    }
    public Task RemoveAsync(Guid entryId, CancellationToken ct)
    {
        var session = Capture(ct); Task work = Task.CompletedTask;
        lock (_sync)
        {
            BindAccount(session);
            if (_order.CaptureRemoval(entryId) is not { } removed) return work;
            var current = _order.CurrentId == entryId; var state = _snapshot.Playback.State;
            if (current) _order.Next(false, [entryId]);
            _order.Remove(entryId); _failedCandidates.Remove(entryId); PublishOrder();
            if (current) work = _order.Current is not null && state == PlaybackState.Playing ? StartCurrent() : StopToPending(state == PlaybackState.Paused ? state : PlaybackState.Stopped);
            RememberUndo(entryId, removed.Index, removed, "撤销移除");
        }
        Notify(); return work;
    }
    public void Move(Guid entryId, int direction)
    {
        var snapshot = Snapshot; var index = snapshot.Entries.ToList().FindIndex(e => e.EntryId == entryId);
        if (index >= 0) MoveTo(new(entryId, Math.Clamp(index + Math.Sign(direction), 0, snapshot.Entries.Count - 1), snapshot.QueueRevision, snapshot.AccountEpoch));
    }
    /// <summary>版本、账号、目标身份与边界在同一短锁校验；拖动预览期间没有任何结构写入。</summary>
    public bool MoveTo(QueueMoveIntent intent)
    {
        lock (_sync)
        {
            if (_closed || _accountToken.IsCancellationRequested || intent.AccountEpoch != _snapshot.AccountEpoch || intent.QueueRevision != _snapshot.QueueRevision) return false;
            var index = _order.IndexOf(intent.EntryId);
            if (!_order.MoveTo(intent.EntryId, intent.TargetIndex)) return false;
            PublishOrder(); RememberUndo(intent.EntryId, index, null, "撤销移动");
        }
        Notify(); return true;
    }
    public bool UndoQueueChange(Guid undoId, long accountEpoch)
    {
        lock (_sync)
        {
            if (_closed || _accountToken.IsCancellationRequested || _undo is not { } undo || undo.Info.Id != undoId ||
                undo.Epoch != accountEpoch || accountEpoch != _snapshot.AccountEpoch || undo.Revision != _snapshot.QueueRevision) return false;
            if (undo.Removed is { } removed)
            {
                if (_order.Entries.Count >= 10000 || _order.IndexOf(undo.EntryId) >= 0) return false;
                _order.Reinsert(removed);
            }
            else if (!_order.MoveTo(undo.EntryId, undo.Index)) return false;
            PublishOrder();
        }
        Notify(); return true;
    }
    private void RememberUndo(Guid entryId, int index, QueueNavigator.RemovedEntry? removed, string description)
    {
        var info = new QueueUndoInfo(Guid.NewGuid(), description);
        _undo = new(info, _snapshot.AccountEpoch, _snapshot.QueueRevision, entryId, index, removed);
        _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Undo = info };
    }
    public void SetMode(PlaybackMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        lock (_sync) { if (_closed) return; _order.SetMode(mode); PublishOrder(); }
        Notify();
    }
    public Task ClearAsync() => ClearCore(null);
    public Task ClearIfUnchangedAsync(long expectedQueueRevision) => ClearCore(expectedQueueRevision);
    private Task ClearCore(long? expectedQueueRevision)
    {
        Task work;
        lock (_sync)
        {
            if (expectedQueueRevision is { } expected && expected != _snapshot.QueueRevision)
                throw new MusicException(MusicError.Storage, "队列已经变化，请重新确认。");
            _attempt = Guid.Empty; _terminalConsumed = true; _failedCandidates.Clear(); _order.Replace([], 0);
            PublishOrder(); work = StopToPending(PlaybackState.Stopped);
        }
        Notify(explicitReplacement: true); return work;
    }
    public Task StopAsync()
    {
        Task work;
        lock (_sync) { _failedCandidates.Clear(); work = StopToPending(PlaybackState.Stopped); }
        Notify(); return work;
    }
    public Task PauseAsync(bool paused, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_closed || _order.Current is null) return Task.CompletedTask;
            if (!paused && (_attempt == Guid.Empty || _snapshot.Playback.State is PlaybackState.Stopped or PlaybackState.Ended or PlaybackState.Failed))
            { _failedCandidates.Clear(); return StartCurrent(_resumePosition); }
            return _single.PauseAsync(paused, ct);
        }
    }
    public async Task SetVolumeAsync(int volume, CancellationToken ct)
    {
        await _single.SetVolumeAsync(volume, ct).ConfigureAwait(false);
        lock (_sync)
        {
            if (_closed) return;
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Playback = _snapshot.Playback with { Volume = volume } };
        }
        Notify();
    }
    public Task SeekAsync(Guid entryId, long generation, long positionMs, CancellationToken ct)
    {
        lock (_sync)
        {
            if (_closed || _order.CurrentId != entryId || _snapshot.Playback.Generation != generation) return Task.CompletedTask;
            return _single.SeekAsync(generation, positionMs, ct);
        }
    }
    /// <summary>
    /// 账号核验后的静默恢复。检查完整快照 revision，连读取期间的新音量/停止意图也优先；
    /// 不解析 URL、不打开媒体，保存的起点只在用户点击继续时交给原生适配。
    /// </summary>
    public async Task<bool> RestoreAsync(MusicSession session, PlaybackStateData data, long expectedRevision)
    {
        Task volume;
        lock (_sync)
        {
            if (_closed || !_sessions.IsCurrent(session) || data.AccountId != session.AccountId || _snapshot.Revision != expectedRevision) return false;
            BindAccount(session);
            var entries = data.Entries.Select(entry => entry.ToQueueEntry()).ToArray();
            Validate(entries); var index = Array.FindIndex(entries, entry => entry.EntryId == data.CurrentEntryId);
            _order.Replace(entries, Math.Max(0, index)); _order.SetMode(data.Mode); _attempt = Guid.Empty; _terminalConsumed = true; _failedCandidates.Clear();
            PublishOrder(); SetPending(entries.Length == 0 ? PlaybackState.Stopped : PlaybackState.Paused);
            _resumePosition = data.PositionMs;
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Playback = _snapshot.Playback with { PositionMs = data.PositionMs,
                DurationMs = _order.Current?.Track?.DurationMs ?? 0, Volume = data.Volume, Message = entries.Length == 0 ? "" : "已恢复上次队列，点击继续播放。" } };
            _snapshot = _snapshot with { Restoration = entries.Length == 0 ? null : new(Guid.NewGuid(), entries.Length, data.PositionMs) };
            volume = _single.SetVolumeAsync(data.Volume, CancellationToken.None);
        }
        await volume.ConfigureAwait(false); Notify(save: false); return true;
    }
    private MusicSession Capture(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var session = _sessions.Capture();
        if (session.AccountId <= 0) throw new MusicException(MusicError.SignedOut, "请先核验网易账号。");
        return session;
    }
    private void BindAccount(MusicSession session)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (!_sessions.IsCurrent(session)) throw new OperationCanceledException(session.Revoked);
        if (_snapshot.AccountEpoch == session.Epoch && _snapshot.AccountId == session.AccountId) return;
        // 旧注册使用非阻塞取消登记，避免在状态锁中等待一个同样需要此锁的撤销回调。
        _revocation.Unregister(); _order.Replace([], 0); _attempt = Guid.Empty;
        _snapshot = _snapshot with { AccountId = session.AccountId, AccountEpoch = session.Epoch };
        _accountToken = session.Revoked;
        _revocation = session.Revoked.Register(() => Revoke(session.Epoch));
        session.Revoked.ThrowIfCancellationRequested();
    }
    private void Revoke(long epoch)
    {
        lock (_sync)
        {
            if (_closed || _snapshot.AccountEpoch != epoch) return;
            _attempt = Guid.Empty; _terminalConsumed = true; _failedCandidates.Clear(); _order.Replace([], 0);
            _snapshot = _snapshot with { AccountId = 0, AccountEpoch = 0 }; PublishOrder();
            Track(StopToPending(PlaybackState.Stopped));
        }
        Notify();
    }
    private Task StartCurrent(long startPosition = 0)
    {
        var current = _order.Current;
        if (current is null) return Task.CompletedTask;
        _attempt = Guid.NewGuid(); _terminalConsumed = false;
        _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Restoration = null };
        _resumePosition = startPosition;
        var track = current.Track ?? new MusicTrack(current.TrackId, $"歌曲 {current.TrackId}", "待加载", "", null, 0);
        // PlayAsync 只同步登记代次，执行体首先异步让出，保持本状态锁短暂；旧资源由单曲尾任务先释放。
        return _single.PlayAsync(_attempt, track, _closing.Token, startPosition);
    }
    private Task StopToPending(PlaybackState state)
    {
        _attempt = Guid.Empty; _terminalConsumed = true;
        var work = _single.StopAsync(); SetPending(state); var revision = _snapshot.Revision;
        return CompleteStop();
        async Task CompleteStop()
        {
            await work.ConfigureAwait(false);
            lock (_sync)
            {
                // Stop 本身也可能失败。原尝试已撤销，不能靠其事件回填；只有本次停止仍有效时显式转发错误。
                if (_closed || _snapshot.Revision != revision || _attempt != Guid.Empty || _single.Snapshot.State != PlaybackState.Failed) return;
                _snapshot = _snapshot with { Revision = revision + 1, Playback = _snapshot.Playback with { State = PlaybackState.Failed,
                    Error = _single.Snapshot.Error, Message = _single.Snapshot.Message } };
            }
            Notify();
        }
    }
    private void SetPending(PlaybackState state)
    {
        _resumePosition = 0;
        var current = _order.Current;
        _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Playback = new(_snapshot.Playback.Revision + 1, state,
            current?.Track ?? (current is null ? null : new(current.TrackId, current.Display, "", "", null, 0)), Volume: _snapshot.Playback.Volume) };
    }
    private void PublishOrder(bool metadataOnly = false)
    {
        if (!metadataOnly) _undo = null;
        _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, QueueRevision = _snapshot.QueueRevision + 1,
            Entries = Array.AsReadOnly(_order.Entries.ToArray()), CurrentEntryId = _order.CurrentId, Mode = _order.Mode,
            CanNext = _order.CanNext, CanPrevious = _order.CanPrevious, Restoration = null, Undo = _undo?.Info };
        if (_undo is not null) _undo = _undo with { Revision = _snapshot.QueueRevision };
    }
    private void SingleChanged(object? sender, PlaybackSnapshot playback)
    {
        lock (_sync)
        {
            // 账号取消回调按逆序执行，单曲 Stop 可能先于队列 Revoke。此时不得把归零事实保存成最后续播位置。
            if (_closed || _accountToken.IsCancellationRequested || _attempt == Guid.Empty || playback.AttemptId != _attempt || playback.Revision <= _lastSingleRevision) return;
            _lastSingleRevision = playback.Revision;
            if (playback.State == PlaybackState.Failed && _resumePosition > 0)
                playback = playback with { PositionMs = _resumePosition, Message = playback.Message + " 已保留续播位置，可重试。" };
            if (playback.Track is { } track && _order.Current?.Track != track) { _order.UpdateTrack(track); PublishOrder(metadataOnly: true); }
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Playback = playback };
            if (playback.State == PlaybackState.Playing) { _failedCandidates.Clear(); _resumePosition = 0; }
            if (playback.State is PlaybackState.Ended or PlaybackState.Failed && !_terminalConsumed)
            {
                _terminalConsumed = true;
                Track(Task.Run(() => AdvanceTerminalAsync(playback.AttemptId, playback.State, playback.Error)));
            }
        }
        Notify();
    }
    private async Task AdvanceTerminalAsync(Guid attempt, PlaybackState state, MusicError? error)
    {
        Task next = Task.CompletedTask;
        lock (_sync)
        {
            if (_closed || attempt != _attempt) return;
            if (state == PlaybackState.Failed)
            {
                if (error != MusicError.Unavailable) return;
                if (_order.CurrentId is { } id) _failedCandidates.Add(id);
                if (_failedCandidates.Count >= Math.Min(20, _order.Entries.Count))
                { SetFailureSummary(); }
                else if (_order.Next(false, _failedCandidates) is not null) { PublishOrder(); next = StartCurrent(); }
                else SetFailureSummary();
            }
            else if (_order.Next(true) is not null) { PublishOrder(); next = StartCurrent(); }
        }
        Notify(); await next.ConfigureAwait(false);
    }
    private void SetFailureSummary() => _snapshot = _snapshot with { Revision = _snapshot.Revision + 1,
        Playback = _snapshot.Playback with { State = PlaybackState.Failed, Message = $"已尝试 {_failedCandidates.Count} 个不可播放项，自动播放已停止，请选择其他歌曲。" } };
    private void Track(Task task) => _background = _background.IsCompleted ? task : Task.WhenAll(_background, task);
    private void Notify(bool explicitReplacement = false, bool save = true)
    {
        var snapshot = Snapshot;
        if (save) _persistence?.Observe(snapshot, explicitReplacement);
        if (Changed is not { } handlers) return;
        foreach (EventHandler<PlayerSessionSnapshot> handler in handlers.GetInvocationList())
            try { handler(this, snapshot); } catch (Exception) { /* 页面订阅失败不改变共享播放事实。 */ }
    }
    private static void Validate(IReadOnlyList<QueueEntry> entries)
    {
        if (entries.Count > 10000 || entries.Any(e => e.TrackId <= 0 || e.EntryId == Guid.Empty) || entries.Select(e => e.EntryId).Distinct().Count() != entries.Count)
            throw new ArgumentException("队列需要有效且唯一的条目标识，最多 10,000 项。", nameof(entries));
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_shutdown is not null) return new(_shutdown);
            _closed = true; _attempt = Guid.Empty; _single.Changed -= SingleChanged; _revocation.Unregister();
            _closing.Cancel(); Changed = null;
            _shutdown = ShutdownAsync(); return new(_shutdown);
        }
    }
    private async Task ShutdownAsync()
    {
        await Task.Yield(); await _single.StopAsync().ConfigureAwait(false);
        if (_persistence is not null) await _persistence.FlushAsync().ConfigureAwait(false);
        Task background; lock (_sync) background = _background;
        await background.ConfigureAwait(false); _revocation.Dispose(); _closing.Dispose();
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
