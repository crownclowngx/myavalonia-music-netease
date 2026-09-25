namespace MusicNetEasePlugin.Application.Playback;

/// <summary>
/// 只接受值快照的存储协调，不反向持有播放器。短锁维护资格，IO 尾任务串行；退出最终清理排在旧写入之后。
/// 队列变化合并 500 ms，正常位置每 5 秒落盘，暂停/进程关闭 flush。损坏文件只由明确新建/清空意图解除保护。
/// </summary>
public sealed class PlaybackPersistence(IPlaybackStateStore store, TimeProvider time) : IAsyncDisposable, IDisposable
{
    private readonly object _sync = new();
    private PlaybackStateData _state = PlaybackStateData.Empty(0);
    private PlayerSessionSnapshot? _latest;
    private PlaybackStorageSnapshot _snapshot = new(0, 0, Array.Empty<RecentTrack>());
    private long _epoch, _generation, _mutation;
    private bool _loaded, _protected, _explicit, _dirty, _stopping;
    private Guid _recordedAttempt;
    private DateTimeOffset _lastPositionSave;
    private CancellationTokenSource? _delay;
    private Task _delayWork = Task.CompletedTask;
    private Task _tail = Task.CompletedTask;
    private Task? _shutdown;
    // 清理失败可能跨多个账号积累；按账号保留，不能让后一次退出覆盖前一次待清理事实。
    private readonly Dictionary<long, PlaybackStateData> _cleanup = new();
    public PlaybackStorageSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    public event EventHandler<PlaybackStorageSnapshot>? Changed;
    public Task Pending { get { lock (_sync) return Task.WhenAll(_tail, _delayWork); } }

    public Task<PlaybackStateData?> ActivateAsync(MusicSession session, CancellationToken ct)
    {
        long generation;
        lock (_sync)
        {
            if (_stopping) return Task.FromResult<PlaybackStateData?>(null);
            CancelDelay(); generation = ++_generation; _epoch = session.Epoch; _loaded = _protected = _explicit = _dirty = false;
            _state = PlaybackStateData.Empty(session.AccountId); _latest = null; _recordedAttempt = Guid.Empty; _mutation = 0;
            _lastPositionSave = time.GetUtcNow(); Publish("正在读取本地播放记录…");
            return Enqueue(async () =>
            {
                try
                {
                    var read = await store.LoadAsync(session.AccountId, ct).ConfigureAwait(false); ct.ThrowIfCancellationRequested();
                    lock (_sync)
                    {
                        if (_stopping || generation != _generation || session.Revoked.IsCancellationRequested) return null;
                        if (_cleanup.ContainsKey(session.AccountId) && read.Data is { } pending)
                            read = read with { Data = pending with { Entries = Array.Empty<StoredEntry>(), CurrentEntryId = null, PositionMs = 0 } };
                        var live = _state;
                        _state = read.Data ?? PlaybackStateData.Empty(session.AccountId); _loaded = true; _protected = read.ProtectedFile && !_explicit;
                        // 用户在读取期间已经开始播放时保留新状态，只补回该账号历史；队列恢复资格另由 QueueRevision 判定。
                        if (_latest is not null)
                        {
                            var recent = live.Recent.Concat(_state.Recent).DistinctBy(track => track.TrackId).Take(200).ToArray();
                            _state = live with { Revision = _state.Revision, Recent = recent }; _dirty = true; Schedule(TimeSpan.FromMilliseconds(500));
                        }
                        Publish(_protected ? read.Error : "");
                    }
                    Notify(); return read.Data;
                }
                catch (OperationCanceledException) { return null; }
                catch (Exception)
                {
                    lock (_sync) if (generation == _generation) { _loaded = true; _protected = true; Publish("本地恢复读取失败，原文件保持不变。"); }
                    Notify(); return null;
                }
            });
        }
    }

