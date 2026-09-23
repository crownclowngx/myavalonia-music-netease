using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MyAvaloniaManagement.PluginSdk;
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Features.Library;
using MusicNetEasePlugin.Application.Lyrics;

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
    private int _disposed;
    private readonly CancellationTokenSource _closing = new();
    private readonly CancellationTokenRegistration _lifetime;
    private readonly object _sync = new();
    private CancellationTokenSource? _search;
    private Task _searchWork = Task.CompletedTask;
    private Task _stopWork = Task.CompletedTask;
    private long _generation;
    private long _loginRevision = -1;
    private bool _closed;
    private string _keyword = "";
    private string _searchedKeyword = "";
    private string _message = "";
    private bool _loading;
    private bool _hasMore;
    private int _offset;
    private MusicTrack? _selected;
    public MusicWorkspace(IMusicCatalogApi catalog, IMusicSessionAccessor sessions, IPlayerSession playback,
        LoginCoordinator login, ILoginUiDispatcher ui, IDocumentLifetime lifetime, IAccountImageSource? images = null, UiPreferences? preferences = null, PlaylistBrowser? playlists = null, LyricsCoordinator? lyrics = null, PlaybackPersistence? persistence = null)
    {
        (_catalog, _sessions, _playback, _login, _ui) = (catalog, sessions, playback, login, ui);
        Player = new(playback, ui, images, preferences);
        Preferences = preferences;
        Playlists = playlists;
        Queue = new(playback, ui);
        Lyrics = lyrics is null ? null : new(lyrics, ui, preferences);
        History = persistence is null ? null : new(persistence, playback, ui);
        ShowLibraryCommand = new AsyncRelayCommand(async () =>
        {
            Navigation.Browse(MusicBrowsePage.Library);
            if (Playlists is not null && Playlists.Playlists.Count == 0) await Playlists.LoadPlaylistsAsync(true);
        });
        SearchCommand = new AsyncRelayCommand(() => SearchAsync(0, Keyword), AsyncRelayCommandOptions.AllowConcurrentExecutions);
        NextSearchPageCommand = new AsyncRelayCommand(() => SearchAsync(_offset + 30, _searchedKeyword), () => HasMore && !IsLoading);
        PreviousSearchPageCommand = new AsyncRelayCommand(() => SearchAsync(Math.Max(0, _offset - 30), _searchedKeyword), () => _offset > 0 && !IsLoading);
        PlayCommand = new AsyncRelayCommand(() => ExecutePlayerAsync(() => SelectedTrack is { } track ? _playback.PlayNowAsync(QueueEntry.FromTrack(track), _closing.Token) : Task.CompletedTask),
            () => !_closed && SelectedTrack is not null, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        AppendCommand = new AsyncRelayCommand(() => EnqueueSelectedAsync(false), () => !_closed && SelectedTrack is not null);
        PlayNextCommand = new AsyncRelayCommand(() => EnqueueSelectedAsync(true), () => !_closed && SelectedTrack is not null);
        _login.Changed += LoginChanged;
        _lifetime = lifetime.ClosingToken.Register(Close);
    }
    public ObservableCollection<MusicTrack> Tracks { get; } = [];
    public UiPreferences? Preferences { get; }
    public PlaylistBrowser? Playlists { get; }
    public QueueWorkspace Queue { get; }
    public PlayerBarWorkspace Player { get; }
    public MusicNavigation Navigation { get; } = new();
    public LyricsWorkspace? Lyrics { get; }
    public HistoryWorkspace? History { get; }
    public IRelayCommand ShowHistoryCommand => Navigation.ShowHistoryCommand;
    public IRelayCommand ShowLyricsCommand => Navigation.ShowLyricsCommand;
    public IRelayCommand ShowSearchCommand => Navigation.ShowSearchCommand;
    public IRelayCommand ShowQueueCommand => Navigation.ShowQueueCommand;
    public IAsyncRelayCommand ShowLibraryCommand { get; }
    public string Keyword { get => _keyword; set => SetProperty(ref _keyword, value); }
    public MusicTrack? SelectedTrack { get => _selected; set { if (SetProperty(ref _selected, value)) { PlayCommand.NotifyCanExecuteChanged(); AppendCommand.NotifyCanExecuteChanged(); PlayNextCommand.NotifyCanExecuteChanged(); } } }
    public string SearchMessage { get => _message; private set => SetProperty(ref _message, value); }
    public bool IsLoading { get => _loading; private set { SetProperty(ref _loading, value); NextSearchPageCommand.NotifyCanExecuteChanged(); PreviousSearchPageCommand.NotifyCanExecuteChanged(); } }
    public bool HasMore { get => _hasMore; private set { SetProperty(ref _hasMore, value); NextSearchPageCommand.NotifyCanExecuteChanged(); } }
    public string PageText => $"第 {_offset / 30 + 1} 页";
    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand NextSearchPageCommand { get; }
    public IAsyncRelayCommand PreviousSearchPageCommand { get; }
    public IAsyncRelayCommand PlayCommand { get; }
    public IAsyncRelayCommand AppendCommand { get; }
    public IAsyncRelayCommand PlayNextCommand { get; }
    private Task EnqueueSelectedAsync(bool next) => ExecutePlayerAsync(() => SelectedTrack is { } track
        ? _playback.EnqueueAsync([QueueEntry.FromTrack(track)], next, _closing.Token) : Task.CompletedTask);
    private async Task ExecutePlayerAsync(Func<Task> work)
    {
        try { await work().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { Post(() => SearchMessage = ex.Message); }
    }
    public Task SearchAsync(int offset, string keyword)
    {
        if (_closed) return Task.CompletedTask;
        Navigation.Browse(MusicBrowsePage.Search);
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
        Player.Dispose();
        Lyrics?.Dispose();
        History?.Dispose();
        _login.Changed -= LoginChanged;
        // V4 队列由插件容器拥有。页面只撤销自己的搜索、图片与订阅，不再停止已接纳的歌曲。
        completion.TrySetResult();
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Close();
        Task.WhenAll(_searchWork, _stopWork).GetAwaiter().GetResult();
        _lifetime.Dispose();
        _search?.Dispose();
        _closing.Dispose();
    }
}
