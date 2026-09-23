using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>
/// 固定播放区的页面投影。它只把共享播放事实转换为文字、命令和当前封面，不负责搜索、导航或队列算法。
/// 定位草稿仍由 TimelineWorkspace 单独拥有；页面可见性只影响封面，不影响共享播放器。
/// 使用普通构造注入和事件订阅，避免为几个控件引入事件总线或额外的状态框架。
/// </summary>
public sealed class PlayerBarWorkspace : ObservableObject, IDisposable
{
    private readonly IPlayerSession _player;
    private readonly ILoginUiDispatcher _ui;
    private readonly IAccountImageSource? _images;
    private readonly CancellationTokenSource _closing = new();
    private CancellationTokenSource? _coverCancellation;
    private Task _coverWork = Task.CompletedTask;
    private long _coverGeneration;
    private PlayerSessionSnapshot _session = PlayerSessionSnapshot.Empty;
    private bool _closed;
    private bool _visible = true;
    private int _lastVolume = 70;
    private byte[]? _coverBytes;
    private string _commandMessage = "";
    private Guid? _dismissedRestoration;
    public PlayerBarWorkspace(IPlayerSession player, ILoginUiDispatcher ui, IAccountImageSource? images, UiPreferences? preferences)
    {
        (_player, _ui, _images, Preferences) = (player, ui, images, preferences);
        Timeline = new(player, ui); Notice = new(ui);
        Notice.PropertyChanged += NoticeChanged;
        PauseCommand = new AsyncRelayCommand(() => Run(() => player.PauseAsync(true, _closing.Token)), () => State.State == PlaybackState.Playing);
        ResumeCommand = new AsyncRelayCommand(() => Run(() => player.PauseAsync(false, _closing.Token)), () => State.Track is not null && IsPaused);
        ToggleCommand = new AsyncRelayCommand(() => IsPaused ? ResumeCommand.ExecuteAsync(null) : PauseCommand.ExecuteAsync(null), () => PauseCommand.CanExecute(null) || ResumeCommand.CanExecute(null));
        StopCommand = new AsyncRelayCommand(() => Run(player.StopAsync), () => State.State is PlaybackState.Loading or PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Failed);
        MuteCommand = new AsyncRelayCommand(() => Run(() => player.SetVolumeAsync(Volume == 0 ? _lastVolume : 0, _closing.Token)));
        DismissRestorationCommand = new RelayCommand(() => { _dismissedRestoration = _session.Restoration?.Id; OnPropertyChanged(nameof(ShowRestoration)); });
        player.Changed += Changed;
        if (preferences is not null) preferences.PropertyChanged += PreferencesChanged;
        Apply(player.Snapshot);
    }
    private PlaybackSnapshot State => _session.Playback;
    public TimelineWorkspace Timeline { get; }
    public TransientNotice Notice { get; }
    public UiPreferences? Preferences { get; }
    public string CurrentTrack => State.Track?.Display ?? "选择一首歌，开始聆听";
    public string TrackName => State.Track?.Name ?? "还没有播放歌曲";
    public string Artists => State.Track?.Artists ?? "搜索音乐或打开我的歌单";
    public string Album => State.Track?.Album ?? "";
    public long? CurrentTrackId => State.Track?.Id;
    public byte[]? CoverBytes { get => _coverBytes; private set => SetProperty(ref _coverBytes, value); }
    public bool IsPaused => State.State is PlaybackState.Paused or PlaybackState.Stopped or PlaybackState.Ended or PlaybackState.Failed or PlaybackState.Idle;
    public bool IsTrial => State.IsTrial;
    public bool IsLoading => State.State == PlaybackState.Loading;
    public bool ShowPause => !IsPaused && !IsLoading;
    public bool ShowRestoration => _session.Restoration is { } restoration && restoration.Id != _dismissedRestoration;
    public string RestorationText => _session.Restoration is { } restoration ? $"已恢复 {restoration.Count} 首 · 上次停在 {restoration.PositionMs / 60000:00}:{restoration.PositionMs / 1000 % 60:00}" : "";
    public IRelayCommand DismissRestorationCommand { get; }
    public string StateText => State.State switch { PlaybackState.Idle => "待播放", PlaybackState.Loading => "加载中", PlaybackState.Playing => "播放中",
        PlaybackState.Paused => "已暂停", PlaybackState.Stopped => "已停止", PlaybackState.Ended => "播放结束", _ => "播放失败" };
    public string PlaybackMessage => string.IsNullOrEmpty(_commandMessage) ? _session.Restoration is null ? State.Message : "" : _commandMessage;
    public string DisplayStateText => string.IsNullOrEmpty(Notice.Text) ? StateText : Notice.Text;
    private void NoticeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(DisplayStateText));
    public string PositionText => $"{State.PositionMs / 60000:00}:{State.PositionMs / 1000 % 60:00} / {State.DurationMs / 60000:00}:{State.DurationMs / 1000 % 60:00}";
    public int Volume { get => State.Volume; set { if (!_closed && value != Volume && value is >= 0 and <= 100) _ = Run(() => _player.SetVolumeAsync(value, _closing.Token)); } }
    public string VolumeText => Volume == 0 ? "已静音" : $"音量 {Volume}%";
    public IAsyncRelayCommand PauseCommand { get; }
    public IAsyncRelayCommand ResumeCommand { get; }
    public IAsyncRelayCommand ToggleCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }
    public IAsyncRelayCommand MuteCommand { get; }

    private async Task Run(Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { _ui.Post(() => { if (!_closed) { _commandMessage = ex.Message; OnPropertyChanged(nameof(PlaybackMessage)); } }); }
    }
    private void Changed(object? sender, PlayerSessionSnapshot snapshot) { if (_visible || snapshot.AccountEpoch != _session.AccountEpoch) _ui.Post(() => { if (!_closed && (_visible || snapshot.AccountEpoch != _session.AccountEpoch)) Apply(snapshot); }); }
    private void Apply(PlayerSessionSnapshot snapshot)
    {
        if (snapshot.Revision < _session.Revision) return;
        var old = _session;
        var previousText = PositionText;
        _session = snapshot;
        if (old.Restoration != snapshot.Restoration || old.AccountEpoch != snapshot.AccountEpoch)
        { OnPropertyChanged(nameof(ShowRestoration)); OnPropertyChanged(nameof(RestorationText)); }
        if (old.AccountEpoch != snapshot.AccountEpoch) { Notice.Clear(); _dismissedRestoration = null; }
        if (Volume > 0) _lastVolume = Volume;
        if (old.Playback.Track != State.Track)
        {
            foreach (var name in new[] { nameof(CurrentTrack), nameof(TrackName), nameof(Artists), nameof(Album), nameof(CurrentTrackId) }) OnPropertyChanged(name);
        }
        if (old.Playback.Track?.Cover != State.Track?.Cover || old.AccountEpoch != snapshot.AccountEpoch) RefreshCover();
        if (previousText != PositionText) OnPropertyChanged(nameof(PositionText));
        if (old.Playback.Volume != Volume) { OnPropertyChanged(nameof(Volume)); OnPropertyChanged(nameof(VolumeText)); }
        if (old.Playback.IsTrial != IsTrial) OnPropertyChanged(nameof(IsTrial));
        if (old.Playback.State != State.State)
        {
            _commandMessage = "";
            OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(IsPaused)); OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(ShowPause));
            OnPropertyChanged(nameof(DisplayStateText));
            PauseCommand.NotifyCanExecuteChanged(); ResumeCommand.NotifyCanExecuteChanged(); ToggleCommand.NotifyCanExecuteChanged(); StopCommand.NotifyCanExecuteChanged();
        }
        if (old.Playback.State != State.State || old.Playback.Message != State.Message || old.Restoration != snapshot.Restoration) OnPropertyChanged(nameof(PlaybackMessage));
    }

    /// <summary>由实际挂载的播放区报告可见性。后台播放保持不变，只撤销封面下载及迟到图片回填。</summary>
    public void SetVisible(bool visible)
    {
        if (_closed || _visible == visible) return;
        _visible = visible; if (!visible) Notice.Clear(); Timeline.SetVisible(visible); if (visible) Apply(_player.Snapshot); RefreshCover();
    }
    private void PreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(UiPreferences.ShowArtwork)) RefreshCover(); }
    private void RefreshCover()
    {
        CancelCover();
        // 每次请求的 CTS 由本次异步任务释放，不能在使用者尚未完成时从外部 Dispose。
        var generation = ++_coverGeneration;
        CoverBytes = null;
        if (!_visible || Preferences?.ShowArtwork == false || _images is null || State.Track?.Cover is not { } address) return;
        var source = _coverCancellation = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        var token = source.Token;
        var next = Load();
        _coverWork = _coverWork.IsCompleted ? next : Task.WhenAll(_coverWork, next);
        async Task Load()
        {
            try
            {
                var bytes = await _images.LoadPriorityAsync(address, token).ConfigureAwait(false);
                _ui.Post(() => { if (!_closed && generation == _coverGeneration && !token.IsCancellationRequested) CoverBytes = bytes; });
            }
            catch (Exception) { /* 封面是可选装饰。超时、损坏和取消只留下矢量占位，不改写播放结果。 */ }
            finally { source.Dispose(); }
        }
    }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _closing.Cancel(); CancelCover();
        _player.Changed -= Changed;
        if (Preferences is not null) Preferences.PropertyChanged -= PreferencesChanged;
        Timeline.Dispose(); Notice.PropertyChanged -= NoticeChanged; Notice.Dispose();
        _coverWork.GetAwaiter().GetResult();
        _coverCancellation?.Dispose(); _closing.Dispose(); CoverBytes = null;
    }
    private void CancelCover()
    {
        try { _coverCancellation?.Cancel(); }
        catch (ObjectDisposedException) { /* 已完成请求由自己的 finally 释放，重复取消无需再操作。 */ }
        _coverCancellation = null;
    }
}
