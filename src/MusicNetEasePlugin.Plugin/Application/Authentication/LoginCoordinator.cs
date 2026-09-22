namespace MusicNetEasePlugin.Application.Authentication;

/// <summary>
/// 一个插件容器共享一个账号与登录流程。状态锁只保护快照/代次，不跨网络或文件 await。
/// 新操作先取消并等待前一操作收口，因而磁盘补偿不会与新登录提交交叉；UI 可立即显示取消。
/// </summary>
public sealed class LoginCoordinator : IAsyncDisposable, IDisposable
{
    private readonly INeteaseAuthApi _api;
    private readonly ILoginSessionStore _store;
    private readonly TimeProvider _time;
    private readonly LoginOptions _options;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Operation? _current;
    private Task _tail = Task.CompletedTask;
    private long _generation;
    private bool _stopping;
    private bool _restored;
    private AuthContext _context = AuthContext.Create();
    private LoginSnapshot _snapshot = new(0, LoginStage.SignedOut, "登录后即可连接你的网易云音乐账号。");

    private sealed class Operation(long generation, Guid owner, CancellationTokenSource cancellation)
    {
        public long Generation { get; } = generation;
        public Guid Owner { get; } = owner;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public CancellationToken Token { get; } = cancellation.Token;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public LoginCoordinator(INeteaseAuthApi api, ILoginSessionStore store, TimeProvider time, LoginOptions options)
    {
        if (options.PollInterval <= TimeSpan.Zero || options.AttemptBudget <= TimeSpan.Zero || options.NetworkRetries < 0)
            throw new ArgumentException("登录时间和重试策略无效。", nameof(options));
        (_api, _store, _time, _options) = (api, store, time, options);
    }

    public LoginSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    public event EventHandler<LoginSnapshot>? Changed;

    /// <summary>首次页面激活时尝试恢复；多个页面不会重复发起启动账号检查。手动重试可传 force。</summary>
    public Task RestoreAsync(Guid owner, CancellationToken ct, bool force = false)
    {
        return Begin(owner, LoginStage.Restoring, "正在检查已保存的登录信息…", async op =>
        {
            var context = await _store.LoadAsync(op.Token).ConfigureAwait(false);
            if (!Active(op)) return;
            lock (_sync) { if (ActiveLocked(op)) _context = context; }
            if (!context.HasAccount(_time.GetUtcNow()))
            {
                Update(op, LoginStage.SignedOut, "尚未登录，请使用手机扫码。");
                return;
            }
            var result = await _api.CheckAccountAsync(context, op.Token).ConfigureAwait(false);
            await CommitAsync(op, result, clearIfCancelled: false).ConfigureAwait(false);
        }, ct, canBegin: () => _current is null && _snapshot.Account is null && (!_restored || force),
            prepare: () => _restored = true);
    }

