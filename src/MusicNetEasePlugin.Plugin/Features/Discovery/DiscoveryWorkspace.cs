using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Features.Discovery;

public enum DiscoveryPage { Daily, Personal, General, Charts, ArtistSongs, ArtistAlbums, Album, Works, Fm, Recent, Week, AllTime }
public sealed record DiscoverySong(MusicTrack Track, string Detail)
{
    public long Id => Track.Id;
    public string Name => Track.Name;
    public string Artists => Track.Artists;
    public string Album => Track.Album;
}
/// <summary>页面值快照不持有账号凭据；返回导航可恢复选择和滚动，但必须仍属于同一账号代次。</summary>
internal sealed record DiscoveryViewState(DiscoveryPage Page, long Id, string Title, DiscoverySong[] Songs, MusicCard[] Cards,
    int Offset, bool More, bool Complete, DateTimeOffset FetchedAt, ArtistSongOrder Order, long? SelectedId = null, double Scroll = 0);

/// <summary>
/// 每 Document 一份的发现投影。网络端口、播放意图和控件呈现分开，取消和代次双重保护迟到 UI。
/// 当前只懒加载一个区块；旧请求即使忽略取消，也受两槽预算限制，不会因连点形成请求风暴。
/// </summary>
public sealed class DiscoveryWorkspace : ObservableObject, IDisposable
{
    private readonly IDiscoveryCatalogApi _discovery;
    private readonly IArtistAlbumApi _works;
    private readonly IRemoteHistoryApi _history;
    private readonly IMusicCatalogApi _catalog;
    private readonly IMusicSessionAccessor _sessions;
    private readonly IPlayerSession _player;
    private readonly ILoginUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _reads = new(2);
    private readonly CancellationTokenSource _closing = new();
    private readonly List<DiscoveryViewState> _back = [];
    private CancellationTokenSource? _request;
    private CancellationTokenRegistration _revoked;
    private MusicSession? _session;
    private readonly Dictionary<DiscoveryPage, DateTimeOffset> _retryAfter = [];
    private long _generation;
    private bool _closed, _busy;
    private string _message = "选择一个发现来源。";
    private DiscoveryViewState _state;
    private DiscoverySong? _selected;
    private MusicCard? _card;
    public DiscoveryWorkspace(IDiscoveryCatalogApi discovery, IArtistAlbumApi works, IRemoteHistoryApi history, IMusicCatalogApi catalog,
        IMusicSessionAccessor sessions, IPlayerSession player, ILoginUiDispatcher ui, TimeProvider? time = null)
    {
        (_discovery, _works, _history, _catalog, _sessions, _player, _ui, _time) = (discovery, works, history, catalog, sessions, player, ui, time ?? TimeProvider.System);
        _state = new(DiscoveryPage.Daily, 0, "每日推荐", [], [], 0, false, false, default, ArtistSongOrder.Hot);
        DailyCommand = new AsyncRelayCommand(() => NavigateAsync(DiscoveryPage.Daily));
        PersonalCommand = new AsyncRelayCommand(() => NavigateAsync(DiscoveryPage.Personal));
        GeneralCommand = new AsyncRelayCommand(() => NavigateAsync(DiscoveryPage.General));
        ChartsCommand = new AsyncRelayCommand(() => NavigateAsync(DiscoveryPage.Charts));
        FmCommand = new AsyncRelayCommand(() => NavigateAsync(DiscoveryPage.Fm));
        RecentCommand = new AsyncRelayCommand(() => NavigateAsync(DiscoveryPage.Recent));
        WeekCommand = new AsyncRelayCommand(() => NavigateAsync(DiscoveryPage.Week));
        AllTimeCommand = new AsyncRelayCommand(() => NavigateAsync(DiscoveryPage.AllTime));
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(_state.Page, _state.Id, false, false), AsyncRelayCommandOptions.AllowConcurrentExecutions);
        MoreCommand = new AsyncRelayCommand(() => LoadAsync(_state.Page, _state.Id, true, false), () => HasMore && !IsBusy);
        BackCommand = new AsyncRelayCommand(BackAsync, () => _back.Count > 0);
        ArtistAlbumsCommand = new AsyncRelayCommand(() => NavigateAsync(DiscoveryPage.ArtistAlbums, _state.Id), () => IsArtist);
        ArtistSongsCommand = new AsyncRelayCommand(() => NavigateAsync(DiscoveryPage.ArtistSongs, _state.Id), () => IsArtist);
        SortCommand = new AsyncRelayCommand(() => { _state = _state with { Order = _state.Order == ArtistSongOrder.Hot ? ArtistSongOrder.Time : ArtistSongOrder.Hot }; return LoadAsync(_state.Page, _state.Id, false, false); }, () => _state.Page == DiscoveryPage.ArtistSongs);
        OpenCardCommand = new AsyncRelayCommand(OpenCardAsync, () => SelectedCard is not null);
        PlayCommand = new AsyncRelayCommand(() => RunPlayer(() => Selected is { } song ? _player.PlayNowAsync(QueueEntry.FromTrack(song.Track, Title, _state.Id), _closing.Token) : Task.CompletedTask), () => Selected is not null);
        AppendCommand = new AsyncRelayCommand(() => Enqueue(false), () => Selected is not null);
        PlayNextCommand = new AsyncRelayCommand(() => Enqueue(true), () => Selected is not null);
        PlayAllCommand = new AsyncRelayCommand(() => RunPlayer(() => _player.ReplaceAsync(Songs.Select(s => QueueEntry.FromTrack(s.Track, Title, _state.Id)).ToArray(), 0, _closing.Token)), () => Songs.Count > 0 && !IsBusy);
    }
    public Func<long, Task>? OpenPlaylist { get; set; }
    public PrivateFmWorkspace? Fm { get; set; }
    public Action? ShowDiscovery { get; set; }
    public ObservableCollection<DiscoverySong> Songs { get; } = [];
    public ObservableCollection<MusicCard> Cards { get; } = [];
    public DiscoverySong? Selected { get => _selected; set { SetProperty(ref _selected, value); CommandsChanged(); } }
    public MusicCard? SelectedCard { get => _card; set { SetProperty(ref _card, value); OpenCardCommand.NotifyCanExecuteChanged(); } }
    public DiscoveryPage Page => _state.Page;
    public string Title => _state.Title;
    public bool IsFm => Page == DiscoveryPage.Fm;
    public bool IsArtist => Page is DiscoveryPage.ArtistSongs or DiscoveryPage.ArtistAlbums;
    public bool HasSongs => Songs.Count > 0;
    public bool HasCards => Cards.Count > 0;
    public bool HasMore => _state.More;
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); CommandsChanged(); } }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string PlayAllText => _state.Complete ? $"播放全部 · {Songs.Count} 首" : $"播放已加载 · {Songs.Count} 首";
    public string SortText => _state.Order == ArtistSongOrder.Hot ? "按热门 · 切换时间" : "按时间 · 切换热门";
    public double ScrollOffset { get; set; }
    public IAsyncRelayCommand DailyCommand { get; }
    public IAsyncRelayCommand PersonalCommand { get; }
    public IAsyncRelayCommand GeneralCommand { get; }
    public IAsyncRelayCommand ChartsCommand { get; }
    public IAsyncRelayCommand FmCommand { get; }
    public IAsyncRelayCommand RecentCommand { get; }
    public IAsyncRelayCommand WeekCommand { get; }
    public IAsyncRelayCommand AllTimeCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand MoreCommand { get; }
    public IAsyncRelayCommand BackCommand { get; }
    public IAsyncRelayCommand ArtistAlbumsCommand { get; }
    public IAsyncRelayCommand ArtistSongsCommand { get; }
    public IAsyncRelayCommand SortCommand { get; }
    public IAsyncRelayCommand OpenCardCommand { get; }
    public IAsyncRelayCommand PlayCommand { get; }
    public IAsyncRelayCommand PlayNextCommand { get; }
    public IAsyncRelayCommand AppendCommand { get; }
    public IAsyncRelayCommand PlayAllCommand { get; }
    public Task NavigateAsync(DiscoveryPage page, long id = 0) => LoadAsync(page, id, false, true);
    public Task OpenWorksAsync(long trackId) { ShowDiscovery?.Invoke(); return NavigateAsync(DiscoveryPage.Works, trackId); }
    public void Suspend() { Interlocked.Increment(ref _generation); Cancel(_request); IsBusy = false; }
    private MusicSession Capture()
    {
        var session = _sessions.Capture();
        if (_session?.Epoch != session.Epoch || _session.AccountId != session.AccountId)
        {
            _revoked.Unregister(); _session = session; _back.Clear(); Songs.Clear(); Cards.Clear();
            _retryAfter.Clear();
            _state = _state with { Songs = [], Cards = [], FetchedAt = default, More = false, Offset = 0 };
            _revoked = session.Revoked.Register(() => _ui.Post(() =>
            {
                if (_closed || !ReferenceEquals(_session, session)) return;
                Suspend(); _back.Clear(); Songs.Clear(); Cards.Clear(); Selected = null; SelectedCard = null;
                _state = _state with { Songs = [], Cards = [], Offset = 0, More = false, FetchedAt = default };
                Message = "账号已退出，请重新登录。"; Changed();
            }));
        }
        return session;
    }
    private async Task LoadAsync(DiscoveryPage page, long id, bool more, bool remember)
    {
        if (_closed) return;
        MusicSession session;
        try { session = Capture(); } catch (MusicException ex) { Message = ex.Message; return; }
        if (_retryAfter.TryGetValue(page, out var until) && until > _time.GetUtcNow())
        { Message = $"请求受限，请等待 {Math.Ceiling((until - _time.GetUtcNow()).TotalSeconds)} 秒再读取。"; return; }
        if (remember && _state.Page == page && _state.Id == id && _state.FetchedAt != default && _time.GetUtcNow() - _state.FetchedAt < TimeSpan.FromMinutes(5)) return;
        if (remember && _state.FetchedAt != default)
        {
            _back.Add(_state with { SelectedId = Selected?.Id ?? SelectedCard?.Id, Scroll = ScrollOffset });
            if (_back.Count > DiscoveryLimits.Navigation) _back.RemoveAt(0);
        }
        var previous = _state;
        var generation = Interlocked.Increment(ref _generation); Cancel(_request);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token, session.Revoked); _request = request;
        var offset = more && previous.Page == page && previous.Id == id ? previous.Offset : 0;
        _state = previous.Page == page && previous.Id == id ? previous : new(page, id, PageTitle(page), [], [], 0, false, false, default, previous.Order);
        if (previous.Page != page || previous.Id != id) { Songs.Clear(); Cards.Clear(); ScrollOffset = 0; Selected = null; SelectedCard = null; }
        Changed(); IsBusy = true; Message = "正在读取…";
        try
        {
            await _reads.WaitAsync(request.Token).ConfigureAwait(false);
            DiscoveryViewState result;
            var pending = ReadAsync(page, id, offset, previous.Order, session, request.Token);
            // 真实传输每次有 15 秒边界；页面还要能从不响应取消的替身/适配器退出。
            // 歌手页包含两个顺序读取，页面总预算为 30 秒。旧物理读取结束前继续占槽，不扩大并发。
            try { result = await pending.WaitAsync(TimeSpan.FromSeconds(30), _time, request.Token).ConfigureAwait(false); }
            finally { if (pending.IsCompleted) _reads.Release(); else _ = ReleaseReadWhenDone(pending); }
            _ui.Post(() =>
            {
                if (!Current()) return;
                if (more)
                {
                    var tracks = previous.Songs.Concat(result.Songs).DistinctBy(s => s.Id).Take(DiscoveryLimits.Items).ToArray();
                    var cards = previous.Cards.Concat(result.Cards).DistinctBy(c => c.Id).Take(DiscoveryLimits.Items).ToArray();
                    result = result with { Songs = tracks, Cards = cards, More = result.More && result.Offset > offset && (tracks.Length > previous.Songs.Length || cards.Length > previous.Cards.Length), Complete = false };
                }
                Apply(result); Message = page == DiscoveryPage.Fm ? "开始私人 FM 将替换当前待播队列；下一首不会提交不喜欢。"
                    : $"{result.Title} · 已加载 {result.Songs.Length + result.Cards.Length} 项 · 抓取于 {result.FetchedAt.ToLocalTime():HH:mm}" + (result.Complete ? "" : " · 接口返回范围 / 已加载部分");
                if (page is DiscoveryPage.Recent or DiscoveryPage.Week or DiscoveryPage.AllTime) Message += "。网易记录可能延迟，与本机记录范围不同。";
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _ui.Post(() =>
            {
                if (!Current()) return;
                if (ex is MusicException { Kind: MusicError.RateLimited } limited) _retryAfter[page] = _time.GetUtcNow() + (limited.RetryAfter ?? TimeSpan.FromSeconds(30));
                Message = (ex is MusicException ? ex.Message : "读取未完成，请重试。") + (Songs.Count + Cards.Count > 0 ? " 已保留旧内容，本次刷新未成功。" : "");
            });
        }
        finally { _ui.Post(() => { if (generation == _generation && !_closed) IsBusy = false; }); if (ReferenceEquals(_request, request)) _request = null; }
        bool Current() => !_closed && generation == _generation && !request.IsCancellationRequested && _sessions.IsCurrent(session);
    }
    private async Task<DiscoveryViewState> ReadAsync(DiscoveryPage page, long id, int offset, ArtistSongOrder order, MusicSession session, CancellationToken ct)
    {
        var title = PageTitle(page); CatalogPage<MusicTrack>? songs = null; CatalogPage<MusicCard>? cards = null; DiscoverySong[]? records = null;
        switch (page)
        {
            case DiscoveryPage.Daily: songs = await _discovery.DailyAsync(session, ct).ConfigureAwait(false); break;
            case DiscoveryPage.Personal: case DiscoveryPage.General: cards = await _discovery.PlaylistsAsync(page == DiscoveryPage.Personal ? RecommendationKind.Personal : RecommendationKind.General, session, ct).ConfigureAwait(false); break;
            case DiscoveryPage.Charts: cards = await _discovery.ChartsAsync(session, ct).ConfigureAwait(false); break;
            case DiscoveryPage.ArtistSongs:
                songs = await _works.SongsAsync(id, offset, order, session, ct).ConfigureAwait(false);
                try { title = (await _works.ArtistAsync(id, session, ct).ConfigureAwait(false)).Name + " · 歌曲"; }
                catch (MusicException ex) when (ex.Kind != MusicError.SignedOut) { title += "（歌手资料暂不可用）"; }
                break;
            case DiscoveryPage.ArtistAlbums: cards = await _works.AlbumsAsync(id, offset, session, ct).ConfigureAwait(false); break;
            case DiscoveryPage.Album:
                var album = await _works.AlbumAsync(id, session, ct).ConfigureAwait(false); title = album.Album.Name; songs = album.Songs; break;
            case DiscoveryPage.Works:
                var track = await _catalog.DetailAsync(id, session, ct).ConfigureAwait(false);
                // 类型决定跳转，展示文案只负责阅读；以后修改中文标签不能改变目标路由。
                cards = new(track.ArtistRefs.Select(a => new MusicCard(a.Id, a.Name, Description: "歌手", Kind: MusicCardKind.Artist)).Concat(track.AlbumRef is { } a ? [new MusicCard(a.Id, a.Name, Description: "专辑", Kind: MusicCardKind.Album)] : []).ToArray());
                title = track.Name + " · 歌手 / 专辑"; break;
            case DiscoveryPage.Recent: case DiscoveryPage.Week: case DiscoveryPage.AllTime:
                var history = await _history.ReadAsync(page == DiscoveryPage.Recent ? RemoteHistoryKind.Recent : page == DiscoveryPage.Week ? RemoteHistoryKind.Week : RemoteHistoryKind.AllTime, session, ct).ConfigureAwait(false);
                records = history.Items.Select((r, index) => new DiscoverySong(r.Track, page == DiscoveryPage.Recent ? r.PlayedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "播放时间未知"
                    : $"第 {index + 1} 名" + (r.PlayCount is { } count ? $" · {count} 次" : "") + (r.Score is { } score ? $" · 分值 {score}" : ""))).ToArray(); break;
        }
        return new(page, id, title, records ?? songs?.Items.Select(t => new DiscoverySong(t, t.Artists + " · " + t.Album)).ToArray() ?? [], cards?.Items.ToArray() ?? [],
            songs?.NextOffset ?? cards?.NextOffset ?? 0, songs?.HasMore ?? cards?.HasMore ?? false,
            page == DiscoveryPage.ArtistSongs || records is not null ? false : songs?.Complete ?? cards?.Complete ?? false, _time.GetUtcNow(), order);
    }
    private Task BackAsync()
    {
        if (_back.Count == 0) return Task.CompletedTask;
        var state = _back[^1]; _back.RemoveAt(_back.Count - 1); Suspend();
        if (_session is null || !_sessions.IsCurrent(_session)) return Task.CompletedTask;
        Apply(state);
        return _time.GetUtcNow() - state.FetchedAt > TimeSpan.FromMinutes(5) ? LoadAsync(state.Page, state.Id, false, false) : Task.CompletedTask;
    }
    private Task OpenCardAsync() => SelectedCard is not { } card ? Task.CompletedTask : Page switch
    {
        DiscoveryPage.Works => NavigateAsync(card.Kind == MusicCardKind.Artist ? DiscoveryPage.ArtistSongs : DiscoveryPage.Album, card.Id),
        DiscoveryPage.ArtistAlbums => NavigateAsync(DiscoveryPage.Album, card.Id),
        _ => OpenPlaylist?.Invoke(card.Id) ?? Task.CompletedTask
    };
    private void Apply(DiscoveryViewState state)
    {
        _state = state; Songs.Clear(); Cards.Clear(); foreach (var song in state.Songs) Songs.Add(song); foreach (var card in state.Cards) Cards.Add(card);
        Selected = Songs.FirstOrDefault(s => s.Id == state.SelectedId); SelectedCard = Cards.FirstOrDefault(c => c.Id == state.SelectedId); ScrollOffset = state.Scroll; Changed();
    }
    private async Task Enqueue(bool next) => await RunPlayer(async () =>
    {
        if (Selected is not { } song) return;
        var result = await _player.EnqueueAsync([QueueEntry.FromTrack(song.Track, Title, _state.Id)], next, _closing.Token).ConfigureAwait(false);
        _ui.Post(() => { if (!_closed && _session?.Epoch == result.AccountEpoch) Message = result.Message; });
    });
    private async Task RunPlayer(Func<Task> work)
    {
        if (_session is null || !_sessions.IsCurrent(_session) || _closed) return;
        try { await work().ConfigureAwait(false); } catch (OperationCanceledException) { } catch (MusicException ex) { _ui.Post(() => { if (!_closed) Message = ex.Message; }); }
    }
    private void Changed()
    {
        foreach (var property in new[] { nameof(Page), nameof(Title), nameof(IsFm), nameof(IsArtist), nameof(HasSongs), nameof(HasCards), nameof(HasMore), nameof(PlayAllText), nameof(SortText), nameof(ScrollOffset) }) OnPropertyChanged(property);
        CommandsChanged();
    }
    private void CommandsChanged()
    { foreach (var command in new[] { MoreCommand, BackCommand, ArtistAlbumsCommand, ArtistSongsCommand, SortCommand, PlayCommand, AppendCommand, PlayNextCommand, PlayAllCommand }) command.NotifyCanExecuteChanged(); }
    private static string PageTitle(DiscoveryPage page) => page switch
    {
        DiscoveryPage.Daily => "每日推荐", DiscoveryPage.Personal => "个性推荐歌单", DiscoveryPage.General => "通用推荐歌单", DiscoveryPage.Charts => "榜单",
        DiscoveryPage.ArtistSongs => "歌手歌曲", DiscoveryPage.ArtistAlbums => "歌手专辑", DiscoveryPage.Album => "专辑", DiscoveryPage.Fm => "私人 FM",
        DiscoveryPage.Recent => "网易最近", DiscoveryPage.Week => "网易听歌排行 · 最近一周", DiscoveryPage.AllTime => "网易听歌排行 · 所有时间", _ => "作品"
    };
    private static void Cancel(CancellationTokenSource? source) { try { source?.Cancel(); } catch (ObjectDisposedException) { } }
    private async Task ReleaseReadWhenDone(Task pending)
    { try { await pending.ConfigureAwait(false); } catch (Exception) { /* 迟到读取没有页面提交资格，只负责归还物理槽。 */ } finally { _reads.Release(); } }
    public void Dispose()
    { if (_closed) return; _closed = true; Interlocked.Increment(ref _generation); _revoked.Unregister(); _closing.Cancel(); Cancel(_request); Songs.Clear(); Cards.Clear(); _back.Clear(); _session = null; OpenPlaylist = null; ShowDiscovery = null; Fm?.Dispose(); }
}
