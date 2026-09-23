using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Application.Lyrics;

/// <summary>
/// 插件共享歌词与同步索引。缓存只保留本账号最近 20 首解析结果；换曲取消旧请求，回包还需核验请求代次及账号。
/// 不控制音频，也不把歌词失败变成切歌或退出；重试必须由用户主动发起。
/// </summary>
public sealed class LyricsCoordinator : IDisposable, IAsyncDisposable
{
    private readonly IPlayerSession _player;
    private readonly IMusicSessionAccessor _sessions;
    private readonly ILyricsApi _api;
    private readonly object _sync = new();
    private readonly Dictionary<long, LyricDocument> _cache = [];
    private readonly CancellationTokenSource _closing = new();
    private CancellationTokenSource? _request;
    private long _generation;
    private long _playerRevision = -1;
    private bool _closed;
    private Task _pending = Task.CompletedTask;
    private Task? _shutdown;
    private LyricsSnapshot _snapshot = new(0, 0, 0, LyricDocument.Empty, -1, 0);
    public LyricsCoordinator(IPlayerSession player, IMusicSessionAccessor sessions, ILyricsApi api)
    { (_player, _sessions, _api) = (player, sessions, api); player.Changed += PlayerChanged; PlayerChanged(null, player.Snapshot); }
    public LyricsSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    public event EventHandler<LyricsSnapshot>? Changed;
    internal int CachedCount { get { lock (_sync) return _cache.Count; } }
    public Task Pending { get { lock (_sync) return _pending; } }
    public Task RetryAsync()
    {
        lock (_sync) { if (_closed || _snapshot.TrackId == 0) return Task.CompletedTask; _cache.Remove(_snapshot.TrackId); Start(); return _pending; }
    }
    private void PlayerChanged(object? sender, PlayerSessionSnapshot player)
    {
        lock (_sync)
        {
            if (_closed || player.Revision <= _playerRevision) return;
            _playerRevision = player.Revision;
            var id = player.AccountId > 0 ? player.Playback.Track?.Id ?? 0 : 0;
            var changed = id != _snapshot.TrackId || player.AccountEpoch != _snapshot.AccountEpoch;
            if (changed)
            {
                _generation++; _request?.Cancel();
                if (player.AccountEpoch != _snapshot.AccountEpoch || id == 0) _cache.Clear();
                _snapshot = new(_snapshot.Revision + 1, id, player.AccountEpoch, _cache.GetValueOrDefault(id) ?? LyricDocument.Empty, -1, player.Playback.PositionMs);
            }
            _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, PositionMs = player.Playback.PositionMs, SyncLimited = player.Playback.IsTrial };
            Synchronize();
            if (changed && id > 0 && !_cache.ContainsKey(id)) Start();
        }
        Notify();
    }
    private void Start()
    {
        var generation = ++_generation; var id = _snapshot.TrackId; var epoch = _snapshot.AccountEpoch;
        _request?.Cancel(); var request = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token); _request = request;
        _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Loading = true, Failed = false, Message = "正在加载歌词…" };
        var next = LoadAsync(); _pending = _pending.IsCompleted ? next : Task.WhenAll(_pending, next);
        async Task LoadAsync()
        {
            await Task.Yield(); Notify();
            try
            {
                var session = _sessions.Capture();
                if (session.Epoch != epoch) return;
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Token, session.Revoked);
                var raw = await _api.GetAsync(id, session, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested(); var document = LyricParser.Parse(raw);
                lock (_sync)
                {
                    if (_closed || generation != _generation || !_sessions.IsCurrent(session)) return;
                    if (_cache.Count >= 20) _cache.Remove(_cache.Keys.First());
                    _cache[id] = document;
                    _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Document = document, Loading = false, Message = "" }; Synchronize();
                }
                Notify();
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                lock (_sync) { if (_closed || generation != _generation) return; _snapshot = _snapshot with { Revision = _snapshot.Revision + 1, Loading = false, Failed = true, Message = "歌词读取失败，可点击重试。" }; }
                Notify();
            }
            finally { lock (_sync) if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
        }
    }
    private void Synchronize() => _snapshot = _snapshot with { CurrentLine = _snapshot.SyncLimited ? -1 : LyricTimeline.FindLine(_snapshot.Document.Lines, _snapshot.PositionMs) };
    private void Notify()
    {
        var value = Snapshot;
        if (Changed is { } handlers) foreach (EventHandler<LyricsSnapshot> handler in handlers.GetInvocationList())
            try { handler(this, value); } catch (Exception) { /* 页面订阅异常不影响播放和其他页面。 */ }
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_shutdown is not null) return new(_shutdown);
            _closed = true; _generation++; _player.Changed -= PlayerChanged; _closing.Cancel(); Changed = null; _cache.Clear();
            return new(_shutdown = Finish());
        }
        async Task Finish() { await Task.Yield(); await _pending.ConfigureAwait(false); _closing.Dispose(); }
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
