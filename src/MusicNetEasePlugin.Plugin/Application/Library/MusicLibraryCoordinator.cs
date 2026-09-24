using System.Collections.Immutable;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Application.Library;

/// <summary>
/// 插件容器内的音乐库唯一写入者。设计思路：低频编辑用一个账号级异步操作所有权即可，
/// 不排队、不重放；网络在锁外执行。账号代次保护远端结果，Revision 保护同账号迟到读取。
/// UI、凭据持久化和播放由各自服务负责，本类只发布不可变的业务事实与失效通知。
/// </summary>
public sealed class MusicLibraryCoordinator : IAsyncDisposable, IDisposable
{
    private readonly IMusicSessionAccessor _sessions;
    private readonly ILikedSongsApi _likes;
    private readonly ILibraryPlaylistQuery _playlists;
    private readonly IPlaylistMutationApi _writes;
    private readonly TimeProvider _time;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenRegistration _account;
    private MusicLibrarySnapshot _snapshot = new(0, 0, 0);
    private Operation? _active;
    private readonly Dictionary<string, Operation> _pending = new();
    private readonly HashSet<Task> _tasks = new();
    private Task? _refresh;
    private long _readGeneration;
    private bool _closed;
    private Task? _dispose;
    public MusicLibraryCoordinator(IMusicSessionAccessor sessions, ILikedSongsApi likes, ILibraryPlaylistQuery playlists,
        IPlaylistMutationApi writes, TimeProvider? time = null) =>
        (_sessions, _likes, _playlists, _writes, _time) = (sessions, likes, playlists, writes, time ?? TimeProvider.System);

