using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Features.Library;

public enum LibraryEditorPage { None, Open, Create, Edit, Add, Remove, Delete, Discard, Results }

/// <summary>
/// 每个 Document 独立的编辑草稿和确认上下文。共享协调器拥有远端事实，本类只投影和表达意图。
/// 后台刷新不覆盖草稿；确认冻结账号与资源；关闭页面解除订阅，但不取消已被共享服务接纳的修改。
/// </summary>
public sealed class PlaylistEditor : ObservableObject, IDisposable
{
    private readonly MusicLibraryCoordinator _library;
    private readonly IPlaylistCatalogApi _catalog;
    private readonly PlaylistBrowser _browser;
    private readonly ILoginUiDispatcher _ui;
    private readonly CancellationTokenSource _closing = new();
    private MusicLibrarySnapshot _snapshot;
    private LibraryPlaylist? _metadata;
    private LibraryPlaylist? _baseline;
    private LibraryIntent? _confirmation;
    private LibraryEditorPage _page;
    private LibraryEditorPage _beforeDiscard;
    private string _name = "", _description = "", _idText = "", _message = "", _trackLabel = "";
    private long _targetTrack;
    private long _draftEpoch, _draftAccount;
    private long _generation;
    private int _offset;
    private bool _hasMore, _loading, _visible, _dirty, _closed, _invalidated, _returnToAdd;
    private LibraryPlaylist? _selectedTarget;
    private readonly HashSet<long> _deletedWhileHidden = [];
    private long _targetGeneration;
    private long _formVersion;
    public PlaylistEditor(MusicLibraryCoordinator library, IPlaylistCatalogApi catalog, PlaylistBrowser browser, ILoginUiDispatcher ui)
    {
        (_library, _catalog, _browser, _ui) = (library, catalog, browser, ui); _snapshot = library.Snapshot;
        OpenPanelCommand = new RelayCommand(() => Show(LibraryEditorPage.Open));
        ResultsPanelCommand = new RelayCommand(() => Page = LibraryEditorPage.Results);
        CreatePanelCommand = new RelayCommand(() => { _returnToAdd = Page == LibraryEditorPage.Add; Show(LibraryEditorPage.Create); });
        EditPanelCommand = new RelayCommand(() => Show(LibraryEditorPage.Edit), () => CanEdit);
        CloseCommand = new RelayCommand(RequestClose);
        DiscardCommand = new RelayCommand(() => { _formVersion++; _dirty = false; Page = LibraryEditorPage.None; });
        KeepEditingCommand = new RelayCommand(() => Page = _beforeDiscard);
        OpenCommand = new AsyncRelayCommand(OpenByIdAsync, () => LibraryEditRules.TryId(IdText, out _) && !IsBusy);
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => NameError.Length == 0 && !IsBusy);
        SaveNameCommand = new AsyncRelayCommand(() => SaveAsync(false), () => CanEdit && NameError.Length == 0 && !IsBusy);
        SaveDescriptionCommand = new AsyncRelayCommand(() => SaveAsync(true), () => CanEdit && _baseline?.DescriptionKnown == true && DescriptionError.Length == 0 && !IsBusy);
        SubscribeCommand = new AsyncRelayCommand(SubscribeAsync, () => _metadata?.CanSubscribe(_snapshot.AccountId) == true && !IsBusy);
        DeletePanelCommand = new RelayCommand(() => Confirm(false), () => CanEdit && !IsBusy);
        RemovePanelCommand = new RelayCommand(() => Confirm(true), () => CanEdit && _browser.SelectedTrack is not null && !IsBusy);
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => _confirmation is not null && !IsBusy);
        AddCommand = new AsyncRelayCommand(AddAsync, () => _selectedTarget is not null && _targetTrack > 0 && !IsBusy);
        MoreTargetsCommand = new AsyncRelayCommand(() => LoadTargetsAsync(false), () => _hasMore && !IsBusy);
        LikeCommand = new AsyncRelayCommand<long>(LikeAsync, id => id > 0 && !IsBusy && _snapshot.IsLiked(id).HasValue);
        AddPanelCommand = new AsyncRelayCommand<long>(ShowAddAsync, id => id > 0 && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        RecheckCommand = new AsyncRelayCommand(RecheckAsync, () => HasPending && !IsBusy);
        AcknowledgeCreateCommand = new RelayCommand(() => _library.AcknowledgeUncertainCreate(_snapshot.AccountId, _snapshot.Epoch), () => HasPendingCreate && !IsBusy);
        ReloadDraftCommand = new AsyncRelayCommand(ReloadDraftAsync, () => _baseline is not null && !IsBusy);
        _library.Changed += LibraryChanged; _browser.PropertyChanged += BrowserChanged;
    }
    public MusicLibrarySnapshot Snapshot => _snapshot;
    public LibraryPlaylist? Metadata => _metadata;
    public LibraryEditorPage Page { get => _page; private set { if (SetProperty(ref _page, value)) Notify(); } }
    public bool IsOpen => Page != LibraryEditorPage.None;
    public bool IsIdPage => Page == LibraryEditorPage.Open;
    public bool IsCreatePage => Page == LibraryEditorPage.Create;
    public bool IsEditPage => Page == LibraryEditorPage.Edit;
    public bool IsAddPage => Page == LibraryEditorPage.Add;
    public bool IsConfirmation => Page is LibraryEditorPage.Delete or LibraryEditorPage.Remove;
    public bool IsDiscardPage => Page == LibraryEditorPage.Discard;
    public bool IsNamePage => IsCreatePage || IsEditPage;
    public bool IsBusy => _snapshot.IsBusy || _loading;
    public bool CanEdit => _metadata?.CanEdit(_snapshot.AccountId) == true;
    public bool HasPending => _snapshot.Pending?.Count > 0;
    public bool HasPendingCreate => _snapshot.Pending?.ContainsKey("create") == true;
    public IReadOnlyList<string> PendingKeys => _snapshot.Pending?.Keys.Order().ToArray() ?? [];
    private string? _selectedPending;
    public string? SelectedPending { get => _selectedPending; set => SetProperty(ref _selectedPending, value); }
    public string LastResultMessage => _snapshot.LastResult?.Message ?? "尚无音乐库修改记录。";
    public string ResultItemsText => string.Join('\n', (_snapshot.LastResult?.Items ?? []).Select(i => $"歌曲 {i.TrackId}：" + (i.State switch
    { LibraryItemState.Added => "已添加", LibraryItemState.AlreadyPresent => "原本已存在", LibraryItemState.Removed => "已移除", LibraryItemState.AlreadyAbsent => "原本不存在", LibraryItemState.Rejected => "被拒绝", _ => "待核实" })));
    public string Title => Page switch { LibraryEditorPage.Open => "打开歌单", LibraryEditorPage.Create => "创建普通歌单", LibraryEditorPage.Edit => "编辑歌单", LibraryEditorPage.Add => "添加到我的歌单", LibraryEditorPage.Remove => "确认从歌单移除", LibraryEditorPage.Delete => "确认删除歌单", LibraryEditorPage.Discard => "有未保存的内容", _ => "音乐库" };
    public string SubscribeText => _metadata?.CreatorId == _snapshot.AccountId ? "我的歌单" : _metadata?.Subscribed switch { true => "取消收藏", false => "收藏歌单", null => "收藏状态待读取" };
    public string NameDraft { get => _name; set { if (SetProperty(ref _name, value)) { _dirty = true; Notify(); } } }
    public string DescriptionDraft { get => _description; set { if (SetProperty(ref _description, value)) { _dirty = true; Notify(); } } }
    public string IdText { get => _idText; set { if (SetProperty(ref _idText, value)) Notify(); } }
    public string NameError { get { try { LibraryEditRules.Name(NameDraft); return ""; } catch (ArgumentException ex) { return ex.Message; } } }
    public string DescriptionError { get { try { LibraryEditRules.Description(DescriptionDraft); return ""; } catch (ArgumentException ex) { return ex.Message; } } }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string TrackLabel => _trackLabel;
    public string ConfirmationText => _confirmation is null || _baseline is null ? "" :
        $"{_baseline.Name} · ID {_baseline.Id} · {(_baseline.TrackCount is { } count ? count + " 首" : "曲目数未知")}\n" +
        (_confirmation.Kind == LibraryOperationKind.Delete ? "将删除这个歌单。不会清空当前播放队列，也不会停止播放。" : $"将移除歌曲 {_trackLabel}（ID {_targetTrack}）。同 ID 的条目按远端规则一起移除，不影响播放队列。") + "\n此远端操作不提供撤销。";
    public ObservableCollection<LibraryPlaylist> Targets { get; } = [];
    public LibraryPlaylist? SelectedTarget { get => _selectedTarget; set { if (SetProperty(ref _selectedTarget, value)) Notify(); } }
    public IRelayCommand OpenPanelCommand { get; }
    public IRelayCommand ResultsPanelCommand { get; }
    public IRelayCommand CreatePanelCommand { get; }
    public IRelayCommand EditPanelCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public IRelayCommand DiscardCommand { get; }
    public IRelayCommand KeepEditingCommand { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand CreateCommand { get; }
    public IAsyncRelayCommand SaveNameCommand { get; }
    public IAsyncRelayCommand SaveDescriptionCommand { get; }
    public IAsyncRelayCommand SubscribeCommand { get; }
    public IRelayCommand DeletePanelCommand { get; }
    public IRelayCommand RemovePanelCommand { get; }
    public IAsyncRelayCommand ConfirmCommand { get; }
    public IAsyncRelayCommand AddCommand { get; }
    public IAsyncRelayCommand MoreTargetsCommand { get; }
    public IAsyncRelayCommand<long> LikeCommand { get; }
    public IAsyncRelayCommand<long> AddPanelCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand RecheckCommand { get; }
    public IRelayCommand AcknowledgeCreateCommand { get; }
    public IAsyncRelayCommand ReloadDraftCommand { get; }

    private void Notify()
    {
        if (_selectedPending is null || !PendingKeys.Contains(_selectedPending)) SelectedPending = PendingKeys.FirstOrDefault();
        OnPropertyChanged(nameof(PendingKeys)); OnPropertyChanged(nameof(ResultItemsText)); OnPropertyChanged(nameof(LastResultMessage));
        foreach (var name in new[] { nameof(IsOpen), nameof(IsIdPage), nameof(IsCreatePage), nameof(IsEditPage), nameof(IsAddPage), nameof(IsConfirmation), nameof(IsDiscardPage), nameof(IsNamePage), nameof(IsBusy), nameof(CanEdit), nameof(HasPending), nameof(HasPendingCreate), nameof(Title), nameof(SubscribeText), nameof(NameError), nameof(DescriptionError), nameof(ConfirmationText), nameof(TrackLabel), nameof(Snapshot), nameof(Metadata) }) OnPropertyChanged(name);
        foreach (var command in new IRelayCommand[] { OpenCommand, CreateCommand, SaveNameCommand, SaveDescriptionCommand, SubscribeCommand, DeletePanelCommand, RemovePanelCommand, ConfirmCommand, AddCommand, MoreTargetsCommand, LikeCommand, AddPanelCommand, RefreshCommand, RecheckCommand, AcknowledgeCreateCommand, EditPanelCommand, ReloadDraftCommand }) command.NotifyCanExecuteChanged();
    }
    private bool CaptureDraft()
    {
        try { var session = _library.CaptureSession(); _draftEpoch = session.Epoch; _draftAccount = session.AccountId; return true; }
        catch (Exception ex) when (ex is MusicException or ObjectDisposedException) { Message = "请先登录网易云音乐。"; return false; }
    }
    private LibraryIntent BindAccount(LibraryIntent intent) => intent with { ExpectedAccountId = _draftAccount, ExpectedEpoch = _draftEpoch };
    private void Show(LibraryEditorPage page)
    {
        if (_closed || !CaptureDraft()) return;
        _formVersion++;
        _confirmation = null; _baseline = page == LibraryEditorPage.Edit ? _metadata : null;
        _name = _baseline?.Name ?? ""; _description = _baseline?.Description ?? ""; _dirty = false;
        OnPropertyChanged(nameof(NameDraft)); OnPropertyChanged(nameof(DescriptionDraft));
        Page = page; Notify();
    }
    public void RequestClose()
    {
        _formVersion++;
        if (_dirty && Page is LibraryEditorPage.Create or LibraryEditorPage.Edit) { _beforeDiscard = Page; Page = LibraryEditorPage.Discard; }
        else { _confirmation = null; Page = LibraryEditorPage.None; }
    }
    public void SetVisible(bool visible)
    {
        if (_closed) return;
        var becameVisible = !_visible && visible; _visible = visible;
        if (becameVisible)
        {
            _ = _library.RefreshLikesAsync();
            if (_invalidated)
            {
                _invalidated = false;
                var id = _browser.Snapshot?.PlaylistId;
                _ = RefreshBrowserAsync(id, true, id.HasValue && _deletedWhileHidden.Contains(id.Value));
                _deletedWhileHidden.Clear();
            }
            else _ = LoadMetadataAsync();
        }
    }
    private void BrowserChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistBrowser.Snapshot))
        {
            if (_baseline is not null && _baseline.Id != _browser.Snapshot?.PlaylistId) { _confirmation = null; if (IsConfirmation) Page = LibraryEditorPage.None; }
            _ = LoadMetadataAsync();
        }
        if (e.PropertyName == nameof(PlaylistBrowser.SelectedTrack)) Notify();
    }
    public Task MetadataLoading { get; private set; } = Task.CompletedTask;
    private Task LoadMetadataAsync()
    {
        var generation = ++_generation; var id = _browser.Snapshot?.PlaylistId;
        if (id is null) { _metadata = null; Notify(); return Task.CompletedTask; }
        return MetadataLoading = Load();
        async Task Load()
        {
            try
            {
                var p = await _library.ReadPlaylistAsync(id.Value, _closing.Token).ConfigureAwait(false);
                _ui.Post(() => { if (_closed || generation != _generation || id != _browser.Snapshot?.PlaylistId) return; _metadata = p; Notify(); });
            }
            catch (Exception ex) when (ex is LibraryReadException or MusicException or OperationCanceledException or ObjectDisposedException)
            { _ui.Post(() => { if (!_closed && generation == _generation) { _metadata = null; Message = "歌单管理状态暂不可用，请重新读取。"; Notify(); } }); }
        }
    }
    private void LibraryChanged(object? sender, LibraryChanged change) => _ui.Post(() =>
    {
        if (_closed || change.Snapshot.Revision < _snapshot.Revision) return;
        if (change.Snapshot.AccountId != 0 && !_library.IsCurrent(change.Snapshot.AccountId, change.Snapshot.Epoch)) return;
        var accountChanged = _snapshot.Epoch != change.Snapshot.Epoch || _snapshot.AccountId != change.Snapshot.AccountId;
        _snapshot = change.Snapshot; Message = change.Snapshot.Message;
        if (accountChanged)
        {
            _generation++; _targetGeneration++; _formVersion++; _loading = false; _metadata = null; _baseline = null; _confirmation = null; _name = ""; _description = ""; _dirty = false;
            _deletedWhileHidden.Clear(); _invalidated = false;
            Targets.Clear(); SelectedTarget = null; Page = LibraryEditorPage.None;
            OnPropertyChanged(nameof(NameDraft)); OnPropertyChanged(nameof(DescriptionDraft));
        }
        Notify();
        if (change.Deleted && _baseline?.Id == change.PlaylistId) { _dirty = false; _confirmation = null; Page = LibraryEditorPage.None; }
        if (!change.DirectoryChanged) return;
        if (!_visible) { _invalidated = true; if (change.Deleted && change.PlaylistId is { } id) _deletedWhileHidden.Add(id); return; }
        var affected = change.PlaylistId ?? (change.LikesChanged && _metadata?.Kind == LibraryPlaylistKind.Liked ? _metadata.Id : null);
        _ = RefreshBrowserAsync(affected, change.DirectoryChanged, change.Deleted);
    });
    private async Task RefreshBrowserAsync(long? affected, bool directory, bool deleted)
    { if (!_closed) await _browser.RefreshLibraryChangeAsync(affected, directory, deleted).ConfigureAwait(false); }
    public async Task RefreshAsync()
    {
        try
        {
            await _library.RefreshLikesAsync(_closing.Token).ConfigureAwait(false);
            // 浏览器和可观察集合必须从 UI 线程启动。HTTP 延续不会假定存在同步上下文。
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ui.Post(async () =>
            {
                try { if (!_closed) { await _browser.RefreshLibraryChangeAsync(_browser.Snapshot?.PlaylistId, true, false); await LoadMetadataAsync(); } done.TrySetResult(); }
                catch (Exception ex) { done.TrySetException(ex); }
            });
            await done.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* 页面结束只停止等待，共享读取仍由库服务拥有，不向 UI 抛异步异常。 */ }
        catch (ObjectDisposedException) when (_closed) { }
    }
    private async Task OpenByIdAsync()
    {
        if (!LibraryEditRules.TryId(IdText, out var id)) return;
        await _browser.OpenAsync(id).ConfigureAwait(false);
        _ui.Post(() => { if (!_closed && _browser.Snapshot?.PlaylistId == id) { _dirty = false; Page = LibraryEditorPage.None; } });
    }
    private async Task<LibraryMutationResult> ExecuteAsync(LibraryIntent intent)
    {
        // 关闭当前页不会撤销远端已接纳的意图。返回后的 UI 回调另用页面寿命和账号双重校验。
        var result = await _library.ExecuteAsync(intent).ConfigureAwait(false);
        _ui.Post(() => { if (!_closed && (intent.ExpectedEpoch == 0 || _library.IsCurrent(intent.ExpectedAccountId, intent.ExpectedEpoch))) { Message = result.Message; Notify(); } });
        return result;
    }
    private async Task CreateAsync()
    {
        var form = _formVersion;
        var intent = BindAccount(new(LibraryOperationKind.Create, Text: NameDraft)); var result = await ExecuteAsync(intent).ConfigureAwait(false);
        if (result is not { Outcome: LibraryOutcome.Confirmed, Playlist: { } created }) return;
        _ui.Post(() =>
        {
            if (_closed || form != _formVersion || !_library.IsCurrent(intent.ExpectedAccountId, intent.ExpectedEpoch)) return;
            _dirty = false;
            if (_returnToAdd) { Targets.Add(created); SelectedTarget = created; Page = LibraryEditorPage.Add; _returnToAdd = false; }
            else { Page = LibraryEditorPage.None; _ = _browser.OpenAsync(created.Id); }
        });
    }
    private async Task SaveAsync(bool description)
    {
        if (_baseline is not { } baseline) return;
        var form = _formVersion;
        var intent = BindAccount(new(description ? LibraryOperationKind.Description : LibraryOperationKind.Rename, baseline.Id,
            Text: description ? DescriptionDraft : NameDraft, Baseline: baseline));
        var result = await ExecuteAsync(intent).ConfigureAwait(false);
        _ui.Post(() =>
        {
            if (_closed || form != _formVersion || !_library.IsCurrent(intent.ExpectedAccountId, intent.ExpectedEpoch) || result.Outcome != LibraryOutcome.Confirmed || result.Playlist is not { } p) return;
            _baseline = p;
            if (description) DescriptionDraft = p.Description ?? ""; else NameDraft = p.Name;
            _dirty = NameDraft != p.Name || DescriptionDraft != (p.Description ?? ""); Notify();
        });
    }
    private async Task ReloadDraftAsync()
    {
        if (_baseline is not { } p) return;
        var form = _formVersion; var account = _draftAccount; var epoch = _draftEpoch;
        try
        {
            var actual = await _library.ReadPlaylistAsync(p.Id, _closing.Token).ConfigureAwait(false);
            _ui.Post(() => { if (_closed || form != _formVersion || !_library.IsCurrent(account, epoch) || _baseline?.Id != actual.Id) return; _baseline = actual; NameDraft = actual.Name; DescriptionDraft = actual.Description ?? ""; _dirty = false; Message = "已重新载入远端内容。"; Notify(); });
        }
        catch (Exception ex) when (ex is LibraryReadException or MusicException or OperationCanceledException) { _ui.Post(() => { if (!_closed) Message = "重新载入失败，原草稿已保留。"; }); }
    }
    private Task SubscribeAsync()
    {
        if (_metadata is not { Subscribed: { } subscribed } p || !CaptureDraft()) return Task.CompletedTask;
        return ExecuteAsync(BindAccount(new(LibraryOperationKind.Subscribe, p.Id, !subscribed)));
    }
    private Task LikeAsync(long id)
    {
        if (_snapshot.IsLiked(id) is not { } liked || !CaptureDraft()) return Task.CompletedTask;
        return ExecuteAsync(BindAccount(new(LibraryOperationKind.Like, id, !liked)));
    }
    private async Task ShowAddAsync(long id)
    {
        if (!CaptureDraft()) return;
        _formVersion++;
        _targetTrack = id; _trackLabel = "歌曲 " + id; _confirmation = null; Page = LibraryEditorPage.Add;
        await LoadTargetsAsync(true).ConfigureAwait(false);
    }
    private async Task LoadTargetsAsync(bool reset)
    {
        MusicSession session;
        try { session = _library.CaptureSession(); }
        catch (MusicException) { Message = "请先登录。"; return; }
        var generation = ++_targetGeneration;
        var offset = reset ? 0 : _offset; _loading = true; Notify();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token, session.Revoked);
            var page = await _catalog.PlaylistsAsync(offset, session, linked.Token).ConfigureAwait(false);
            var targets = new List<LibraryPlaylist>();
            foreach (var item in page.Items.Where(p => p.IsOwned))
            {
                var detail = await _library.ReadPlaylistAsync(item.Id, linked.Token).ConfigureAwait(false);
                if (detail.CanEdit(session.AccountId)) targets.Add(detail);
            }
            _ui.Post(() =>
            {
                if (_closed || generation != _targetGeneration || !_library.IsCurrent(session.AccountId, session.Epoch)) return;
                if (reset) { Targets.Clear(); SelectedTarget = null; }
                foreach (var item in targets) if (Targets.All(p => p.Id != item.Id)) Targets.Add(item);
                _offset = page.NextOffset; _hasMore = page.HasMore && page.NextOffset > offset && page.NextOffset < 1000;
                Message = Targets.Count == 0 ? "已加载内容中没有可编辑的歌单，可继续加载或创建歌单。" : "选择目标后点击添加；创建歌单不会自动添加歌曲。"; Notify();
            });
        }
        catch (Exception ex) when (ex is LibraryReadException or MusicException or OperationCanceledException) { _ui.Post(() => { if (!_closed) Message = "目标歌单读取失败，可重新打开或创建歌单。"; }); }
        finally { _ui.Post(() => { if (!_closed && generation == _targetGeneration) { _loading = false; Notify(); } }); }
    }
    private Task AddAsync() => _selectedTarget is not { } p ? Task.CompletedTask : ExecuteAsync(BindAccount(new(LibraryOperationKind.AddTracks, p.Id, TrackIds: [_targetTrack])));
    private void Confirm(bool remove)
    {
        if (_metadata is not { } p || !CaptureDraft()) return;
        _formVersion++;
        _baseline = p;
        if (remove)
        {
            if (_browser.SelectedTrack is not { } row) return;
            _targetTrack = row.TrackId; _trackLabel = row.Name;
        }
        _confirmation = BindAccount(new(remove ? LibraryOperationKind.RemoveTracks : LibraryOperationKind.Delete, p.Id, TrackIds: remove ? [_targetTrack] : null, Baseline: p));
        Page = remove ? LibraryEditorPage.Remove : LibraryEditorPage.Delete; Notify();
    }
    private async Task ConfirmAsync()
    {
        if (_confirmation is not { } intent || _browser.Snapshot?.PlaylistId != intent.TargetId) return;
        var form = _formVersion;
        var result = await ExecuteAsync(intent).ConfigureAwait(false);
        _ui.Post(() => { if (_closed || form != _formVersion || !_library.IsCurrent(intent.ExpectedAccountId, intent.ExpectedEpoch)) return; if (result.Outcome == LibraryOutcome.Confirmed) { _dirty = false; _confirmation = null; Page = LibraryEditorPage.None; } });
    }
    private async Task RecheckAsync()
    {
        var key = SelectedPending; if (key is null) return;
        var account = _snapshot.AccountId; var epoch = _snapshot.Epoch;
        var result = await _library.RecheckAsync(key, _closing.Token).ConfigureAwait(false);
        _ui.Post(() => { if (!_closed && _library.IsCurrent(account, epoch)) Message = result.Message; });
    }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _generation++; _library.Changed -= LibraryChanged; _browser.PropertyChanged -= BrowserChanged;
        _closing.Cancel(); _closing.Dispose(); Targets.Clear(); _confirmation = null; _baseline = null;
    }
}
