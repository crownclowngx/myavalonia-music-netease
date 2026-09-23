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
    private readonly object _sync = new();
    private readonly CancellationTokenSource _closing = new();
    private CancellationTokenRegistration _revocation;
    private Guid _attempt;
    private bool _terminalConsumed;
    private bool _closed;
    private readonly HashSet<Guid> _failedCandidates = [];
    private PlayerSessionSnapshot _snapshot = PlayerSessionSnapshot.Empty;
    private long _lastSingleRevision = -1;
    private Task _background = Task.CompletedTask;
    private Task? _shutdown;
    public PlaybackQueueCoordinator(IMusicSessionAccessor sessions, PlaybackCoordinator single)
        : this(sessions, single, new QueueNavigator()) { }
    internal PlaybackQueueCoordinator(IMusicSessionAccessor sessions, PlaybackCoordinator single, QueueNavigator order)
    {
        (_sessions, _single, _order) = (sessions, single, order);
        _single.Changed += SingleChanged;
    }
    public PlayerSessionSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    public event EventHandler<PlayerSessionSnapshot>? Changed;
    public Task PlaySingleAsync(MusicTrack track, CancellationToken ct) => ReplaceAsync([QueueEntry.FromTrack(track)], 0, ct);
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
        Notify(); return work;
    }
    public Task EnqueueAsync(IReadOnlyList<QueueEntry> entries, bool playNext, CancellationToken ct)
    {
        Validate(entries); var session = Capture(ct);
        lock (_sync)
        {
            BindAccount(session);
            if (_order.Entries.Count + entries.Count > 10000 || entries.Any(e => _order.Entries.Any(existing => existing.EntryId == e.EntryId)))
                throw new MusicException(MusicError.Storage, "队列超过 10,000 项或包含重复条目标识。");
            var empty = _order.Current is null;
            _order.Add(entries, playNext); PublishOrder();
            if (empty) SetPending(PlaybackState.Stopped);
        }
        Notify(); return Task.CompletedTask;
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
            var current = _order.CurrentId == entryId; var state = _snapshot.Playback.State;
            if (current) _order.Next(false, [entryId]);
            _order.Remove(entryId); _failedCandidates.Remove(entryId); PublishOrder();
            if (current) work = _order.Current is not null && state == PlaybackState.Playing ? StartCurrent() : StopToPending(state == PlaybackState.Paused ? state : PlaybackState.Stopped);
        }
        Notify(); return work;
    }
    public void Move(Guid entryId, int direction)
    {
        lock (_sync) { if (_closed) return; _order.Move(entryId, direction); PublishOrder(); }
        Notify();
    }
    public void SetMode(PlaybackMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        lock (_sync) { if (_closed) return; _order.SetMode(mode); PublishOrder(); }
        Notify();
    }
    public Task ClearAsync()
    {
        Task work;
        lock (_sync)
        {
            _attempt = Guid.Empty; _terminalConsumed = true; _failedCandidates.Clear(); _order.Replace([], 0);
            PublishOrder(); work = StopToPending(PlaybackState.Stopped);
        }
        Notify(); return work;
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
            { _failedCandidates.Clear(); return StartCurrent(); }
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
    private Task StartCurrent()
    {
        var current = _order.Current;
        if (current is null) return Task.CompletedTask;
        _attempt = Guid.NewGuid(); _terminalConsumed = false;
        var track = current.Track ?? new MusicTrack(current.TrackId, $"歌曲 {current.TrackId}", "待加载", "", null, 0);
        // PlayAsync 只同步登记代次，执行体首先异步让出，保持本状态锁短暂；旧资源由单曲尾任务先释放。
        return _single.PlayAsync(_attempt, track, _closing.Token);
    }
    private Task StopToPending(PlaybackState state)
    {
        _attempt = Guid.Empty; _terminalConsumed = true;
        var work = _single.StopAsync(); SetPending(state); return work;
    }
    private void SetPending(PlaybackState state)
    {
        var current = _order.Current;
        _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Playback = new(_snapshot.Playback.Revision + 1, state,
            current?.Track ?? (current is null ? null : new(current.TrackId, current.Display, "", "", null, 0)), Volume: _snapshot.Playback.Volume) };
    }
    private void PublishOrder()
    {
        _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, QueueRevision = _snapshot.QueueRevision + 1,
            Entries = Array.AsReadOnly(_order.Entries.ToArray()), CurrentEntryId = _order.CurrentId, Mode = _order.Mode,
            CanNext = _order.CanNext, CanPrevious = _order.CanPrevious };
    }
    private void SingleChanged(object? sender, PlaybackSnapshot playback)
    {
        lock (_sync)
        {
            if (_closed || _attempt == Guid.Empty || playback.AttemptId != _attempt || playback.Revision <= _lastSingleRevision) return;
            _lastSingleRevision = playback.Revision;
            if (playback.Track is { } track && _order.Current?.Track != track) { _order.UpdateTrack(track); PublishOrder(); }
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Playback = playback };
            if (playback.State == PlaybackState.Playing) _failedCandidates.Clear();
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
    private void Notify()
    {
        var snapshot = Snapshot;
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
        Task background; lock (_sync) background = _background;
        await background.ConfigureAwait(false); _revocation.Dispose(); _closing.Dispose();
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
