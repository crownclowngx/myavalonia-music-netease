using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Music;

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
    private readonly IPlayerSession? _player;
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
    private int _filter;
    private long? _currentTrack;
    private bool _descriptionExpanded;
    private bool _failed;
    public PlaylistBrowser(IPlaylistCatalogApi api, IMusicSessionAccessor sessions, ILoginUiDispatcher ui, IPlayerSession? player = null)
    {
        (_api, _sessions, _ui) = (api, sessions, ui);
        _player = player;
        Busy = new(ui);
        RefreshCommand = new AsyncRelayCommand(() => LoadPlaylistsAsync(true), () => !_closed, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        MoreCommand = new AsyncRelayCommand(() => LoadPlaylistsAsync(false), () => HasMore && !IsLoading);
        OpenCommand = new AsyncRelayCommand(() => SelectedPlaylist is { } p ? OpenAsync(p.Id) : Task.CompletedTask, () => SelectedPlaylist is not null && !IsLoading);
        NextTracksCommand = new AsyncRelayCommand(() => LoadTracksAsync(_trackOffset + 50), () => Snapshot is { } s && _trackOffset + 50 < s.TrackIds.Count && !IsLoading);
        PreviousTracksCommand = new AsyncRelayCommand(() => LoadTracksAsync(Math.Max(0, _trackOffset - 50)), () => _trackOffset > 0 && !IsLoading);
        RetryTracksCommand = new AsyncRelayCommand(() => LoadTracksAsync(_trackOffset, true), () => Snapshot is not null && !IsLoading);
        PlayAllCommand = new AsyncRelayCommand(() => QueueAsync(true, false, false), () => CanReplace);
        PlayFromHereCommand = new AsyncRelayCommand(() => QueueAsync(true, true, false), () => CanReplace && SelectedTrack is not null);
        AppendCommand = new AsyncRelayCommand(() => QueueAsync(false, true, false), () => CanAdd);
        PlayNextCommand = new AsyncRelayCommand(() => QueueAsync(false, true, true), () => CanAdd);
        PlayNowCommand = new AsyncRelayCommand(PlayNowAsync, () => CanAdd);
        BackCommand = new RelayCommand(() => TabIndex = 0);
        if (player is not null) { player.Changed += PlayerChanged; PlayerChanged(player, player.Snapshot); }
    }
    public ObservableCollection<MusicPlaylist> Playlists { get; } = [];
    public ObservableCollection<MusicPlaylist> VisiblePlaylists { get; } = [];
    public string[] Filters { get; } = ["全部已加载", "自建", "收藏"];
    public int FilterIndex { get => _filter; set { if (value is >= 0 and <= 2 && SetProperty(ref _filter, value)) Filter(); } }
    public long? CurrentTrackId { get => _currentTrack; private set => SetProperty(ref _currentTrack, value); }
    public ObservableCollection<PlaylistTrackRow> Tracks { get; } = [];
    public MusicPlaylist? SelectedPlaylist { get => _selected; set { SetProperty(ref _selected, value); OpenCommand.NotifyCanExecuteChanged(); } }
    public PlaylistTrackRow? SelectedTrack { get => _selectedTrack; set { SetProperty(ref _selectedTrack, value); CommandsChanged(); } }
    public PlaylistTracks? Snapshot { get => _snapshot; private set { if (!SetProperty(ref _snapshot, value)) return; DescriptionExpanded = false; OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Cover)); OnPropertyChanged(nameof(Description)); OnPropertyChanged(nameof(CountText)); } }
    public string Title => Snapshot?.Name ?? "尚未打开歌单";
    public string? Cover => Snapshot?.Cover ?? Playlists.FirstOrDefault(p => p.Id == Snapshot?.PlaylistId)?.Cover;
    public string Description => string.IsNullOrWhiteSpace(Snapshot?.Description) ? "暂无歌单简介。" : Snapshot.Description;
    public bool DescriptionExpanded { get => _descriptionExpanded; set => SetProperty(ref _descriptionExpanded, value); }
    public string CountText => Snapshot is not { } s ? "" : $"{(s.IsComplete ? "共" : "已知")} {s.TrackIds.Count} 首 · 本页 {Tracks.Count} 首 · 已缓存资料 {CachedTracks} 首";
    public DelayedBusy Busy { get; }
    public bool Failed { get => _failed; private set => SetProperty(ref _failed, value); }
    public int TabIndex { get => _tabIndex; set { if (SetProperty(ref _tabIndex, value)) { OnPropertyChanged(nameof(IsList)); OnPropertyChanged(nameof(IsDetail)); } } }
    public bool IsList => TabIndex == 0;
    public bool IsDetail => TabIndex == 1;
    public bool IsEmpty => VisiblePlaylists.Count == 0;
    public string EmptyText => IsLoading ? "" : Playlists.Count == 0 ? "当前没有可显示的歌单，点击刷新歌单重新读取。" : "已加载的歌单没有匹配项，可切换筛选或继续加载。";
    public string ReplaceHint => Snapshot is { IsComplete: false } s ? s.Message : "播放全部 / 从这里播放会替换当前队列";
    public string PageText => Snapshot is null ? "" : $"第 {_trackOffset / 50 + 1} 页 · {Snapshot.TrackIds.Count} 首";
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool IsLoading { get => _loading; private set { if (SetProperty(ref _loading, value)) Busy.Set(value); OnPropertyChanged(nameof(EmptyText)); CommandsChanged(); } }
    public bool HasMore { get => _hasMore; private set { SetProperty(ref _hasMore, value); MoreCommand.NotifyCanExecuteChanged(); } }
    internal int CachedTracks { get { lock (_cache) return _cache.Count; } }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand MoreCommand { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand NextTracksCommand { get; }
    public IAsyncRelayCommand PreviousTracksCommand { get; }
    public IAsyncRelayCommand RetryTracksCommand { get; }
    public IAsyncRelayCommand PlayAllCommand { get; }
    public IAsyncRelayCommand PlayFromHereCommand { get; }
    public IAsyncRelayCommand AppendCommand { get; }
    public IAsyncRelayCommand PlayNextCommand { get; }
    public IAsyncRelayCommand PlayNowCommand { get; }
    public IRelayCommand BackCommand { get; }
    private bool CanReplace => _player is not null && !IsLoading && Snapshot is { IsComplete: true, TrackIds.Count: > 0 };
    private bool CanAdd => _player is not null && !IsLoading && SelectedTrack is not null;

    /// <summary>歌单行的播放图标只试听该曲；替换整单仍由明确的“播放全部/从这里播放”命令负责。</summary>
    private async Task PlayNowAsync()
    {
        if (_player is null || SelectedTrack is not { } row || Snapshot is not { } snapshot) return;
        try
        {
            var session = _sessions.Capture();
            if (session.Epoch != _accountEpoch) return;
            using var acceptance = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token, session.Revoked);
            await _player.PlayNowAsync(new(Guid.NewGuid(), row.TrackId, "歌单", snapshot.PlaylistId, row.Track), acceptance.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { _ui.Post(() => { if (!_closed) Message = ex.Message; }); }
    }

    /// <summary>整单操作取完整 ID 快照；资料缓存仅用于展示，不能把当前 50 行误当成完整歌单。</summary>
    public async Task QueueAsync(bool replace, bool fromSelected, bool next)
    {
        if (_player is null || Snapshot is not { } snapshot || (replace && !CanReplace)) return;
        try
        {
            var session = _sessions.Capture();
            if (session.Epoch != _accountEpoch) return;
            var selected = SelectedTrack;
            if (fromSelected && selected is null) return;
            QueueEntry Create(long id) { lock (_cache) return new(Guid.NewGuid(), id, "歌单", snapshot.PlaylistId, _cache.GetValueOrDefault(id)); }
            using var acceptance = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token, session.Revoked);
            if (replace) await _player.ReplaceAsync(snapshot.TrackIds.Select(Create).ToArray(), fromSelected ? selected!.Index : 0, acceptance.Token).ConfigureAwait(false);
            else
            {
                var result = await _player.EnqueueAsync([Create(selected!.TrackId)], next, acceptance.Token).ConfigureAwait(false);
                _ui.Post(() => { if (!_closed && _sessions.IsCurrent(session)) Message = result.Message; });
            }
        }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { _ui.Post(() => { if (!_closed) Message = ex.Message; }); }
    }

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
            Filter();
        });
    });

    public Task OpenAsync(long playlistId) => RunAsync(async (session, generation, ct) =>
    {
        Apply(session, generation, ClearTracks);
        var snapshot = await _api.PlaylistAsync(playlistId, session, ct).ConfigureAwait(false);
        var rows = await FetchRowsAsync(snapshot, 0, session, ct).ConfigureAwait(false);
        Apply(session, generation, () => { Snapshot = snapshot; SetRows(snapshot, 0, rows); TabIndex = 1; });
    });
    /// <summary>
    /// 库写入使远端目录或当前详情失效，刷新时保留浏览目标与分页，而不是复用会清空详情的用户刷新命令。
    /// 网络结果仍经过原有导航代次屏障，用户在请求期间打开其他歌单时，旧刷新不能拉回旧页面。
    /// </summary>
    public Task RefreshLibraryChangeAsync(long? affectedId, bool directory, bool deleted) => RunAsync(async (session, generation, ct) =>
    {
        var current = Snapshot; var selected = SelectedTrack?.TrackId; var offset = _trackOffset;
        var page = directory ? await _api.PlaylistsAsync(0, session, ct).ConfigureAwait(false) : null;
        PlaylistTracks? detail = null; IReadOnlyDictionary<long, MusicTrack>? rows = null;
        var unavailable = false;
        if (current is not null && current.PlaylistId == affectedId && !deleted)
        {
            try
            {
                detail = await _api.PlaylistAsync(current.PlaylistId, session, ct).ConfigureAwait(false);
                offset = detail.TrackIds.Count == 0 ? 0 : Math.Min(offset, (detail.TrackIds.Count - 1) / 50 * 50);
                rows = await FetchRowsAsync(detail, offset, session, ct).ConfigureAwait(false);
            }
            catch (MusicException ex) when (ex.Kind != MusicError.SignedOut)
            { unavailable = true; }
        }
        Apply(session, generation, () =>
        {
            if (page is not null)
            {
                var selectedPlaylistId = SelectedPlaylist?.Id;
                Playlists.Clear(); foreach (var item in page.Items.DistinctBy(p => p.Id)) Playlists.Add(item);
                _offset = page.NextOffset; HasMore = page.HasMore; Filter();
                SelectedPlaylist = Playlists.FirstOrDefault(p => p.Id == selectedPlaylistId);
            }
            if (deleted && Snapshot?.PlaylistId == affectedId) { ClearTracks(); TabIndex = 0; Message = "歌单已删除，播放队列保留。"; }
            else if (unavailable) { ClearTracks(); TabIndex = 0; Message = "歌单详情暂不可读取，目录已刷新；播放队列保留。"; }
            else if (detail is not null && Snapshot?.PlaylistId == detail.PlaylistId)
            { Snapshot = detail; SetRows(detail, offset, rows!); SelectedTrack = Tracks.FirstOrDefault(t => t.TrackId == selected) ?? Tracks.FirstOrDefault(); }
        });
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
        OnPropertyChanged(nameof(PageText)); OnPropertyChanged(nameof(CountText)); CommandsChanged();
        OnPropertyChanged(nameof(ReplaceHint));
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
        if (_accountEpoch != session.Epoch) { Playlists.Clear(); Filter(); TabIndex = 0; ClearTracks(); _offset = 0; HasMore = false; }
        _accountEpoch = session.Epoch;
        _account = session.Revoked.Register(() => _ui.Post(() =>
        {
            if (_closed || _accountEpoch != session.Epoch) return;
            Interlocked.Increment(ref _generation); Playlists.Clear(); SelectedPlaylist = null; ClearTracks();
            HasMore = false; IsLoading = false; _offset = 0; Message = "请先登录网易云音乐。";
            Filter(); TabIndex = 0;
        }));
        Apply(session, generation, () => { IsLoading = true; Failed = false; Message = ""; });
        try { await work(session, generation, operation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Apply(session, generation, () => { Failed = true; Message = ex is MusicException ? ex.Message : "歌单读取失败，请刷新重试。"; }); }
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
    private void Filter()
    {
        VisiblePlaylists.Clear();
        foreach (var item in Playlists.Where(item => FilterIndex == 0 || item.IsOwned == (FilterIndex == 1))) VisiblePlaylists.Add(item);
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(EmptyText));
    }
    private void PlayerChanged(object? sender, PlayerSessionSnapshot snapshot) { if (CurrentTrackId != snapshot.Playback.Track?.Id) _ui.Post(() => { if (!_closed) CurrentTrackId = snapshot.Playback.Track?.Id; }); }
    private void CommandsChanged()
    { MoreCommand.NotifyCanExecuteChanged(); OpenCommand.NotifyCanExecuteChanged(); NextTracksCommand.NotifyCanExecuteChanged(); PreviousTracksCommand.NotifyCanExecuteChanged(); RetryTracksCommand.NotifyCanExecuteChanged(); PlayAllCommand.NotifyCanExecuteChanged(); PlayFromHereCommand.NotifyCanExecuteChanged(); AppendCommand.NotifyCanExecuteChanged(); PlayNextCommand.NotifyCanExecuteChanged(); PlayNowCommand.NotifyCanExecuteChanged(); }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; Busy.Dispose(); Interlocked.Increment(ref _generation); _closing.Cancel(); _account.Dispose(); _closing.Dispose();
        if (_player is not null) _player.Changed -= PlayerChanged;
        Playlists.Clear(); Tracks.Clear(); lock (_cache) _cache.Clear();
    }
}
