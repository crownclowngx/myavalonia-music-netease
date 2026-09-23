using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Application.Playback;

/// <summary>
/// 组合账号事件与恢复用例，存储组件本身不依赖队列。账号 ID/epoch 必须来自已核验会话，
/// 显式退出清除续播，会话失效保留最后有效记录；迟到读取必须通过队列 revision 和账号双重校验。
/// </summary>
public sealed class PlayerAccountCoordinator : IAsyncDisposable, IDisposable
{
    private readonly LoginCoordinator _login;
    private readonly IMusicSessionAccessor _sessions;
    private readonly PlaybackQueueCoordinator _queue;
    private readonly PlaybackPersistence _persistence;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _closing = new();
    private long _loginRevision = -1, _epoch = -1, _accountId;
    private Task _pending = Task.CompletedTask;
    private Task? _shutdown;
    private bool _closed;
    public PlayerAccountCoordinator(LoginCoordinator login, IMusicSessionAccessor sessions, PlaybackQueueCoordinator queue, PlaybackPersistence persistence)
    {
        (_login, _sessions, _queue, _persistence) = (login, sessions, queue, persistence);
        login.Changed += LoginChanged; LoginChanged(null, login.Snapshot);
    }
    public Task Pending { get { lock (_sync) return _pending; } }
    private void LoginChanged(object? sender, LoginSnapshot snapshot)
    {
        lock (_sync)
        {
            if (_closed || snapshot.Revision <= _loginRevision) return;
            _loginRevision = snapshot.Revision;
            if (snapshot.Account is not null)
            {
                MusicSession session;
                try { session = _sessions.Capture(); } catch (MusicException) { return; }
                if (session.Epoch == _epoch && session.AccountId == _accountId) return;
                _epoch = session.Epoch; _accountId = session.AccountId;
                var revision = _queue.Snapshot.Revision;
                Track(Restore(session, revision));
            }
            else if (_accountId != 0 || snapshot.Stage == LoginStage.SigningOut && _persistence.Snapshot.CleanupRequired)
            {
                _epoch = -1; _accountId = 0;
                Track(_persistence.EndAccountAsync(snapshot.Stage == LoginStage.SigningOut));
            }
        }
    }
    private async Task Restore(MusicSession session, long revision)
    {
        using var ct = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token, session.Revoked);
        var data = await _persistence.ActivateAsync(session, ct.Token).ConfigureAwait(false);
        if (data is not null && !ct.IsCancellationRequested && _sessions.IsCurrent(session))
            await _queue.RestoreAsync(session, data, revision).ConfigureAwait(false);
    }
    private void Track(Task task) => _pending = _pending.IsCompleted ? task : Task.WhenAll(_pending, task);
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_shutdown is not null) return new(_shutdown);
            _closed = true; _login.Changed -= LoginChanged; _closing.Cancel();
            return new(_shutdown = Finish());
        }
        async Task Finish() { await Task.Yield(); await Pending.ConfigureAwait(false); _closing.Dispose(); }
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
