using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Application.Discovery;

/// <summary>
/// 容器共享的 FM 内容和反馈服务，没有队列或音频依赖。队列在锁外取内容，锁内核验会话后接纳。
/// 同一轮共用任务，空批和重复批才允许有界补取；耗尽由明确意图重启，进度事件不能重开失败链。
/// </summary>
public sealed class PrivateFmCoordinator(IPrivateFmContentApi content, IPrivateFmFeedbackApi feedback,
    IMusicSessionAccessor sessions, TimeProvider time) : IDisposable
{
    private sealed class Session
    {
        public Session(Guid id, MusicSession account)
        { Id = id; Account = account; Cancel = CancellationTokenSource.CreateLinkedTokenSource(account.Revoked); Token = Cancel.Token; }
        public Guid Id { get; }
        public MusicSession Account { get; }
        public CancellationTokenSource Cancel { get; }
        public CancellationToken Token { get; }
        public Queue<long> Recent { get; } = new();
        public Queue<Guid> FeedbackTargets { get; } = new();
        public Task<IReadOnlyList<MusicTrack>>? Read { get; set; }
        public bool FeedbackBusy { get; set; }
    }
    private readonly object _sync = new();
    private readonly SemaphoreSlim _wire = new(1);
    private Session? _session;
    private bool _closed;
    // 限流属于账号代次而不是 FM 会话，结束后再开始也不能绕过服务端等待窗口。
    private MusicSession? _limitedAccount;
    private DateTimeOffset _retryAfter;
    private FmSnapshot _snapshot = new(Guid.Empty, FmContentState.Idle);
    public FmSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    public event EventHandler<FmSnapshot>? Changed;
    internal int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;
    // 只读诊断记录用于预算验证，记录实际执行次数与等待，不参与业务决策。
    internal int WaitingDelayMilliseconds { get; private set; }
    internal (int Requests, double ElapsedMs, int[] Delays) LastRead { get; private set; }
    public Guid Begin(MusicSession account)
    {
        End();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (!sessions.IsCurrent(account)) throw new OperationCanceledException();
            _session = new(Guid.NewGuid(), account);
            _snapshot = Limited(account) ? new(_session.Id, FmContentState.Failed, "FM 请求受限，请等待服务端窗口后再开始。")
                : new(_session.Id, FmContentState.Loading, "正在取得私人 FM…");
            return _session.Id;
        }
    }
    public bool IsCurrent(Guid id) { lock (_sync) return Current(_session) && _session!.Id == id; }
    public Task<IReadOnlyList<MusicTrack>> ReadAsync(Guid id, IReadOnlyCollection<long> excluded, bool explicitRetry = false)
    {
        lock (_sync)
        {
            var session = _session;
            if (!Current(session) || session!.Id != id) return Task.FromResult<IReadOnlyList<MusicTrack>>([]);
            if (Limited(session.Account)) return Task.FromResult<IReadOnlyList<MusicTrack>>([]);
            if (session.Read is { IsCompleted: false }) return session.Read;
            if (!explicitRetry && _snapshot.State is FmContentState.Exhausted or FmContentState.Failed) return Task.FromResult<IReadOnlyList<MusicTrack>>([]);
            _snapshot = _snapshot with { State = FmContentState.WaitingForContent, Message = "正在补充 FM 内容…" };
            // Task.Run 保证首次 await 前也不在调用者队列锁内进入 HTTP 或通知订阅者。
            return session.Read = Task.Run(() => ReadCore(session, excluded.ToHashSet()));
        }
    }
    private async Task<IReadOnlyList<MusicTrack>> ReadCore(Session session, HashSet<long> excluded)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(session.Token, budget.Token);
        var held = false;
        var started = time.GetTimestamp(); var requests = 0; var delays = new List<int>();
        try
        {
            Notify(); // 首次状态也在锁外发布，网络等待期间页面能显示“正在补充”。
            await _wire.WaitAsync(linked.Token).ConfigureAwait(false); held = true;
            lock (_sync) { foreach (var id in session.Recent) excluded.Add(id); }
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                {
                    var milliseconds = attempt == 1 ? 500 : 1500;
                    var waiting = Task.Delay(TimeSpan.FromMilliseconds(milliseconds), time, linked.Token);
                    WaitingDelayMilliseconds = milliseconds; delays.Add(milliseconds);
                    try { await waiting.ConfigureAwait(false); } finally { WaitingDelayMilliseconds = 0; }
                }
                requests++;
                var pending = content.ReadAsync(session.Account, linked.Token);
                IReadOnlyList<MusicTrack> response;
                try { response = await pending.WaitAsync(linked.Token).ConfigureAwait(false); }
                catch
                {
                    // 不遵守取消的适配器也不能阻塞页面关闭；物理读取槽仍由旧任务持有，防止另开并发请求。
                    if (!pending.IsCompleted) { held = false; _ = ReleaseWireWhenDone(pending); }
                    throw;
                }
                linked.Token.ThrowIfCancellationRequested();
                var result = response.Where(t => t.Id > 0 && excluded.Add(t.Id)).Take(10).ToArray();
                lock (_sync)
                {
                    if (!Current(session)) return [];
                    if (result.Length > 0)
                    {
                        foreach (var track in result) { session.Recent.Enqueue(track.Id); while (session.Recent.Count > 100) session.Recent.Dequeue(); }
                        _snapshot = _snapshot with { State = FmContentState.Active, Message = "私人 FM · 下一首不会提交不喜欢。" };
                        return result;
                    }
                }
            }
            SetState(session, FmContentState.Exhausted, "暂无新的 FM 内容，可重新开始；FM 已在运行时也可点击下一首重试。");
        }
        catch (OperationCanceledException)
        { SetState(session, FmContentState.Exhausted, "FM 取数已取消或达到 20 秒预算，可重新开始或点击下一首重试。"); }
        catch (Exception ex)
        {
            if (ex is MusicException { Kind: MusicError.RateLimited } limited)
                lock (_sync) { if (Current(session)) { _limitedAccount = session.Account; _retryAfter = time.GetUtcNow() + (limited.RetryAfter ?? TimeSpan.FromSeconds(30)); } }
            SetState(session, FmContentState.Failed, ex is MusicException ? ex.Message : "FM 读取失败，请稍后重试。");
        }
        finally { LastRead = (requests, time.GetElapsedTime(started).TotalMilliseconds, delays.ToArray()); if (held) _wire.Release(); Notify(); }
        return [];
    }
    public async Task<FmFeedbackResult> DislikeAsync(FmFeedbackTarget target, CancellationToken ct)
    {
        Session session;
        lock (_sync)
        {
            if (!Current(_session) || _session!.Id != target.SessionId || _session.Account.Epoch != target.AccountEpoch || target.EntryId == Guid.Empty || target.TrackId <= 0 || ct.IsCancellationRequested)
                return new(FmFeedbackState.NotSent, "当前 FM 目标已变化，未发送反馈。");
            session = _session;
            if (session.FeedbackBusy || session.FeedbackTargets.Contains(target.EntryId)) return new(FmFeedbackState.NotSent, "目标正在反馈或已经反馈，不会重复发送。");
            session.FeedbackBusy = true; session.FeedbackTargets.Enqueue(target.EntryId);
            while (session.FeedbackTargets.Count > 100) session.FeedbackTargets.Dequeue();
            _snapshot = _snapshot with { FeedbackBusy = true, Feedback = null };
        }
        Notify();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Token);
        FmFeedbackResult result;
        try { result = await feedback.DislikeAsync(target.TrackId, session.Account, linked.Token).WaitAsync(TimeSpan.FromSeconds(15), time, linked.Token).ConfigureAwait(false); }
        catch (Exception) { result = new(FmFeedbackState.Uncertain, "反馈结果未确认，未自动重发。"); }
        lock (_sync)
        {
            session.FeedbackBusy = false;
            if (Current(session)) _snapshot = _snapshot with { FeedbackBusy = false, Feedback = result };
        }
        Notify(); return result;
    }
    private bool Current(Session? session) => !_closed && session is not null && ReferenceEquals(session, _session) && !session.Cancel.IsCancellationRequested && sessions.IsCurrent(session.Account);
    private bool Limited(MusicSession account) => _limitedAccount?.AccountId == account.AccountId && _limitedAccount.Epoch == account.Epoch && _retryAfter > time.GetUtcNow();
    private async Task ReleaseWireWhenDone(Task pending)
    { try { await pending.ConfigureAwait(false); } catch (Exception) { /* 迟到错误已失去提交资格。 */ } finally { _wire.Release(); } }
    private void SetState(Session session, FmContentState state, string message)
    { lock (_sync) { if (Current(session)) _snapshot = _snapshot with { State = state, Message = message }; } }
    public void End()
    {
        Session? old;
        lock (_sync) { old = _session; _session = null; _snapshot = new(Guid.Empty, FmContentState.Idle, "私人 FM 已结束。"); }
        // 先撤销身份，再触发取消，避免取消回调或旧 finally 修改下一次会话。
        old?.Cancel.Cancel();
        if (old is not null) _ = DisposeCancellation(old);
        Notify();
    }
    private static async Task DisposeCancellation(Session session)
    { if (session.Read is { } read) await read.ConfigureAwait(false); session.Cancel.Dispose(); }
    private void Notify()
    {
        var value = Snapshot;
        if (Changed is { } handlers) foreach (EventHandler<FmSnapshot> handler in handlers.GetInvocationList())
            try { handler(this, value); } catch (Exception) { /* 一个页面订阅失败不改变共享内容事实。 */ }
    }
    public void Dispose() { lock (_sync) { if (_closed) return; _closed = true; } End(); Changed = null; }
}
