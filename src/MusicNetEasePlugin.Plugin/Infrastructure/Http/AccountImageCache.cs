using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>
/// 一个插件容器共享的压缩图片缓存。相同地址合并请求，等待者各自取消；最后一个等待者离开才撤销下载。
/// 下载槽只覆盖网络工作，绝不在等待 UI 时占用。账户切换后旧请求即使迟到，也不能重新进入缓存。
/// </summary>
internal sealed class AccountImageCache : IAccountImageSource, IDisposable
{
    internal const int ByteLimit = 8 * 1024 * 1024;
    private readonly IAccountImageSource _transport;
    private readonly LoginCoordinator? _login;
    private readonly object _sync = new();
    private int _active;
    private readonly Dictionary<string, Download> _pending = [];
    private readonly Dictionary<string, (byte[] Bytes, long Used)> _cache = [];
    private long _clock, _epoch, _loginRevision = -1;
    private long? _account;
    private bool _disposed;
    internal int CachedBytes { get { lock (_sync) return _cache.Values.Sum(x => x.Bytes.Length); } }
    internal int PendingCount { get { lock (_sync) return _pending.Count; } }
    private sealed class Download
    {
        public readonly CancellationTokenSource Cancellation = new();
        public readonly TaskCompletionSource<byte[]?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Readers;
        public bool Started, Priority;
        public string Address = "";
        public long Epoch;
    }
    public AccountImageCache(IAccountImageSource transport, LoginCoordinator? login = null)
    {
        (_transport, _login) = (transport, login);
        if (login is not null) { _account = login.Snapshot.Account?.Id; login.Changed += LoginChanged; }
    }
    private void LoginChanged(object? sender, LoginSnapshot snapshot)
    {
        lock (_sync)
        {
            if (snapshot.Revision <= _loginRevision) return;
            _loginRevision = snapshot.Revision;
            if (_account == snapshot.Account?.Id) return;
            _account = snapshot.Account?.Id; ClearCore();
        }
    }
    public Task<byte[]?> LoadAsync(string? address, CancellationToken cancellationToken) => LoadAsync(address, false, cancellationToken);
    public Task<byte[]?> LoadPriorityAsync(string? address, CancellationToken cancellationToken) => LoadAsync(address, true, cancellationToken);
    private async Task<byte[]?> LoadAsync(string? address, bool priority, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 2048) return null;
        address = AccountImageSource.Normalize(address) ?? address;
        cancellationToken.ThrowIfCancellationRequested();
        Download download;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cache.TryGetValue(address, out var hit)) { _cache[address] = (hit.Bytes, ++_clock); return hit.Bytes; }
            if (!_pending.TryGetValue(address, out download!))
            {
                // 有限可见控件之外的请求直接降级为占位，避免恶意大列表创建无限等待任务。
                if (_pending.Count >= 64) return null;
                download = new() { Address = address, Epoch = _epoch }; _pending.Add(address, download);
            }
            download.Readers++; download.Priority |= priority; Pump();
        }
        try { return await download.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            lock (_sync)
            {
                if (--download.Readers == 0 && !download.Completion.Task.IsCompleted)
                {
                    if (_pending.TryGetValue(address, out var current) && ReferenceEquals(current, download)) _pending.Remove(address);
                    download.Cancellation.Cancel();
                    if (!download.Started) { download.Completion.TrySetResult(null); download.Cancellation.Dispose(); }
                }
            }
        }
    }
    private void Pump()
    {
        // 已开始的两个请求不抢断；新空位先交给当前封面，再按登记顺序处理缩略图。
        while (_active < 2)
        {
            var next = _pending.Values.Where(x => !x.Started).OrderByDescending(x => x.Priority).FirstOrDefault();
            if (next is null) break;
            next.Started = true; _active++; _ = FetchAsync(next.Address, next, next.Epoch);
        }
    }
    private async Task FetchAsync(string address, Download download, long epoch)
    {
        // 先异步移出注册临界区，确保 Readers 已登记，避免同步完成的传输在登记前释放 CTS。
        await Task.Yield();
        byte[]? bytes = null;
        try
        {
            bytes = await _transport.LoadAsync(address, download.Cancellation.Token).ConfigureAwait(false);
            lock (_sync)
            {
                if (_disposed || epoch != _epoch || download.Cancellation.IsCancellationRequested) bytes = null;
                if (bytes is { Length: > 0 and <= 2 * 1024 * 1024 })
                {
                    while (_cache.Count > 0 && (CachedBytes + bytes.Length > ByteLimit || _cache.Count >= 128))
                        _cache.Remove(_cache.MinBy(x => x.Value.Used).Key);
                    _cache[address] = (bytes, ++_clock);
                }
                else bytes = null;
            }
        }
        catch (Exception) { /* 可选图片的下载、超时及撤销统一降级，不泄漏底层地址或账号信息。 */ }
        finally
        {
            lock (_sync)
            {
                if (_pending.TryGetValue(address, out var current) && ReferenceEquals(current, download)) _pending.Remove(address);
                download.Completion.TrySetResult(bytes);
                download.Cancellation.Dispose();
                _active--; Pump();
            }
        }
    }
    internal void Clear() { lock (_sync) ClearCore(); }
    private void ClearCore()
    {
        _epoch++; _cache.Clear();
        foreach (var task in _pending.Values)
        {
            task.Cancellation.Cancel();
            if (!task.Started) { task.Completion.TrySetResult(null); task.Cancellation.Dispose(); }
        }
        _pending.Clear();
    }
    public void Dispose()
    {
        if (_login is not null) _login.Changed -= LoginChanged;
        lock (_sync) { if (_disposed) return; _disposed = true; ClearCore(); }
        // 在途任务有自己的取消与 finally；容器释放不阻塞 UI 等待可选图片。
    }
}
