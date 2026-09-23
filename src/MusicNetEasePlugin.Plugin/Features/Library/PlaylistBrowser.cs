using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Features.Library;

public sealed record PlaylistTrackRow(int Index, long TrackId, MusicTrack? Track)
{
    public string Name => Track?.Name ?? $"歌曲 {TrackId}（资料暂不可用）";
    public string Detail => Track is null ? "保留原始位置，可重新加载资料" : Track.Artists + " · " + Track.Album;
}

/// <summary>
/// Document 自己的歌单浏览状态。完整队列来源只保存有序 ID，当前页补 50 条资料，缓存最多 500 条。
/// 每次导航先淘汰旧代次再取消；所有集合修改由 UI 调度器投递，关闭时不等待 UI 自身的回调，避免死锁。
/// </summary>
public sealed class PlaylistBrowser : ObservableObject, IDisposable
{
    private readonly IPlaylistCatalogApi _api;
    private readonly IMusicSessionAccessor _sessions;
    private readonly ILoginUiDispatcher _ui;
    private readonly CancellationTokenSource _closing = new();
    private CancellationTokenSource? _operation;
    private CancellationTokenRegistration _account;
    private readonly Dictionary<long, MusicTrack> _cache = new();
    private long _generation;
    private long _accountEpoch;
    private bool _closed;
    private bool _loading;
    private bool _hasMore;
    private int _offset;
    private int _trackOffset;
    private string _message = "点击刷新读取当前账号的歌单。";
    private MusicPlaylist? _selected;
    private PlaylistTrackRow? _selectedTrack;
    private PlaylistTracks? _snapshot;
    private int _tabIndex;
    public PlaylistBrowser(IPlaylistCatalogApi api, IMusicSessionAccessor sessions, ILoginUiDispatcher ui)
    {
        (_api, _sessions, _ui) = (api, sessions, ui);
        RefreshCommand = new AsyncRelayCommand(() => LoadPlaylistsAsync(true), () => !_closed, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        MoreCommand = new AsyncRelayCommand(() => LoadPlaylistsAsync(false), () => HasMore && !IsLoading);
        OpenCommand = new AsyncRelayCommand(() => SelectedPlaylist is { } p ? OpenAsync(p.Id) : Task.CompletedTask, () => SelectedPlaylist is not null && !IsLoading);
        NextTracksCommand = new AsyncRelayCommand(() => LoadTracksAsync(_trackOffset + 50), () => Snapshot is { } s && _trackOffset + 50 < s.TrackIds.Count && !IsLoading);
        PreviousTracksCommand = new AsyncRelayCommand(() => LoadTracksAsync(Math.Max(0, _trackOffset - 50)), () => _trackOffset > 0 && !IsLoading);
        RetryTracksCommand = new AsyncRelayCommand(() => LoadTracksAsync(_trackOffset, true), () => Snapshot is not null && !IsLoading);
    }
    public ObservableCollection<MusicPlaylist> Playlists { get; } = [];
    public ObservableCollection<PlaylistTrackRow> Tracks { get; } = [];
    public MusicPlaylist? SelectedPlaylist { get => _selected; set { SetProperty(ref _selected, value); OpenCommand.NotifyCanExecuteChanged(); } }
    public PlaylistTrackRow? SelectedTrack { get => _selectedTrack; set => SetProperty(ref _selectedTrack, value); }
    public PlaylistTracks? Snapshot { get => _snapshot; private set { SetProperty(ref _snapshot, value); OnPropertyChanged(nameof(Title)); } }
    public string Title => Snapshot?.Name ?? "尚未打开歌单";
    public int TabIndex { get => _tabIndex; set => SetProperty(ref _tabIndex, value); }
    public string PageText => Snapshot is null ? "" : $"第 {_trackOffset / 50 + 1} 页 · {Snapshot.TrackIds.Count} 首";
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool IsLoading { get => _loading; private set { SetProperty(ref _loading, value); CommandsChanged(); } }
    public bool HasMore { get => _hasMore; private set { SetProperty(ref _hasMore, value); MoreCommand.NotifyCanExecuteChanged(); } }
    internal int CachedTracks { get { lock (_cache) return _cache.Count; } }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand MoreCommand { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand NextTracksCommand { get; }
    public IAsyncRelayCommand PreviousTracksCommand { get; }
    public IAsyncRelayCommand RetryTracksCommand { get; }

    public Task LoadPlaylistsAsync(bool refresh) => RunAsync(async (session, generation, ct) =>
    {
        var offset = refresh ? 0 : _offset;
        var page = await _api.PlaylistsAsync(offset, session, ct).ConfigureAwait(false);
        Apply(session, generation, () =>
        {
            if (refresh) { Playlists.Clear(); ClearTracks(); }
            var known = Playlists.Select(p => p.Id).ToHashSet();
            var added = 0;
            foreach (var p in page.Items)
                if (Playlists.Count < 1000 && known.Add(p.Id)) { Playlists.Add(p); added++; }
            _offset = page.NextOffset;
            HasMore = page.HasMore && added > 0 && page.NextOffset > offset && Playlists.Count < 1000;
            Message = Playlists.Count == 0 ? "当前账号没有可访问的歌单。" :
                page.HasMore && added == 0 ? "分页没有取得新歌单，请刷新后重试。" :
                Playlists.Count >= 1000 ? "已达到 1,000 个歌单显示上限。" : $"已加载 {Playlists.Count} 个歌单。";
        });
    });

    public Task OpenAsync(long playlistId) => RunAsync(async (session, generation, ct) =>
    {
        Apply(session, generation, ClearTracks);
        var snapshot = await _api.PlaylistAsync(playlistId, session, ct).ConfigureAwait(false);
        var rows = await FetchRowsAsync(snapshot, 0, session, ct).ConfigureAwait(false);
        Apply(session, generation, () => { Snapshot = snapshot; SetRows(snapshot, 0, rows); TabIndex = 1; });
    });
    public Task LoadTracksAsync(int offset, bool refresh = false)
    {
        var snapshot = Snapshot;
        if (snapshot is null || offset < 0 || offset >= snapshot.TrackIds.Count) return Task.CompletedTask;
        if (refresh) { lock (_cache) _cache.Clear(); }
        return RunAsync(async (session, generation, ct) =>
        {
            var rows = await FetchRowsAsync(snapshot, offset, session, ct).ConfigureAwait(false);
            Apply(session, generation, () => { if (ReferenceEquals(snapshot, Snapshot)) SetRows(snapshot, offset, rows); });
        });
    }
    private async Task<IReadOnlyDictionary<long, MusicTrack>> FetchRowsAsync(PlaylistTracks snapshot, int offset, MusicSession session, CancellationToken ct)
    {
        var ids = snapshot.TrackIds.Skip(offset).Take(50).ToArray();
        if (ids.Length == 0) return new Dictionary<long, MusicTrack>();
        // 拷贝缓存后再离开短锁发请求；退出可以立即清空，不必等待慢网络。
        Dictionary<long, MusicTrack> known;
        lock (_cache) known = ids.Distinct().Where(_cache.ContainsKey).ToDictionary(id => id, id => _cache[id]);
        var missing = ids.Where(id => !known.ContainsKey(id)).Distinct().ToArray();
        if (missing.Length > 0)
            foreach (var (id, track) in await _api.TracksAsync(missing, session, ct).ConfigureAwait(false)) known[id] = track;
        return known;
    }
    private void SetRows(PlaylistTracks snapshot, int offset, IReadOnlyDictionary<long, MusicTrack> details)
    {
        lock (_cache) foreach (var (id, track) in details)
        {
            if (_cache.Count >= 500 && !_cache.ContainsKey(id)) _cache.Remove(_cache.Keys.First());
            _cache[id] = track;
        }
        Tracks.Clear(); SelectedTrack = null; _trackOffset = offset;
        foreach (var index in Enumerable.Range(offset, Math.Min(50, snapshot.TrackIds.Count - offset)))
        { var id = snapshot.TrackIds[index]; Tracks.Add(new(index, id, details.GetValueOrDefault(id))); }
        Message = snapshot.IsComplete ? Tracks.Count == 0 ? "这个歌单还没有歌曲。" : "资料缺失的歌曲仍保留原位置。" : snapshot.Message;
        OnPropertyChanged(nameof(PageText)); CommandsChanged();
    }
    private async Task RunAsync(Func<MusicSession, long, CancellationToken, Task> work)
    {
        if (_closed) return;
        MusicSession session;
        try { session = _sessions.Capture(); }
        catch (MusicException ex) { Message = ex.Message; return; }
        var generation = Interlocked.Increment(ref _generation);
        var operation = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token, session.Revoked);
        var old = Interlocked.Exchange(ref _operation, operation);
        try { old?.Cancel(); } catch (ObjectDisposedException) { }
        _account.Dispose();
        if (_accountEpoch != session.Epoch) { Playlists.Clear(); ClearTracks(); _offset = 0; HasMore = false; }
        _accountEpoch = session.Epoch;
        _account = session.Revoked.Register(() => _ui.Post(() =>
        {
            if (_closed || _accountEpoch != session.Epoch) return;
            Interlocked.Increment(ref _generation); Playlists.Clear(); SelectedPlaylist = null; ClearTracks();
            HasMore = false; IsLoading = false; _offset = 0; Message = "请先登录网易云音乐。";
        }));
        Apply(session, generation, () => { IsLoading = true; Message = "正在加载…"; });
        try { await work(session, generation, operation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Apply(session, generation, () => Message = ex is MusicException ? ex.Message : "歌单读取失败，请刷新重试。"); }
        finally
        {
            Apply(session, generation, () => IsLoading = false);
            Interlocked.CompareExchange(ref _operation, null, operation);
            operation.Dispose();
        }
    }
    private void Apply(MusicSession session, long generation, Action action) => _ui.Post(() =>
    { if (!_closed && generation == Interlocked.Read(ref _generation) && _sessions.IsCurrent(session)) action(); });
    private void ClearTracks() { Snapshot = null; Tracks.Clear(); SelectedTrack = null; lock (_cache) _cache.Clear(); _trackOffset = 0; OnPropertyChanged(nameof(PageText)); CommandsChanged(); }
    private void CommandsChanged()
    { MoreCommand.NotifyCanExecuteChanged(); OpenCommand.NotifyCanExecuteChanged(); NextTracksCommand.NotifyCanExecuteChanged(); PreviousTracksCommand.NotifyCanExecuteChanged(); RetryTracksCommand.NotifyCanExecuteChanged(); }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; Interlocked.Increment(ref _generation); _closing.Cancel(); _account.Dispose(); _closing.Dispose();
        Playlists.Clear(); Tracks.Clear(); lock (_cache) _cache.Clear();
    }
}
