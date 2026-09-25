using MusicNetEasePlugin.Application.Lyrics;
using MyAvaloniaManagement.PluginSdk;

namespace MusicNetEasePlugin.Application.Playback;

/// <summary>插件私有播放资源由音乐 Document 共同持有，设置 Tool 不计入租约。</summary>
public sealed class MusicPlaybackLifetime(PlaybackQueueCoordinator queue, LyricsCoordinator? lyrics = null) : IDisposable, IAsyncDisposable
{
    private readonly object _sync = new();
    private int _leases;
    private bool _closed;
    private Task _pending = Task.CompletedTask;
    private Task? _shutdown;

    public Task Pending { get { lock (_sync) return _pending; } }

    internal void Acquire()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_leases++ != 0) return;
            queue.ResumeDocumentSession(_pending);
            lyrics?.Resume();
        }
    }

    internal Task Release()
    {
        lock (_sync)
        {
            if (_closed || --_leases != 0) return _pending;
            return _pending = EndSession();
        }
    }

    private Task EndSession()
    {
        // 两个入口同步撤销接纳资格，异步尾任务只在后台等待，不依赖 UI continuation。
        var lyricWork = lyrics?.Suspend() ?? Task.CompletedTask;
        var playbackWork = queue.SuspendDocumentSessionAsync();
        return Task.WhenAll(_pending, lyricWork, playbackWork);
    }

    public Task ShutdownAsync()
    {
        lock (_sync)
        {
            if (_shutdown is not null) return _shutdown;
            _closed = true;
            _leases = 0;
            return _shutdown = _pending = EndSession();
        }
    }

    public ValueTask DisposeAsync() => new(ShutdownAsync());
    public void Dispose() => ShutdownAsync().GetAwaiter().GetResult();
}

/// <summary>每个 Document Scope 一份；关闭令牌与 Scope.Dispose 共享一次归还和同一完成任务。</summary>
public sealed class MusicPlaybackLease : IDisposable
{
    private readonly MusicPlaybackLifetime _owner;
    private readonly object _sync = new();
    private readonly CancellationTokenRegistration _registration;
    private Task? _release;

    public MusicPlaybackLease(MusicPlaybackLifetime owner, IDocumentLifetime lifetime)
    {
        _owner = owner;
        owner.Acquire();
        _registration = lifetime.ClosingToken.Register(() => _ = Close());
    }

    public Task Close()
    {
        lock (_sync) return _release ??= _owner.Release();
    }

    public void Dispose()
    {
        try { Close().GetAwaiter().GetResult(); }
        finally { _registration.Dispose(); }
    }
}