    /// <summary>显式登录或换一个二维码；每个尝试从空账号 Cookie 开始，不复用旧二维码授权状态。</summary>
    public Task StartAsync(Guid owner, CancellationToken ct)
    {
        return Begin(owner, LoginStage.CreatingQr, "正在准备二维码…", async op =>
        {
            AuthContext context;
            try { context = await _store.LoadAsync(op.Token).ConfigureAwait(false); }
            catch (AuthException ex) when (ex.Kind == AuthError.Storage)
            {
                // 本地恢复失败不阻止一次新的显式扫码；保存若仍失败会单独提示，不降级明文。
                lock (_sync) context = _context;
            }
            context = context with { Cookies = context.Cookies.Clear() };
            var key = await _api.CreateKeyAsync(context, op.Token).ConfigureAwait(false);
            context = key.Context;
            Update(op, LoginStage.WaitingForScan, "请使用网易云音乐 App 扫描二维码。", key: key.Key);
            var failures = 0;
            while (Active(op))
            {
                await Task.Delay(_options.PollInterval, _time, op.Token).ConfigureAwait(false);
                QrCheck check;
                try
                {
                    check = await _api.CheckQrAsync(key.Key, context, op.Token).ConfigureAwait(false);
                    failures = 0;
                }
                catch (AuthException ex) when (Retryable(ex) && failures++ < _options.NetworkRetries)
                {
                    Update(op, LoginStage.WaitingForScan, "网络暂时不可用，正在有限重试…", key: key.Key);
                    var delay = ex.RetryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, failures));
                    await Task.Delay(delay, _time, op.Token).ConfigureAwait(false);
                    continue;
                }
                context = check.Context;
                switch (check.Status)
                {
                    case QrStatus.Expired:
                        Update(op, LoginStage.Expired, "二维码已过期，请重新生成。");
                        return;
                    case QrStatus.WaitingForScan:
                        Update(op, LoginStage.WaitingForScan, "请使用网易云音乐 App 扫描二维码。", key: key.Key);
                        break;
                    case QrStatus.WaitingForConfirmation:
                        Update(op, LoginStage.WaitingForConfirmation, "已扫码，请在手机上确认登录。", key: key.Key);
                        break;
                    case QrStatus.Authorized:
                        if (!context.HasAccount(_time.GetUtcNow()))
                            throw new AuthException(AuthError.Protocol, "授权完成，但没有收到可用的登录凭据。");
                        Update(op, LoginStage.Verifying, "正在确认账号…");
                        var account = await _api.CheckAccountAsync(context, op.Token).ConfigureAwait(false);
                        await CommitAsync(op, account, clearIfCancelled: true).ConfigureAwait(false);
                        return;
                    default:
                        throw new AuthException(AuthError.Protocol, "网易返回了未知的扫码状态。");
                }
            }
        }, ct, canBegin: () => _snapshot.Account is null);
    }

    public Task RetrySaveAsync(Guid owner, CancellationToken ct)
    {
        AccountCheck? account = null;
        return Begin(owner, LoginStage.Verifying, "正在重新保存登录信息…",
            op => CommitAsync(op, account!, clearIfCancelled: false), ct,
            canBegin: () => _snapshot.Account is not null,
            prepare: () => account = new(_snapshot.Account!, _context));
    }

    /// <summary>
    /// 退出先撤销内存身份和旧代次，再等前一操作收口并清除磁盘，最后尝试远端退出。
    /// 清除失败与远端失败分别呈现；用户取消网络不应取消已经要求的本地清除。
    /// </summary>
    public Task LogoutAsync(Guid owner, CancellationToken ct)
    {
        AuthContext? previous = null;
        return Begin(owner, LoginStage.SigningOut, "正在清除登录信息…", async op =>
        {
            try { await _store.ClearAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (AuthException ex)
            {
                Update(op, LoginStage.Failed, ex.Message, cleanup: true);
                return;
            }
            Update(op, LoginStage.SigningOut, "本地登录信息已清除，正在确认远端退出…",
                allowCancelled: true, cleanupCompleted: true);
            try
            {
                if (previous!.HasAccount(_time.GetUtcNow()))
                    await _api.LogoutAsync(previous, op.Token).ConfigureAwait(false);
                Update(op, LoginStage.SignedOut, "已退出登录，本地登录信息已清除。");
            }
            catch (AuthException)
            {
                Update(op, LoginStage.SignedOut, "本地登录信息已清除；远端退出未确认，可在官方客户端管理在线设备。");
            }
        }, ct, clearAccount: true, prepare: () =>
        {
            previous = _context;
            _context = _context with { Cookies = _context.Cookies.Clear() };
        });
    }

    /// <summary>仅取消指定页面拥有的流程；其他页面关闭不能取消当前页面的扫码。</summary>
    public void Cancel(Guid owner)
    {
        Operation? operation;
        LoginSnapshot? snapshot = null;
        lock (_sync)
        {
            operation = _current;
            if (operation is null || operation.Owner != owner) return;
            _generation++;
            if (_snapshot.Stage == LoginStage.Restoring) _restored = false;
            _snapshot = _snapshot with
            {
                Revision = _snapshot.Revision + 1,
                Stage = _snapshot.Account is null ? LoginStage.Cancelled : LoginStage.SignedIn,
                Message = "当前操作已取消。", QrKey = null
            };
            snapshot = _snapshot;
        }
        TryCancel(operation);
        Notify(snapshot);
    }

    public Task WaitForIdleAsync() { lock (_sync) return _tail; }
    public bool IsOwnedBy(Guid owner) { lock (_sync) return _current?.Owner == owner; }

    // SDK 当前释放 IServiceScope/Provider 使用同步 Dispose；所有后台 await 不捕获 UI 上下文，
    // 因此这里可安全等待。Host 正常关闭先调用异步生命周期，此处主要是幂等兜底。
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private Task Begin(Guid owner, LoginStage stage, string message, Func<Operation, Task> work,
        CancellationToken ct, bool clearAccount = false, Func<bool>? canBegin = null, Action? prepare = null)
    {
        Operation operation;
        Operation? old;
        Task previous;
        LoginSnapshot snapshot;
        lock (_sync)
        {
            // 前置判断、读取当前会话与登记新操作必须原子完成，避免另一页面在两把锁之间提交账号。
            if (_stopping || canBegin?.Invoke() == false) return _tail;
            prepare?.Invoke();
            old = _current;
            previous = _tail;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
            operation = new(++_generation, owner, cancellation);
            _current = operation;
            _tail = operation.Completion.Task;
            _snapshot = _snapshot with
            {
                Revision = _snapshot.Revision + 1, Stage = stage, Message = message, QrKey = null,
                Account = clearAccount ? null : _snapshot.Account,
                Remembered = clearAccount ? false : _snapshot.Remembered,
                CleanupRequired = clearAccount || _snapshot.CleanupRequired
            };
            snapshot = _snapshot;
        }
        TryCancel(old);
        Notify(snapshot);
        _ = ExecuteAsync(operation, previous, work, clearAccount);
        return operation.Completion.Task;
    }

    private async Task ExecuteAsync(Operation op, Task previous, Func<Operation, Task> work, bool mustClear)
    {
        // 替换操作必须等待前一个文件提交/补偿结束，不能只发出取消便认为资源已闲置。
        using var deadline = new CancellationTokenSource(_options.AttemptBudget, _time);
        using var registration = deadline.Token.Register(() => TryCancel(op));
        try
        {
            await previous.ConfigureAwait(false);
            if (!mustClear) op.Token.ThrowIfCancellationRequested();
            await work(op).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Update(op, deadline.IsCancellationRequested ? LoginStage.Expired : LoginStage.Cancelled,
                deadline.IsCancellationRequested ? "等待已结束，请重新尝试。" : "当前操作已取消。", allowCancelled: true);
        }
        catch (AuthException ex)
        {
            if (ex.Kind == AuthError.SessionExpired)
            {
                lock (_sync)
                {
                    if (ActiveLocked(op))
                    {
                        _context = _context with { Cookies = _context.Cookies.Clear() };
                        _snapshot = _snapshot with { Account = null, Remembered = false };
                    }
                }
            }
            Update(op, LoginStage.Failed, ex.Message);
        }
        catch (Exception)
        {
            // 不把未知底层异常传播到异步命令/Host 日志中，防止包含凭据；用户仍能取消或重试。
            Update(op, LoginStage.Failed, "登录过程未能完成，请重试或清除本地登录信息。");
        }
        finally
        {
            lock (_sync) { if (ReferenceEquals(_current, op)) _current = null; }
            op.Cancellation.Dispose();
            op.Completion.TrySetResult();
        }
    }

    private async Task CommitAsync(Operation op, AccountCheck account, bool clearIfCancelled)
    {
        op.Token.ThrowIfCancellationRequested();
        if (!account.Context.HasAccount(_time.GetUtcNow()))
            throw new AuthException(AuthError.SessionExpired, "登录凭据已失效，请重新扫码。");
        var remembered = false;
        var message = "已登录，登录信息已安全保存。";
        try
        {
            await _store.SaveAsync(account.Context, op.Token).ConfigureAwait(false);
            remembered = true;
        }
        catch (AuthException ex) when (ex.Kind == AuthError.Storage)
        {
            message = ex.Message;
        }
        LoginSnapshot? snapshot = null;
        lock (_sync)
        {
            // 只在同一个临界区内决定发布或补偿，避免“检查有效”与“发布”之间被取消。
            if (ActiveLocked(op))
            {
                _context = account.Context;
                _snapshot = new(_snapshot.Revision + 1, LoginStage.SignedIn, message,
                    account.Account, Remembered: remembered);
                snapshot = _snapshot;
            }
        }
        if (snapshot is not null) Notify(snapshot);
        else if (remembered && clearIfCancelled)
        {
            try { await _store.ClearAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (AuthException)
            {
                LoginSnapshot warning;
                lock (_sync)
                {
                    _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, CleanupRequired = true,
                        Message = "取消完成，但登录信息清理失败，请点击清除本地登录信息。" };
                    warning = _snapshot;
                }
                Notify(warning);
            }
        }
    }

    private bool Active(Operation op) { lock (_sync) return ActiveLocked(op); }
    private bool ActiveLocked(Operation op) => !_stopping && op.Generation == _generation && !op.Token.IsCancellationRequested;

    private void Update(Operation op, LoginStage stage, string message, string? key = null,
        bool cleanup = false, bool allowCancelled = false, bool cleanupCompleted = false)
    {
        LoginSnapshot snapshot;
        lock (_sync)
        {
            if (_stopping || op.Generation != _generation || (!allowCancelled && op.Token.IsCancellationRequested)) return;
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Stage = stage,
                Message = message, QrKey = key, CleanupRequired = cleanup || (!cleanupCompleted && stage != LoginStage.SignedOut && _snapshot.CleanupRequired) };
            snapshot = _snapshot;
        }
        Notify(snapshot);
    }

    private static bool Retryable(AuthException ex) => ex.Kind is AuthError.Network or AuthError.Timeout ||
        ex.Kind == AuthError.RateLimited && ex.RetryAfter.HasValue;

    private static void TryCancel(Operation? operation)
    {
        try { operation?.Cancellation.Cancel(); }
        catch (ObjectDisposedException) { /* 已收口的前一操作不再需要取消。 */ }
    }

    private void Notify(LoginSnapshot snapshot)
    {
        if (Changed is not { } changed) return;
        foreach (EventHandler<LoginSnapshot> listener in changed.GetInvocationList())
        {
            try { listener(this, snapshot); }
            catch (Exception)
            {
                // 展示订阅者异常不能截断登录任务或使 Completion 永不完成，也不输出敏感快照。
            }
        }
    }

    /// <summary>插件停止时等待所有取消、磁盘补偿和请求收口，再由容器释放 Flurl。</summary>
    public async ValueTask DisposeAsync()
    {
        Task tail;
        Operation? current;
        lock (_sync)
        {
            _stopping = true;
            _generation++;
            tail = _tail;
            current = _current;
        }
        TryCancel(current);
        await tail.ConfigureAwait(false);
        _shutdown.Dispose();
    }
}
