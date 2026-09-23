using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MyAvaloniaManagement.PluginSdk;
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Features.Library;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>
/// 每个 Document 的搜索投影与控制意图；音频由共享协调器拥有。只接受最新搜索和快照，
/// 真正关闭通过 IDocumentLifetime 传入，Dock 隐藏/重挂不取消歌曲。
/// </summary>
public sealed class MusicWorkspace : ObservableObject, IDisposable
{
    private readonly IMusicCatalogApi _catalog;
    private readonly IMusicSessionAccessor _sessions;
    private readonly IPlayerSession _playback;
    private readonly LoginCoordinator _login;
    private readonly ILoginUiDispatcher _ui;
    private readonly IAccountImageSource? _images;
    private CancellationTokenSource? _coverCancellation;
    private Task _coverWork = Task.CompletedTask;
    private byte[]? _coverBytes;
    private long _coverGeneration;
    private int _disposed;
    private readonly CancellationTokenSource _closing = new();
    private readonly CancellationTokenRegistration _lifetime;
    private readonly object _sync = new();
    private CancellationTokenSource? _search;
    private Task _searchWork = Task.CompletedTask;
    private Task _stopWork = Task.CompletedTask;
    private long _generation;
    private long _playbackRevision = -1;
    private long _loginRevision = -1;
    private bool _closed;
    private string _keyword = "";
    private string _searchedKeyword = "";
    private string _message = "";
    private bool _loading;
    private bool _hasMore;
    private int _offset;
    private MusicTrack? _selected;
    private PlaybackSnapshot _snapshot = new(0, PlaybackState.Idle);
    public MusicWorkspace(IMusicCatalogApi catalog, IMusicSessionAccessor sessions, IPlayerSession playback,
        LoginCoordinator login, ILoginUiDispatcher ui, IDocumentLifetime lifetime, IAccountImageSource? images = null, UiPreferences? preferences = null, PlaylistBrowser? playlists = null)
    {
        (_catalog, _sessions, _playback, _login, _ui) = (catalog, sessions, playback, login, ui);
        _images = images;
        Preferences = preferences;
        Playlists = playlists;
        Queue = new(playback, ui);
        Timeline = new(playback, ui);
        ShowSearchCommand = new RelayCommand(() => Pane = 0);
        ShowQueueCommand = new RelayCommand(() => Pane = 2);
        ShowLibraryCommand = new AsyncRelayCommand(async () =>
        {
            Pane = 1;
            if (Playlists is not null && Playlists.Playlists.Count == 0) await Playlists.LoadPlaylistsAsync(true);
        });
        SearchCommand = new AsyncRelayCommand(() => SearchAsync(0, Keyword), AsyncRelayCommandOptions.AllowConcurrentExecutions);
        NextSearchPageCommand = new AsyncRelayCommand(() => SearchAsync(_offset + 30, _searchedKeyword), () => HasMore && !IsLoading);
        PreviousSearchPageCommand = new AsyncRelayCommand(() => SearchAsync(Math.Max(0, _offset - 30), _searchedKeyword), () => _offset > 0 && !IsLoading);
        PlayCommand = new AsyncRelayCommand(() => ExecutePlayerAsync(() => SelectedTrack is { } track ? _playback.PlaySingleAsync(track, _closing.Token) : Task.CompletedTask),
            () => !_closed && SelectedTrack is not null, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        AppendCommand = new AsyncRelayCommand(() => EnqueueSelectedAsync(false), () => !_closed && SelectedTrack is not null);
        PlayNextCommand = new AsyncRelayCommand(() => EnqueueSelectedAsync(true), () => !_closed && SelectedTrack is not null);
        PauseCommand = new AsyncRelayCommand(() => _playback.PauseAsync(true, _closing.Token), () => _snapshot.State == PlaybackState.Playing);
        ResumeCommand = new AsyncRelayCommand(() => _playback.PauseAsync(false, _closing.Token), () => _snapshot.Track is not null && _snapshot.State is PlaybackState.Paused or PlaybackState.Stopped or PlaybackState.Ended or PlaybackState.Failed);
        StopCommand = new AsyncRelayCommand(() => _playback.StopAsync(), () => _snapshot.State is PlaybackState.Loading or PlaybackState.Playing or PlaybackState.Paused);
        _playback.Changed += PlaybackChanged;
        _login.Changed += LoginChanged;
        ApplyPlayback(playback.Snapshot.Playback with { Revision = playback.Snapshot.Revision });
        _lifetime = lifetime.ClosingToken.Register(Close);
    }
    public ObservableCollection<MusicTrack> Tracks { get; } = [];
    public UiPreferences? Preferences { get; }
    public PlaylistBrowser? Playlists { get; }
    public QueueWorkspace Queue { get; }
    public TimelineWorkspace Timeline { get; }
    private int _pane;
    private int Pane { get => _pane; set { if (SetProperty(ref _pane, value)) { OnPropertyChanged(nameof(IsSearch)); OnPropertyChanged(nameof(IsLibrary)); OnPropertyChanged(nameof(IsQueue)); } } }
    public bool IsSearch => Pane == 0;
    public bool IsLibrary => Pane == 1;
    public bool IsQueue => Pane == 2;
    public IRelayCommand ShowSearchCommand { get; }
    public IRelayCommand ShowQueueCommand { get; }
    public IAsyncRelayCommand ShowLibraryCommand { get; }
    public bool IsPaused => _snapshot.State is PlaybackState.Paused or PlaybackState.Stopped or PlaybackState.Ended or PlaybackState.Failed;
    public string Keyword { get => _keyword; set => SetProperty(ref _keyword, value); }
    public MusicTrack? SelectedTrack { get => _selected; set { if (SetProperty(ref _selected, value)) { PlayCommand.NotifyCanExecuteChanged(); AppendCommand.NotifyCanExecuteChanged(); PlayNextCommand.NotifyCanExecuteChanged(); } } }
    public string SearchMessage { get => _message; private set => SetProperty(ref _message, value); }
    public bool IsLoading { get => _loading; private set { SetProperty(ref _loading, value); NextSearchPageCommand.NotifyCanExecuteChanged(); PreviousSearchPageCommand.NotifyCanExecuteChanged(); } }
    public bool HasMore { get => _hasMore; private set { SetProperty(ref _hasMore, value); NextSearchPageCommand.NotifyCanExecuteChanged(); } }
    public string PageText => $"第 {_offset / 30 + 1} 页";
    public string CurrentTrack => _snapshot.Track?.Display ?? "尚未选择歌曲";
    public string Album => _snapshot.Track?.Album ?? "";
    public byte[]? CoverBytes { get => _coverBytes; private set => SetProperty(ref _coverBytes, value); }
    public string PlaybackMessage => _snapshot.Message;
    public bool IsTrial => _snapshot.IsTrial;
    public string StateText => _snapshot.State switch { PlaybackState.Idle => "待播放", PlaybackState.Loading => "加载中", PlaybackState.Playing => "播放中",
        PlaybackState.Paused => "已暂停", PlaybackState.Stopped => "已停止", PlaybackState.Ended => "播放结束", _ => "播放失败" };
    public string PositionText => $"{_snapshot.PositionMs / 60000:00}:{_snapshot.PositionMs / 1000 % 60:00} / {_snapshot.DurationMs / 60000:00}:{_snapshot.DurationMs / 1000 % 60:00}";
    public double Progress => _snapshot.DurationMs > 0 ? Math.Clamp(_snapshot.PositionMs * 100d / _snapshot.DurationMs, 0, 100) : 0;
    public int Volume
    {
        get => _snapshot.Volume;
        set { if (!_closed && value != _snapshot.Volume && value is >= 0 and <= 100) _ = SetVolumeAsync(value); }
    }
    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand NextSearchPageCommand { get; }
    public IAsyncRelayCommand PreviousSearchPageCommand { get; }
    public IAsyncRelayCommand PlayCommand { get; }
    public IAsyncRelayCommand AppendCommand { get; }
    public IAsyncRelayCommand PlayNextCommand { get; }
    public IAsyncRelayCommand PauseCommand { get; }
    public IAsyncRelayCommand ResumeCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }
    private Task EnqueueSelectedAsync(bool next) => ExecutePlayerAsync(() => SelectedTrack is { } track
        ? _playback.EnqueueAsync([QueueEntry.FromTrack(track)], next, _closing.Token) : Task.CompletedTask);
    private async Task ExecutePlayerAsync(Func<Task> work)
    {
        try { await work().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { Post(() => SearchMessage = ex.Message); }
    }
    private async Task SetVolumeAsync(int volume)
    {
        try { await _playback.SetVolumeAsync(volume, _closing.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { Post(() => SearchMessage = ex.Message); }
    }
    public Task SearchAsync(int offset, string keyword)
    {
        if (_closed) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(keyword)) { SearchMessage = "请输入歌名或歌手名。"; return Task.CompletedTask; }
        if (keyword.Trim().Length > 200) { SearchMessage = "搜索关键词过长。"; return Task.CompletedTask; }
        MusicSession session;
        try { session = _sessions.Capture(); }
        catch (MusicException ex) { SearchMessage = ex.Message; return Task.CompletedTask; }
        CancellationTokenSource cancellation;
        CancellationTokenSource? old;
        long generation;
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_closed) return Task.CompletedTask;
            old = _search;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token, session.Revoked);
            _search = cancellation;
            generation = ++_generation;
            _searchWork = _searchWork.IsCompleted ? complete.Task : Task.WhenAll(_searchWork, complete.Task);
        }
        TryCancel(old);
        IsLoading = true;
        SearchMessage = "正在搜索…";
        _ = RunAsync();
        return complete.Task;
        async Task RunAsync()
        {
            try
            {
                var page = await _catalog.SearchAsync(keyword.Trim(), offset, session, cancellation.Token).ConfigureAwait(false);
                Post(() =>
                {
                    if (generation != _generation || cancellation.IsCancellationRequested || !_sessions.IsCurrent(session)) return;
                    Tracks.Clear();
                    foreach (var track in page.Tracks) Tracks.Add(track);
                    SelectedTrack = null;
                    _offset = offset;
                    _searchedKeyword = keyword.Trim();
                    HasMore = page.HasMore;
                    OnPropertyChanged(nameof(PageText));
                    SearchMessage = Tracks.Count == 0 ? "没有找到歌曲。" : $"本页 {Tracks.Count} 首";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Post(() => { if (generation == _generation) SearchMessage = ex is MusicException ? ex.Message : "搜索未完成，请重试。"; }); }
            finally
            {
                Post(() => { if (generation == _generation) IsLoading = false; });
                lock (_sync) { if (ReferenceEquals(_search, cancellation)) _search = null; }
                cancellation.Dispose();
                complete.TrySetResult();
            }
        }
    }
    private void PlaybackChanged(object? sender, PlayerSessionSnapshot snapshot) => Post(() => ApplyPlayback(snapshot.Playback with { Revision = snapshot.Revision }));
    private void ApplyPlayback(PlaybackSnapshot snapshot)
    {
        if (snapshot.Revision <= _playbackRevision) return;
        _playbackRevision = snapshot.Revision;
        var coverChanged = _snapshot.Track?.Id != snapshot.Track?.Id || _snapshot.Track?.Cover != snapshot.Track?.Cover;
        _snapshot = snapshot;
        if (coverChanged) StartCover(snapshot.Track?.Cover);
        foreach (var name in new[] { nameof(CurrentTrack), nameof(Album), nameof(PlaybackMessage), nameof(StateText), nameof(IsPaused), nameof(IsTrial), nameof(PositionText), nameof(Progress), nameof(Volume) })
            OnPropertyChanged(name);
        PauseCommand.NotifyCanExecuteChanged(); ResumeCommand.NotifyCanExecuteChanged(); StopCommand.NotifyCanExecuteChanged();
    }
    private void LoginChanged(object? sender, LoginSnapshot snapshot) => Post(() =>
    {
        if (snapshot.Revision <= _loginRevision) return;
        _loginRevision = snapshot.Revision;
        if (snapshot.Account is not null) return;
        lock (_sync) { _generation++; }
        TryCancel(_search);
        _offset = 0; _searchedKeyword = ""; OnPropertyChanged(nameof(PageText));
        Tracks.Clear(); SelectedTrack = null; HasMore = false; IsLoading = false; SearchMessage = "";
    });
    private void Post(Action action) => _ui.Post(() => { if (!_closed) action(); });
    private void StartCover(string? address)
    {
        lock (_sync)
        {
            if (_closed) return;
            _coverCancellation?.Cancel();
            _coverCancellation?.Dispose();
            _coverCancellation = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
            var token = _coverCancellation.Token;
            var generation = ++_coverGeneration;
            CoverBytes = null;
            _coverWork = LoadAsync(_coverWork);
            async Task LoadAsync(Task previous)
            {
                try
                {
                    await previous.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (_images is null || address is null) return;
                    var bytes = await _images.LoadAsync(address, token).ConfigureAwait(false);
                    Post(() => { if (generation == _coverGeneration && !token.IsCancellationRequested) CoverBytes = bytes; });
                }
                catch (Exception) { /* 封面是可选展示，失败只保留占位；绝不改变播放或账号状态。 */ }
            }
        }
    }
    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { /* 搜索可能已经在锁外完成并释放；完成的请求无需再取消。 */ }
    }
    private void Close()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_closed) return;
            _closed = true; _generation++;
            // 先发布收口任务，再在锁外取消；并发 Dispose 不会误以为关闭已经完成。
            _stopWork = completion.Task;
        }
        _closing.Cancel();
        Playlists?.Dispose();
        Queue.Dispose();
        Timeline.Dispose();
        _login.Changed -= LoginChanged;
        _playback.Changed -= PlaybackChanged;
        // V4 队列由插件容器拥有。页面只撤销自己的搜索、图片与订阅，不再停止已接纳的歌曲。
        completion.TrySetResult();
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Close();
        Task.WhenAll(_searchWork, _stopWork, _coverWork).GetAwaiter().GetResult();
        _lifetime.Dispose();
        _search?.Dispose();
        _coverCancellation?.Dispose();
        _closing.Dispose();
    }
}