    public void Observe(PlayerSessionSnapshot player, bool explicitReplacement = false)
    {
        lock (_sync)
        {
            // 撤销后的空队列不是用户清空；忽略它才能保留会话失效及关闭前的恢复位置。
            if (_stopping || player.AccountId <= 0 || player.AccountId != _state.AccountId || player.AccountEpoch != _epoch) return;
            if (_latest is { } last && player.Revision <= last.Revision && !explicitReplacement) return;
            if (explicitReplacement) { _explicit = true; _protected = false; _cleanup.Remove(player.AccountId); }
            var previous = _latest; _latest = player; _mutation++;
            var queueChanged = previous is null || previous.QueueRevision != player.QueueRevision;
            var recent = _state.Recent;
            var p = player.Playback;
            if (p.State == PlaybackState.Playing && p.AttemptId != Guid.Empty && p.AttemptId != _recordedAttempt && p.Track is { } track &&
                previous?.Playback is { State: PlaybackState.Playing, IsSeeking: false } old && old.AttemptId == p.AttemptId &&
                !p.IsSeeking && old.SeekRevision == p.SeekRevision && p.PositionMs > old.PositionMs && p.PositionMs - old.PositionMs <= 5000)
            {
                _recordedAttempt = p.AttemptId;
                var entry = new RecentTrack(track.Id, StoredEntry.Short(track.Name, 256), StoredEntry.Short(track.Artists, 128), StoredEntry.Short(track.Album, 256), Math.Clamp(track.DurationMs, 0, 86400000), time.GetUtcNow());
                recent = new[] { entry }.Concat(recent.Where(item => item.TrackId != track.Id)).Take(200).ToArray();
            }
            var historyChanged = !ReferenceEquals(recent, _state.Recent);
            _state = _state with { Entries = queueChanged ? player.Entries.Select(StoredEntry.From).ToArray() : _state.Entries,
                CurrentEntryId = player.CurrentEntryId, Mode = player.Mode, Volume = p.Volume, PositionMs = Math.Clamp(p.PositionMs, 0, 86400000), Recent = recent };
            var structural = explicitReplacement || queueChanged || historyChanged || previous?.Playback.Volume != p.Volume || previous?.Playback.State != p.State;
            var positionDue = p.State == PlaybackState.Playing && time.GetUtcNow() - _lastPositionSave >= TimeSpan.FromSeconds(5);
            if (structural || positionDue)
            {
                _dirty = true;
                if (positionDue) _lastPositionSave = time.GetUtcNow();
                Schedule(p.State == PlaybackState.Paused || positionDue ? TimeSpan.Zero : TimeSpan.FromMilliseconds(500));
            }
            if (historyChanged || explicitReplacement) Publish(_protected ? _snapshot.Message : "");
        }
        Notify();
    }
    public Task FlushAsync()
    {
        lock (_sync) { CancelDelay(); return SaveCurrent(_generation, CancellationToken.None, force: true); }
    }
    public Task RetryAsync()
    {
        lock (_sync) return _cleanup.Count != 0 ? Task.WhenAll(_cleanup.Values.Select(data => SaveFinal(data, true)).ToArray()) : FlushAsync();
    }
    public Task EndAccountAsync(bool clearResume)
    {
        lock (_sync)
        {
            CancelDelay(); var state = _state; var loaded = _loaded; var protect = _protected;
            ++_generation; _epoch = 0; _state = PlaybackStateData.Empty(0); _latest = null; _loaded = false; _dirty = false;
            _snapshot = new(_snapshot.Revision + 1, 0, Array.Empty<RecentTrack>()); Notify();
            if (state.AccountId <= 0) return clearResume && _cleanup.Count != 0 ? RetryAsync() : Task.CompletedTask;
            // 尚未完成的读取也在同一尾任务中，最终清理必须重新读历史，不能用空内存覆盖它。
            return Enqueue(async () =>
            {
                if (clearResume)
                {
                    var disk = await ReadForFinal(state.AccountId).ConfigureAwait(false);
                    if (disk.ProtectedFile) { CleanupFailed(state); return false; }
                    var recent = state.Recent.Concat(disk.Data?.Recent ?? []).DistinctBy(track => track.TrackId).Take(200).ToArray();
                    var cleared = state with { Entries = Array.Empty<StoredEntry>(), CurrentEntryId = null, PositionMs = 0, Recent = recent,
                        Revision = Math.Max(state.Revision, disk.Data?.Revision ?? 0) + 1 };
                    await WriteFinal(cleared, true).ConfigureAwait(false);
                }
                else if (loaded && !protect) await WriteFinal(state with { Revision = state.Revision + 1 }, false).ConfigureAwait(false);
                return true;
            });
        }
    }
    private void Schedule(TimeSpan delay)
    {
        CancelDelay(); var source = new CancellationTokenSource(); _delay = source; var generation = _generation;
        var next = WaitAndSave(); _delayWork = _delayWork.IsCompleted ? next : Task.WhenAll(_delayWork, next);
        async Task WaitAndSave()
        {
            try { await Task.Delay(delay, time, source.Token).ConfigureAwait(false); await SaveCurrent(generation, source.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            finally { lock (_sync) if (ReferenceEquals(_delay, source)) _delay = null; source.Dispose(); }
        }
    }
    private Task SaveCurrent(long generation, CancellationToken ct, bool force = false)
    {
        lock (_sync)
        {
            if (generation != _generation || !_loaded || _protected || _state.AccountId <= 0 || !force && !_dirty) return Task.CompletedTask;
            var data = _state = _state with { Revision = _state.Revision + 1 }; var mutation = _mutation;
            return Enqueue(async () =>
            {
                lock (_sync) if (generation != _generation) return false;
                try
                {
                    await store.SaveAsync(data, ct).ConfigureAwait(false);
                    lock (_sync) if (generation == _generation) { if (mutation == _mutation) _dirty = false; Publish(""); }
                }
                catch (OperationCanceledException) { }
                catch (Exception) { lock (_sync) if (generation == _generation) { _dirty = true; Publish("本地恢复未保存，请重试保存。"); } }
                Notify(); return true;
            });
        }
    }
    private Task SaveFinal(PlaybackStateData data, bool clear)
    {
        // 清理失败后只能重试清理，不能误把原来的完整队列写回去。
        return Enqueue(async () =>
        {
            lock (_sync) if (!_cleanup.TryGetValue(data.AccountId, out var pending) || !ReferenceEquals(pending, data)) return false;
            var disk = await ReadForFinal(data.AccountId).ConfigureAwait(false);
            if (disk.ProtectedFile) { CleanupFailed(data); return false; }
            var value = data with { Revision = Math.Max(data.Revision, disk.Data?.Revision ?? 0) + 1,
                Entries = Array.Empty<StoredEntry>(), CurrentEntryId = null, PositionMs = 0,
                Recent = data.Recent.Concat(disk.Data?.Recent ?? []).DistinctBy(track => track.TrackId).Take(200).ToArray() };
            await WriteFinal(value, clear).ConfigureAwait(false); return true;
        });
    }
    private async Task WriteFinal(PlaybackStateData state, bool clear)
    {
        try
        {
            await store.SaveAsync(state, CancellationToken.None).ConfigureAwait(false);
            lock (_sync) if (clear) { _cleanup.Remove(state.AccountId); Publish(""); }
        }
        catch (Exception)
        {
            if (clear) CleanupFailed(state);
            else lock (_sync) _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Message = "本地恢复未保存，请重试保存。" };
        }
        Notify();
    }
    private void CleanupFailed(PlaybackStateData state)
    {
        lock (_sync) { _cleanup[state.AccountId] = state with { Entries = Array.Empty<StoredEntry>(), CurrentEntryId = null, PositionMs = 0 }; Publish(""); }
        Notify();
    }
    private async Task<PlaybackStateRead> ReadForFinal(long id)
    {
        try { return await store.LoadAsync(id, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception) { return new(null, "本地清理读取失败。", true); }
    }
    private Task<T> Enqueue<T>(Func<Task<T>> action)
    {
        // 调用点已在短锁内；先发布尾任务再让执行体离开锁，保证激活、保存、最终清理的登记顺序。
        // Document Scope 在 UI 线程同步释放；这里不能用捕获 UI 上下文的 Task.Yield。
        var previous = _tail; var next = Task.Run(Run); _tail = next; return next;
        async Task<T> Run() { try { await previous.ConfigureAwait(false); } catch (Exception) { } return await action().ConfigureAwait(false); }
    }
    private void CancelDelay() { try { _delay?.Cancel(); } catch (ObjectDisposedException) { } }
    private void Publish(string message) => _snapshot = new(_snapshot.Revision + 1, _state.AccountId, _state.Recent,
        _cleanup.Count == 0 ? message : "退出后的本地续播清理失败，请重试清理。" + message, _cleanup.Count != 0);
    private void Notify()
    {
        var snapshot = Snapshot;
        if (Changed is { } handlers) foreach (EventHandler<PlaybackStorageSnapshot> handler in handlers.GetInvocationList())
            try { handler(this, snapshot); } catch (Exception) { /* 存储事实不受页面异常影响。 */ }
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_shutdown is not null) return new(_shutdown);
            _stopping = true; CancelDelay(); return new(_shutdown = Task.Run(Finish));
        }
        async Task Finish() { await FlushAsync().ConfigureAwait(false); await Pending.ConfigureAwait(false); Changed = null; }
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