    public event EventHandler<LibraryChanged>? Changed;
    public MusicLibrarySnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    internal int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;
    internal int PendingTasks { get { lock (_sync) return _tasks.Count; } }
    public MusicSession CaptureSession() => EnsureSession();
    public bool IsCurrent(long accountId, long epoch)
    {
        try { var session = _sessions.Capture(); return session.AccountId == accountId && session.Epoch == epoch && _sessions.IsCurrent(session); }
        catch (MusicException) { return false; }
    }
    private MusicSession EnsureSession()
    {
        var session = _sessions.Capture();
        if (session.AccountId <= 0 || !_sessions.IsCurrent(session)) throw new MusicException(MusicError.SignedOut, "请先登录网易云音乐。");
        CancellationTokenRegistration old = default;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_snapshot.Epoch == session.Epoch && _snapshot.AccountId == session.AccountId) return session;
            old = _account; _account = default;
            _active = null; _pending.Clear(); _refresh = null; _readGeneration++;
            _snapshot = new(session.AccountId, session.Epoch, _snapshot.Revision + 1);
        }
        old.Unregister();
        var registration = session.Revoked.Register(() => Revoke(session));
        lock (_sync)
        {
            if (Matches(session)) _account = registration; else registration.Unregister();
        }
        Publish();
        return session;
    }
    private bool Matches(MusicSession session) => !_closed && _snapshot.AccountId == session.AccountId &&
        _snapshot.Epoch == session.Epoch && !session.Revoked.IsCancellationRequested;
    private void Revoke(MusicSession session)
    {
        lock (_sync)
        {
            if (_snapshot.Epoch != session.Epoch || _snapshot.AccountId != session.AccountId) return;
            _active = null; _pending.Clear(); _refresh = null; _readGeneration++;
            _snapshot = new(0, 0, _snapshot.Revision + 1, Message: "请先登录网易云音乐。");
        }
        Publish();
    }
    private void Publish(long? playlistId = null, bool directory = false, bool deleted = false, bool likes = false)
    {
        var snapshot = Snapshot;
        // 一个已关闭页面的回调异常不能阻止其他页面更新，更不能让写入完成任务永久悬挂。
        foreach (EventHandler<LibraryChanged> subscriber in Changed?.GetInvocationList() ?? [])
        {
            try { subscriber(this, new(snapshot, playlistId, directory, deleted, likes)); }
            catch (Exception) { System.Diagnostics.Trace.WriteLine("音乐库视图通知失败；业务结果仍已保存。"); }
        }
    }

    /// <summary>所有歌曲行共用一次全量读取；写入期间的新旧读取均不能覆盖协调器自己的写后确认。</summary>
    public Task RefreshLikesAsync(CancellationToken ct = default)
    {
        MusicSession session;
        try { session = EnsureSession(); }
        catch (MusicException) { return Task.CompletedTask; }
        TaskCompletionSource completion; long generation; long revision;
        lock (_sync)
        {
            if (_refresh is { IsCompleted: false }) return _refresh.WaitAsync(ct);
            if (_active is not null) return Task.CompletedTask;
            generation = ++_readGeneration; revision = _snapshot.Revision;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _refresh = completion.Task; _tasks.Add(completion.Task);
        }
        _ = Read(); return completion.Task.WaitAsync(ct);
        async Task Read()
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(session.Revoked, _shutdown.Token);
            try
            {
                var likes = await _likes.ReadAsync(session, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
                lock (_sync)
                {
                    if (!Matches(session) || generation != _readGeneration || revision != _snapshot.Revision || _active is not null) return;
                    _snapshot = _snapshot with { Likes = likes, Revision = _snapshot.Revision + 1, Message = $"已读取 {likes.Count} 首喜欢的歌曲。" };
                }
                Publish(likes: true);
            }
            catch (Exception ex) when (ex is LibraryReadException or MusicException or OperationCanceledException)
            {
                lock (_sync)
                {
                    if (!Matches(session) || generation != _readGeneration || revision != _snapshot.Revision) return;
                    _snapshot = _snapshot with { Message = "喜欢状态读取失败，可重新读取。", Revision = _snapshot.Revision + 1 };
                }
                Publish();
            }
            finally
            {
                lock (_sync) { _tasks.Remove(completion.Task); if (ReferenceEquals(_refresh, completion.Task)) _refresh = null; }
                completion.TrySetResult();
            }
        }
    }

    public async Task<LibraryPlaylist> ReadPlaylistAsync(long id, CancellationToken ct = default)
    {
        if (id <= 0) throw new ArgumentException("歌单 ID 无效。");
        var session = EnsureSession();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Revoked, _shutdown.Token);
        var result = await _playlists.ReadAsync(id, session, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
        linked.Token.ThrowIfCancellationRequested();
        if (!_sessions.IsCurrent(session)) throw new OperationCanceledException(linked.Token);
        return result;
    }
    public Task<LibraryMutationResult> SetLikedAsync(long trackId, bool desiredValue, CancellationToken ct = default) =>
        ExecuteAsync(new(LibraryOperationKind.Like, trackId, desiredValue), ct);

    /// <summary>不等待旧操作释放后再偷偷提交。调用者取消不等于远端撤销；页面接受后应只传账号级取消资格。</summary>
    public Task<LibraryMutationResult> ExecuteAsync(LibraryIntent intent, CancellationToken ct = default)
    {
        try { intent = LibraryEditRules.Normalize(intent); ct.ThrowIfCancellationRequested(); }
        catch (ArgumentException ex) { return Task.FromResult(Result(LibraryOutcome.NotSent, ex.Message)); }
        catch (OperationCanceledException) { return Task.FromResult(Result(LibraryOutcome.NotSent, "操作尚未发送，已取消。")); }
        return Begin(intent, null, ct);
    }
    public Task<LibraryMutationResult> RecheckAsync(string resourceKey, CancellationToken ct = default)
    {
        Operation? pending;
        lock (_sync) _pending.TryGetValue(resourceKey, out pending);
        return pending is null ? Task.FromResult(Result(LibraryOutcome.NotSent, "没有待核实的操作。")) : Begin(pending.Intent, pending, ct);
    }
    /// <summary>创建丢失 ID 无法通过同名自动认领；只有用户已核对目录后，才允许结束提示并主动发起另一笔创建。</summary>
    public void AcknowledgeUncertainCreate(long accountId, long epoch)
    {
        if (!IsCurrent(accountId, epoch)) return;
        lock (_sync)
        {
            if (_active is not null) return;
            _pending.Remove("create"); UpdatePending();
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Message = "已结束创建结果提示；再次创建将作为新的操作。" };
        }
        Publish();
    }
    private Task<LibraryMutationResult> Begin(LibraryIntent intent, Operation? pending, CancellationToken ct)
    {
        MusicSession session;
        try { session = EnsureSession(); }
        catch (Exception ex) when (ex is MusicException or ObjectDisposedException) { return Task.FromResult(Result(LibraryOutcome.StaleSession, "账号或插件已关闭。")); }
        Operation op;
        lock (_sync)
        {
            if (!Matches(session)) return Task.FromResult(Result(LibraryOutcome.StaleSession, "账号已变化，请重新操作。"));
            if (intent.ExpectedEpoch != 0 && (intent.ExpectedEpoch != session.Epoch || intent.ExpectedAccountId != session.AccountId))
                return Task.FromResult(Result(LibraryOutcome.StaleSession, "确认目标所属账号已变化。"));
            if (_active is not null) return Task.FromResult(Result(LibraryOutcome.Busy, "正在处理音乐库操作，请稍候。"));
            var key = LibraryEditRules.ResourceKey(intent);
            if (pending is null && _pending.TryGetValue(key, out var unresolved)) return Task.FromResult(unresolved.Result!);
            if (pending is null && _pending.Count >= 64) return Task.FromResult(Result(LibraryOutcome.NotSent, "请先核实已有的音乐库操作。"));
            if (pending is not null && (pending.Session.Epoch != session.Epoch || pending.Session.AccountId != session.AccountId))
                return Task.FromResult(Result(LibraryOutcome.StaleSession, "旧账号的结果不能在当前账号核实。"));
            if (pending?.NotBefore is { } notBefore && _time.GetUtcNow() < notBefore)
                return Task.FromResult(pending.Result! with { Message = "服务端要求稍后再读取，请等待限流时间结束。" });
            op = new(intent, session, pending); _active = op; _tasks.Add(op.Completion.Task);
            _readGeneration++; _snapshot = _snapshot with { IsBusy = true, Revision = _snapshot.Revision + 1, Message = pending is null ? "正在处理音乐库操作…" : "正在重新读取结果…" };
        }
        Publish(); _ = RunAsync(op, pending is not null, ct); return op.Completion.Task;
    }
    private async Task RunAsync(Operation op, bool recheckOnly, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, op.Session.Revoked, _shutdown.Token);
        LibraryMutationResult result;
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!recheckOnly)
            {
                var preflight = await PrepareAsync(op, linked.Token).ConfigureAwait(false);
                if (preflight is not null) { Finish(op, preflight); return; }
                linked.Token.ThrowIfCancellationRequested();
                if (!_sessions.IsCurrent(op.Session)) throw new OperationCanceledException(linked.Token);
                // 从交接给适配器开始，取消或网络异常都不能假定远端没收到。适配器可更精确返回 NotSent。
                op.Dispatched = true;
                op.Receipt = op.Intent.Kind == LibraryOperationKind.Like
                    ? await _likes.SetAsync(op.Intent.TargetId, op.Intent.Desired, op.Session, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false)
                    : await _writes.WriteAsync(op.WireIntent ?? op.Intent, op.Session, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
                if (op.Receipt.RetryAfter is { } retry) op.NotBefore = _time.GetUtcNow() + retry;
            }
            if (op.Receipt is { State: LibraryReceiptState.NotSent or LibraryReceiptState.Rejected } receipt)
                result = new(op.Id, receipt.State == LibraryReceiptState.NotSent ? LibraryOutcome.NotSent : LibraryOutcome.Rejected, receipt.Message, receipt);
            else if (!recheckOnly && op.Receipt?.StopRecheck == true)
                result = new(op.Id, LibraryOutcome.NeedsRecheck, op.Receipt.Message, op.Receipt);
            else result = await ReconcileAsync(op, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new(op.Id, !_sessions.IsCurrent(op.Session) || _closed ? LibraryOutcome.StaleSession :
                op.Dispatched ? LibraryOutcome.NeedsRecheck : LibraryOutcome.NotSent,
                op.Dispatched ? "操作可能已经生效，请重新读取结果；不会自动重发。" : "操作尚未发送，已取消。", op.Receipt);
        }
        catch (Exception ex) when (ex is LibraryReadException or MusicException or ArgumentException)
        {
            result = new(op.Id, op.Dispatched ? LibraryOutcome.NeedsRecheck : LibraryOutcome.NotSent,
                op.Dispatched ? "结果尚未核实，请重新读取；不会自动重发。" : ex.Message, op.Receipt);
        }
        catch (Exception)
        {
            // 外部适配器异常也必须释放写入资格，不传播可能含凭据的底层异常文本。
            result = new(op.Id, op.Dispatched ? LibraryOutcome.NeedsRecheck : LibraryOutcome.NotSent, "音乐库操作未完成，请重新读取后检查。", op.Receipt);
        }
        Finish(op, result);
    }

    private async Task<LibraryMutationResult?> PrepareAsync(Operation op, CancellationToken ct)
    {
        var intent = op.Intent;
        if (intent.Kind == LibraryOperationKind.Create) return null;
        if (intent.Kind == LibraryOperationKind.Like)
        {
            var likes = await _likes.ReadAsync(op.Session, ct).WaitAsync(ct).ConfigureAwait(false); op.ReadLikes = likes;
            return likes.Contains(intent.TargetId) == intent.Desired ? new(op.Id, LibraryOutcome.Confirmed, "已经是目标喜欢状态。", NoChange: true) : null;
        }
        var playlist = await _playlists.ReadAsync(intent.TargetId, op.Session, ct).WaitAsync(ct).ConfigureAwait(false); op.Before = playlist;
        if (intent.Kind == LibraryOperationKind.Subscribe)
        {
            if (!playlist.CanSubscribe(op.Session.AccountId)) return new(op.Id, LibraryOutcome.NotSent, "只能收藏状态已知的他人歌单。", Playlist: playlist);
            return playlist.Subscribed == intent.Desired ? new(op.Id, LibraryOutcome.Confirmed, "已经是目标收藏状态。", Playlist: playlist, NoChange: true) : null;
        }
        if (!playlist.CanEdit(op.Session.AccountId)) return new(op.Id, LibraryOutcome.NotSent, "仅允许编辑已核验的自有普通歌单。", Playlist: playlist);
        if (LibraryEditRules.Conflicts(intent, playlist)) return new(op.Id, LibraryOutcome.Conflict, "歌单已在其他位置变化，请重新载入并核对草稿。", Playlist: playlist);
        if (intent.Kind == LibraryOperationKind.Description && !playlist.DescriptionKnown) return new(op.Id, LibraryOutcome.NotSent, "原描述尚未读取，暂不能覆盖。", Playlist: playlist);
        if (intent.Kind == LibraryOperationKind.Rename && playlist.Name == intent.Text || intent.Kind == LibraryOperationKind.Description && playlist.Description == intent.Text)
            return new(op.Id, LibraryOutcome.Confirmed, "内容没有变化。", Playlist: playlist, NoChange: true);
        if (intent.Kind is LibraryOperationKind.AddTracks or LibraryOperationKind.RemoveTracks)
        {
            if (!playlist.IsComplete) return new(op.Id, LibraryOutcome.NotSent, "歌单曲目列表不完整，暂不能增删曲目。", Playlist: playlist);
            var add = intent.Kind == LibraryOperationKind.AddTracks;
            var existing = playlist.TrackIds.ToHashSet();
            var actual = intent.TrackIds!.Where(id => existing.Contains(id) != add).ToArray();
            op.WireIntent = intent with { TrackIds = actual };
            if (actual.Length == 0) return new(op.Id, LibraryOutcome.Confirmed, add ? "歌曲原本已在歌单中。" : "歌曲原本已不在歌单中。", Playlist: playlist,
                Items: LibraryEditRules.Classify(intent.TrackIds!, playlist.TrackIds, playlist.TrackIds, add), NoChange: true);
        }
        return null;
    }

    private async Task<LibraryMutationResult> ReconcileAsync(Operation op, CancellationToken ct)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20), _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        var intent = op.Intent;
        if (intent.Kind == LibraryOperationKind.Create && op.Receipt?.CreatedId is not > 0)
            return new(op.Id, LibraryOutcome.NeedsRecheck, "可能已创建歌单，请刷新目录核对；同名歌单不能自动认领。", op.Receipt);
        LibraryPlaylist? latest = null; string? stoppedMessage = null;
        for (var round = 0; round < 3; round++)
        {
            try
            {
                if (round > 0) await Task.Delay(TimeSpan.FromMilliseconds(round == 1 ? 500 : 1500), _time, linked.Token).ConfigureAwait(false);
                if (intent.Kind == LibraryOperationKind.Like)
                {
                    var likes = await _likes.ReadAsync(op.Session, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
                    op.ReadLikes = likes;
                    if (likes.Contains(intent.TargetId) == intent.Desired)
                        return new(op.Id, LibraryOutcome.Confirmed, intent.Desired ? "已喜欢这首歌曲。" : "已取消喜欢。", op.Receipt);
                }
                else
                {
                    latest = await _playlists.ReadAsync(intent.Kind == LibraryOperationKind.Create ? op.Receipt!.CreatedId!.Value : intent.TargetId, op.Session, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
                    var confirmed = intent.Kind switch
                    {
                        LibraryOperationKind.Subscribe => latest.Subscribed == intent.Desired,
                        LibraryOperationKind.Create => latest.CreatorId == op.Session.AccountId && latest.Kind == LibraryPlaylistKind.Normal && latest.Name == (op.Receipt?.ServerName ?? intent.Text),
                        LibraryOperationKind.Rename => latest.Name == (op.Receipt?.ServerName ?? intent.Text),
                        LibraryOperationKind.Description => latest.DescriptionKnown && latest.Description == intent.Text,
                        LibraryOperationKind.AddTracks or LibraryOperationKind.RemoveTracks => latest.IsComplete && intent.TrackIds!.All(id => latest.TrackIds.Contains(id) == (intent.Kind == LibraryOperationKind.AddTracks)),
                        _ => false
                    };
                    if (confirmed) return new(op.Id, LibraryOutcome.Confirmed, "已重新读取并确认音乐库修改。", op.Receipt, latest,
                        intent.TrackIds is null ? null : LibraryEditRules.Classify(intent.TrackIds, op.Before!.TrackIds, latest.TrackIds, intent.Kind == LibraryOperationKind.AddTracks));
                }
            }
            catch (LibraryReadException ex) when (ex.Kind == LibraryReadError.MissingOrInaccessible && intent.Kind == LibraryOperationKind.Delete && op.Receipt?.State == LibraryReceiptState.Accepted)
            { return new(op.Id, LibraryOutcome.Confirmed, "删除回执成功，重新读取已不可找到该歌单。", op.Receipt); }
            catch (LibraryReadException ex)
            {
                if (ex.StopsRecheck) { stoppedMessage = ex.Message; if (ex.RetryAfter is { } retry) op.NotBefore = _time.GetUtcNow() + retry; break; }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
        }
        var items = intent.TrackIds is not null && op.Before is not null
            ? LibraryEditRules.Classify(intent.TrackIds, op.Before.TrackIds, latest is { IsComplete: true } ? latest.TrackIds : null, intent.Kind == LibraryOperationKind.AddTracks) : null;
        var partial = items?.Any(i => i.State is LibraryItemState.Added or LibraryItemState.Removed) == true;
        return new(op.Id, partial ? LibraryOutcome.PartiallyConfirmed : LibraryOutcome.NeedsRecheck,
            stoppedMessage ?? (partial ? "部分歌曲已确认，其余待核实；不会自动重复增删。" : "结果尚未核实，请重新读取；不会自动重发。"), op.Receipt, latest, items);
    }

    private void Finish(Operation op, LibraryMutationResult result)
    {
        if (result.Receipt?.CredentialSaveFailed == true)
            result = result with { Message = result.Message + " 会话保存失败，请检查本地存储；不会重复提交修改。" };
        var publish = false;
        lock (_sync)
        {
            if (Matches(op.Session) && ReferenceEquals(_active, op) && _sessions.IsCurrent(op.Session))
            {
                op.Result = result;
                var key = LibraryEditRules.ResourceKey(op.Intent);
                if (result.Outcome is LibraryOutcome.NeedsRecheck or LibraryOutcome.PartiallyConfirmed) _pending[key] = op;
                else _pending.Remove(key);
                _active = null;
                _snapshot = _snapshot with { IsBusy = false, Revision = _snapshot.Revision + 1, LastResult = result, Message = result.Message,
                    Likes = op.ReadLikes ?? _snapshot.Likes };
                UpdatePending(); publish = true;
            }
            else result = result with { Outcome = LibraryOutcome.StaleSession, Message = "账号或操作代次已变化，旧结果已丢弃。" };
            _tasks.Remove(op.Completion.Task);
        }
        if (publish)
        {
            var changed = result.Outcome is LibraryOutcome.Confirmed or LibraryOutcome.PartiallyConfirmed or LibraryOutcome.NeedsRecheck;
            Publish(op.Intent.Kind == LibraryOperationKind.Create ? result.Playlist?.Id : op.Intent.Kind == LibraryOperationKind.Like ? null : op.Intent.TargetId,
                changed, changed && op.Intent.Kind == LibraryOperationKind.Delete && result.Outcome == LibraryOutcome.Confirmed, op.ReadLikes is not null);
        }
        op.Completion.TrySetResult(result);
    }
    private void UpdatePending() => _snapshot = _snapshot with { Pending = _pending.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Result!) };
    private static LibraryMutationResult Result(LibraryOutcome outcome, string message) => new(Guid.NewGuid(), outcome, message);

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        Task[] pending;
        lock (_sync)
        {
            if (_dispose is not null) return new(_dispose);
            _closed = true; _readGeneration++; _active = null; _pending.Clear();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _dispose = completion.Task;
            pending = _tasks.ToArray(); Changed = null;
        }
        _account.Unregister(); _shutdown.Cancel(); _ = Drain(); return new(completion.Task);
        async Task Drain()
        {
            try { await Task.WhenAll(pending).ConfigureAwait(false); }
            finally { _shutdown.Dispose(); completion.TrySetResult(); }
        }
    }
    // Host Shutdown 会先异步收口；容器随后重复 Dispose 只等待同一个已完成的尾任务。
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    private sealed class Operation(LibraryIntent intent, MusicSession session, Operation? previous)
    {
        public Guid Id { get; } = previous?.Id ?? Guid.NewGuid();
        public LibraryIntent Intent { get; } = intent;
        public MusicSession Session { get; } = session;
        public LibraryIntent? WireIntent;
        public LibraryPlaylist? Before = previous?.Before;
        public LibraryWriteReceipt? Receipt = previous?.Receipt;
        public bool Dispatched = previous?.Dispatched ?? false;
        public DateTimeOffset? NotBefore = previous?.NotBefore;
        public ImmutableHashSet<long>? ReadLikes;
        public LibraryMutationResult? Result;
        public TaskCompletionSource<LibraryMutationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
