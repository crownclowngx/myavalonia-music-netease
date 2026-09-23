namespace MusicNetEasePlugin.Application.Playback;

/// <summary>
/// 容器内唯一单曲编排者。短状态锁只登记意图，网络/原生/文件工作在锁外按尾任务串行收口。
/// 先淘汰代次再取消，避免旧地址或原生事件在 Stop 和换曲期间让旧歌复活。
/// </summary>
public sealed class PlaybackCoordinator : IAsyncDisposable, IDisposable
{
    private readonly IMusicSessionAccessor _sessions;
    private readonly IMusicCatalogApi _catalog;
    private readonly MediaLoader _loader;
    private readonly IAudioOutput _audio;
    private readonly object _sync = new();
    private long _generation;
    private Guid _owner;
    private CancellationTokenSource? _operation;
    private CancellationTokenRegistration _revocation;
    private CancellationTokenRegistration _accountRevocation;
    private long _accountEpoch;
    private long _accountWatchVersion;
    private Task _tail = Task.CompletedTask;
    private BufferedMedia? _media;
    private bool _disposed;
    private Task? _shutdown;
    private readonly SemaphoreSlim _volumeChanges = new(1);
    private CancellationTokenSource? _seek;
    private long _seekVersion;
    private PlaybackSnapshot _snapshot = new(0, PlaybackState.Idle);
    public PlaybackCoordinator(IMusicSessionAccessor sessions, IMusicCatalogApi catalog, IPlaybackResourceResolver resources,
        IMediaBuffer buffer, IAudioOutput audio, TimeProvider? time = null)
    {
        (_sessions, _catalog, _audio) = (sessions, catalog, audio);
        _loader = new(resources, buffer, time ?? TimeProvider.System);
        audio.Changed += OnAudioChanged;
    }
    public PlaybackSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    public event EventHandler<PlaybackSnapshot>? Changed;

    public Task PlayAsync(Guid owner, MusicTrack track, CancellationToken ct, long startPositionMs = 0)
    {
        MusicSession session;
        try { session = _sessions.Capture(); }
        catch (MusicException ex) { PublishFailure(ex.Message); return Task.CompletedTask; }
        CancellationTokenSource? previousCancellation;
        CancellationTokenRegistration previousRegistration;
        CancellationTokenRegistration previousAccountRegistration;
        CancellationTokenSource operation;
        Task previous;
        long generation;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PlaybackSnapshot snapshot;
        lock (_sync)
        {
            if (_disposed) return Task.CompletedTask;
            previousCancellation = _operation;
            previousRegistration = _revocation;
            _revocation = default;
            previousAccountRegistration = _accountRevocation;
            _accountRevocation = default;
            operation = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Revoked);
            _operation = operation;
            previous = _tail;
            _tail = completion.Task;
            generation = ++_generation;
            _accountEpoch = session.Epoch;
            _accountWatchVersion = generation;
            _owner = owner;
            snapshot = _snapshot = new(_snapshot.Revision + 1, PlaybackState.Loading, track, Volume: _snapshot.Volume,
                Message: "正在加载音频…", Generation: generation, AttemptId: owner);
        }
        // 这里可能位于队列接纳意图的短锁中，不能等待正在回调并发布队列状态的取消注册。
        // 旧回调即使已经开始，也会被 generation/account 校验拒绝；解除登记即可。
        previousRegistration.Unregister();
        previousAccountRegistration.Unregister();
        previousCancellation?.Cancel();
        Notify(snapshot);
        // 注册发生在新代次登记之后；预取消也会安排停止，但必须仍启动本次收口任务以完成尾任务。
        var registration = operation.Token.Register(() => _ = StopIfCurrentAsync(generation, clearTrack: session.Revoked.IsCancellationRequested));
        bool retained;
        lock (_sync) { retained = generation == _generation; if (retained) _revocation = registration; }
        if (!retained) registration.Dispose();
        // 停止/自然结束仍会保留当前歌曲信息；账号监听不能随活动播放一起解绑，否则重登会显示旧账号曲目。
        var accountRegistration = session.Revoked.Register(() =>
            _ = StopCoreAsync(null, null, PlaybackState.Stopped, "", true, session.Epoch));
        lock (_sync)
        {
            retained = !_disposed && _accountWatchVersion == generation;
            if (retained) _accountRevocation = accountRegistration;
        }
        if (!retained) accountRegistration.Dispose();
        _ = LoadAsync(previous, previousCancellation, operation, session, track, generation, completion, startPositionMs);
        return completion.Task;
    }
    private async Task LoadAsync(Task previous, CancellationTokenSource? previousCancellation, CancellationTokenSource operation,
        MusicSession session, MusicTrack track, long generation, TaskCompletionSource completion, long startPositionMs)
    {
        // 把执行让到登记意图之后，队列可在短锁内完成播放交接，不让网络和原生操作进入外层状态锁。
        await Task.Yield();
        try
        {
            await previous.ConfigureAwait(false);
            previousCancellation?.Dispose();
            await ReleaseMediaAsync().ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            var detail = await _catalog.DetailAsync(track.Id, session, operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            var loaded = await _loader.LoadAsync(track.Id, session, operation.Token,
                p => Update(generation, s => s with { Buffer = p }), message => Update(generation, s => s with { Message = message, Buffer = null })).ConfigureAwait(false);
            _media = loaded.Media; var resource = loaded.Resource;
            operation.Token.ThrowIfCancellationRequested();
            if (!_sessions.IsCurrent(session)) throw new OperationCanceledException(operation.Token);
            var duration = resource.TrialDurationMs ?? detail.DurationMs;
            var start = Math.Max(0, startPositionMs);
            if (duration > 0 && start >= duration) start = 0;
            Update(generation, s => s with { Track = detail, IsTrial = resource.IsTrial, DurationMs = duration, TrialDurationMs = resource.TrialDurationMs });
            await ApplyCurrentVolumeAsync(operation.Token).ConfigureAwait(false);
            await _audio.OpenAsync(_media.Path, generation, operation.Token, start).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            if (start != startPositionMs) Update(generation, s => s with { Message = "续播位置超出当前可播范围，已从开头播放。" });
        }
        catch (OperationCanceledException) { await ReleaseMediaSafelyAsync().ConfigureAwait(false); }
        catch (Exception ex)
        {
            await ReleaseMediaSafelyAsync().ConfigureAwait(false);
            Update(generation, s => s with { State = PlaybackState.Failed, PositionMs = 0, Error = ex is MusicException music ? music.Kind : MusicError.Device,
                Message = ex is MusicException ? ex.Message : "播放失败，请重试。" });
        }
        finally { completion.TrySetResult(); }
    }
    public Task StopAsync(Guid? owner = null) => StopCoreAsync(owner, null, PlaybackState.Stopped, "", false);
    private Task StopIfCurrentAsync(long generation, bool clearTrack) => StopCoreAsync(null, generation, PlaybackState.Stopped, "", clearTrack);
    private Task StopCoreAsync(Guid? owner, long? expected, PlaybackState state, string message, bool clearTrack, long? expectedAccount = null)
    {
        Task previous;
        long stoppedGeneration;
        CancellationTokenSource? cancellation;
        CancellationTokenRegistration registration;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PlaybackSnapshot snapshot;
        lock (_sync)
        {
            if (owner.HasValue && owner.Value != _owner || expected.HasValue && expected.Value != _generation ||
                expectedAccount.HasValue && expectedAccount.Value != _accountEpoch) return Task.CompletedTask;
            stoppedGeneration = ++_generation;
            cancellation = _operation;
            _operation = null;
            registration = _revocation;
            _revocation = default;
            previous = _tail;
            _tail = completion.Task;
            snapshot = _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, State = state,
                PositionMs = state == PlaybackState.Ended ? _snapshot.DurationMs : 0, CanSeek = false, IsSeeking = false, Buffer = null,
                Error = state == PlaybackState.Failed ? MusicError.Device : null,
                Track = clearTrack ? null : _snapshot.Track, DurationMs = clearTrack ? 0 : _snapshot.DurationMs,
                IsTrial = clearTrack ? false : _snapshot.IsTrial, Message = message };
        }
        cancellation?.Cancel();
        Notify(snapshot);
        _ = CompleteStopAsync();
        return completion.Task;
        async Task CompleteStopAsync()
        {
            try
            {
                await previous.ConfigureAwait(false);
                try { await ReleaseMediaAsync().ConfigureAwait(false); }
                catch (Exception)
                {
                    Update(stoppedGeneration, s => s with { State = PlaybackState.Failed,
                        Message = "音频停止未完成，请重试停止；临时文件将在释放句柄后清理。" });
                }
                // 取消回调可能正在调度本方法；异步尾任务结束后再 Dispose 注册，避免在自身回调中互等。
                registration.Dispose();
                cancellation?.Dispose();
            }
            finally { completion.TrySetResult(); }
        }
    }
    public async Task PauseAsync(bool paused, CancellationToken ct)
    {
        long generation;
        CancellationToken operation;
        lock (_sync)
        {
            if (_snapshot.State is not (PlaybackState.Playing or PlaybackState.Paused)) return;
            generation = _generation;
            operation = _operation?.Token ?? new CancellationToken(true);
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, operation);
        try
        {
            await _audio.PauseAsync(paused, linked.Token, generation).ConfigureAwait(false);
            Update(generation, s => s with { State = paused ? PlaybackState.Paused : PlaybackState.Playing });
        }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { await StopCoreAsync(null, generation, PlaybackState.Failed, ex.Message, false).ConfigureAwait(false); }
    }
    /// <summary>只修改已确认的当前媒体；用户目标不写入事实位置。后来的定位取消旧等待，换曲也通过操作令牌撤销。</summary>
    public async Task SeekAsync(long generation, long positionMs, CancellationToken ct)
    {
        CancellationTokenSource seek; long version; long target; CancellationTokenSource? old;
        lock (_sync)
        {
            if (_disposed || generation != _generation || !_snapshot.CanSeek || _snapshot.DurationMs <= 0 || _snapshot.State is not (PlaybackState.Playing or PlaybackState.Paused)) return;
            old = _seek; seek = CancellationTokenSource.CreateLinkedTokenSource(ct, _operation?.Token ?? new(true)); _seek = seek;
            version = ++_seekVersion; target = Math.Clamp(positionMs, 0, Math.Max(0, _snapshot.DurationMs - 1));
        }
        try { old?.Cancel(); } catch (ObjectDisposedException) { }
        Update(generation, s => s with { IsSeeking = true, SeekRevision = s.SeekRevision + 1, Message = s.IsTrial ? "正在播放试听片段" : "" });
        try { await _audio.SeekAsync(generation, target, seek.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { Update(generation, s => s with { Message = ex.Message }); }
        finally
        {
            lock (_sync) if (ReferenceEquals(_seek, seek)) _seek = null;
            if (version == Interlocked.Read(ref _seekVersion)) Update(generation, s => s with { IsSeeking = false });
            seek.Dispose();
        }
    }
    public async Task SetVolumeAsync(int volume, CancellationToken ct)
    {
        if (volume is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(volume));
        await _volumeChanges.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_sync) { if (_disposed) return; }
            await _audio.SetVolumeAsync(volume, ct).ConfigureAwait(false);
            PlaybackSnapshot snapshot;
            lock (_sync) snapshot = _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Volume = volume };
            Notify(snapshot);
        }
        finally { _volumeChanges.Release(); }
    }
    private async Task ApplyCurrentVolumeAsync(CancellationToken ct)
    {
        // 新歌继承音量与用户改音量走同一条顺序，防止加载过程把刚调整的值回滚。
        await _volumeChanges.WaitAsync(ct).ConfigureAwait(false);
        try { await _audio.SetVolumeAsync(Snapshot.Volume, ct).ConfigureAwait(false); }
        finally { _volumeChanges.Release(); }
    }
    private void OnAudioChanged(object? sender, AudioProgress progress)
    {
        if (progress.State is PlaybackState.Ended or PlaybackState.Failed)
        {
            // 只排入异步收口；不能在原生事件线程同步 Stop，后者可能等待本事件返回。
            _ = Task.Run(() => StopCoreAsync(null, progress.Generation, progress.State, progress.Error ?? "", false));
            return;
        }
        Update(progress.Generation, s => s with { State = progress.State, PositionMs = Math.Max(0, progress.PositionMs),
            DurationMs = progress.DurationMs > 0 ? s.TrialDurationMs is { } limit ? Math.Min(progress.DurationMs, limit) : progress.DurationMs : s.DurationMs,
            CanSeek = progress.CanSeek,
            Message = progress.StartPositionReset ? "续播位置超出当前媒体长度，已从开头播放。" : s.State == PlaybackState.Loading ? s.IsTrial ? "正在播放试听片段" : "" : s.Message });
    }
    private async Task ReleaseMediaAsync()
    {
        await _audio.StopAsync().ConfigureAwait(false);
        _media?.Dispose();
        _media = null;
    }
    private async Task ReleaseMediaSafelyAsync()
    {
        try { await ReleaseMediaAsync().ConfigureAwait(false); }
        catch (Exception) { /* 保留仍可能被句柄占用的文件，不能因 Stop 失败强行删除；下一次安全清理重试。 */ }
    }
    private void Update(long generation, Func<PlaybackSnapshot, PlaybackSnapshot> change)
    {
        PlaybackSnapshot snapshot;
        lock (_sync)
        {
            if (_disposed || generation != _generation) return;
            snapshot = _snapshot = change(_snapshot) with { Revision = _snapshot.Revision + 1 };
        }
        Notify(snapshot);
    }
    private void PublishFailure(string message)
    {
        PlaybackSnapshot snapshot;
        lock (_sync) snapshot = _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, State = PlaybackState.Failed, Message = message };
        Notify(snapshot);
    }
    private void Notify(PlaybackSnapshot snapshot)
    {
        if (Changed is not { } handlers) return;
        foreach (EventHandler<PlaybackSnapshot> handler in handlers.GetInvocationList())
            try { handler(this, snapshot); } catch (Exception) { }
    }
    public ValueTask DisposeAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration accountRegistration;
        lock (_sync)
        {
            if (_shutdown is not null) return new(_shutdown);
            _disposed = true;
            _shutdown = completion.Task;
            accountRegistration = _accountRevocation;
            _accountRevocation = default;
        }
        accountRegistration.Dispose();
        _audio.Changed -= OnAudioChanged;
        _ = ShutdownAsync();
        return new(completion.Task);
        async Task ShutdownAsync()
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
                // 已经进入音量门的控制也必须结束，随后容器才能安全销毁原生适配。
                await _volumeChanges.WaitAsync().ConfigureAwait(false);
                _volumeChanges.Release();
            }
            finally { Changed = null; completion.TrySetResult(); }
        }
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
