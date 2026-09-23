using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Features.Music;

public sealed class LyricRow(LyricLine line) : ObservableObject
{
    public LyricLine Line { get; } = line;
    public string Text => Line.Text;
    public string? Translation => Line.Translation;
    public string BeforeText { get; private set; } = line.Text;
    public string ActiveText { get; private set; } = "";
    public string AfterText { get; private set; } = "";
    private int _word = -1;
    internal void UpdatePosition(long position)
    {
        var words = Line.Words;
        var index = IsCurrent && words is { Count: > 0 } ? LyricTimeline.FindWord(words, position) : -1;
        if (_word == index) return;
        _word = index;
        BeforeText = index < 0 ? Text : string.Concat(words!.Take(index).Select(word => word.Text));
        ActiveText = index < 0 ? "" : words![index].Text;
        AfterText = index < 0 ? "" : string.Concat(words!.Skip(index + 1).Select(word => word.Text));
        OnPropertyChanged(nameof(BeforeText)); OnPropertyChanged(nameof(ActiveText)); OnPropertyChanged(nameof(AfterText));
    }
    private bool _current;
    public bool IsCurrent { get => _current; internal set => SetProperty(ref _current, value); }
}

/// <summary>Document 只保存跟随偏好与可视投影，当前行由插件共享歌词服务计算；隐藏页面不驱动播放时钟。</summary>
public sealed class LyricsWorkspace : ObservableObject, IDisposable
{
    private readonly LyricsCoordinator _lyrics;
    private readonly ILoginUiDispatcher _ui;
    private LyricsSnapshot? _snapshot;
    private bool _closed;
    private bool _following = true;
    private bool _visible;
    private readonly IPlayerSession? _player;
    private LyricRow? _selected;
    private (Guid Entry, long Generation, long Epoch)? _selectionIdentity;
    private readonly CancellationTokenSource _closing = new();
    private string _jumpMessage = "";
    public LyricsWorkspace(LyricsCoordinator lyrics, ILoginUiDispatcher ui, UiPreferences? preferences = null, IPlayerSession? player = null)
    {
        (_lyrics, _ui) = (lyrics, ui);
        _player = player;
        Preferences = preferences;
        if (preferences is not null) preferences.PropertyChanged += PreferencesChanged;
        FollowCommand = new RelayCommand(() => { Following = true; OnPropertyChanged(nameof(CurrentLine)); });
        RetryCommand = new AsyncRelayCommand(lyrics.RetryAsync, () => _snapshot is { TrackId: > 0, Loading: false });
        JumpCommand = new AsyncRelayCommand(JumpAsync, () => JumpReason.Length == 0);
        if (player is not null) player.Changed += PlaybackChanged;
        lyrics.Changed += Changed; Apply(lyrics.Snapshot);
    }
    public ObservableCollection<LyricRow> Lines { get; } = [];
    public UiPreferences? Preferences { get; }
    public bool ShowTranslation { get => Preferences?.ShowTranslation ?? true; set { if (Preferences is not null) Preferences.ShowTranslation = value; } }
    public bool HasTranslation => Lines.Any(line => !string.IsNullOrEmpty(line.Translation));
    public bool Failed => _snapshot?.Failed == true;
    public bool Following { get => _following; private set => SetProperty(ref _following, value); }
    public int CurrentLine => _snapshot?.CurrentLine ?? -1;
    public string PlainText => _snapshot?.Document.Lines.Count == 0 ? _snapshot.Document.PlainText : "";
    public string UnmatchedTranslation => _snapshot?.Document.UnmatchedTranslation ?? "";
    public string Message => _snapshot is not { } s ? "尚未选择歌曲。" : s.Loading || s.Failed ? s.Message :
        s.SyncLimited ? "试听片段：时间映射未经确认，仅显示歌词。" : s.Document.Status;
    public IRelayCommand FollowCommand { get; }
    public IAsyncRelayCommand RetryCommand { get; }
    public IAsyncRelayCommand JumpCommand { get; }
    public LyricRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            var state = _player?.Snapshot;
            _selectionIdentity = state?.CurrentEntryId is { } id ? (id, state.Playback.Generation, state.AccountEpoch) : null;
            RefreshJump();
        }
    }
    public string JumpMessage { get => _jumpMessage; private set => SetProperty(ref _jumpMessage, value); }
    public string JumpReason
    {
        get
        {
            if (Selected is null) return "请选择带时间的歌词行";
            if (_snapshot is not { SyncLimited: false, Loading: false, Failed: false } lyrics || !Lines.Contains(Selected) || Selected.Line.StartMs < 0) return "此歌词没有可信的同步时间";
            var state = _player?.Snapshot;
            if (state is null || _selectionIdentity is not { } identity || state.CurrentEntryId != identity.Entry || state.Playback.Generation != identity.Generation ||
                state.AccountEpoch != identity.Epoch || state.AccountEpoch != lyrics.AccountEpoch || state.Playback.Track?.Id != lyrics.TrackId) return "歌曲已经变化，请重新选择歌词";
            if (!state.Playback.CanSeek || state.Playback.DurationMs <= 0 || state.Playback.State is not (PlaybackState.Playing or PlaybackState.Paused)) return "当前播放状态无法定位";
            return "";
        }
    }
    /// <summary>选中时固定目标身份，提交时重新核验；迟到菜单不能把上首歌词的时间用于新歌。</summary>
    private async Task JumpAsync()
    {
        if (_closed || JumpReason.Length != 0 || Selected is not { } row || _selectionIdentity is not { } identity || _player is null) return;
        try { await _player.SeekAsync(identity.Entry, identity.Generation, row.Line.StartMs, _closing.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { _ui.Post(() => { if (!_closed) JumpMessage = ex.Message; }); }
    }
    public string PreviewTextAt(long position)
    {
        var lyrics = _lyrics.Snapshot; var state = _player?.Snapshot;
        if (lyrics.SyncLimited || lyrics.Loading || lyrics.Failed || state?.Playback.Track?.Id != lyrics.TrackId || state.AccountEpoch != lyrics.AccountEpoch) return "";
        var index = LyricTimeline.FindLine(lyrics.Document.Lines, position);
        return index >= 0 ? lyrics.Document.Lines[index].Text : "";
    }
    private void PlaybackChanged(object? sender, PlayerSessionSnapshot state)
    {
        // 只在资格变化时通知菜单，逐字/进度 tick 不产生额外界面投递。
        var qualification = (state.CurrentEntryId, state.Playback.Generation, state.AccountEpoch, state.Playback.CanSeek, state.Playback.State);
        if (_qualification == qualification) return;
        _qualification = qualification;
        if (_visible) _ui.Post(() => { if (!_closed && _visible) RefreshJump(); });
    }
    private (Guid?, long, long, bool, PlaybackState) _qualification;
    private void RefreshJump() { JumpCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(JumpReason)); }
    public void StopFollowing() => Following = false;
    private void Changed(object? sender, LyricsSnapshot snapshot)
    {
        // 隐藏时只保留最后一个不可变快照，不投递 UI tick，也不刷新整份歌词。
        // 重新可见时一次投影最新事实，音频和共享歌词协调器继续正常运行。
        if (_visible || snapshot.AccountEpoch != _snapshot?.AccountEpoch || snapshot.TrackId == 0)
            _ui.Post(() => { if (!_closed && (_visible || snapshot.AccountEpoch != _snapshot?.AccountEpoch || snapshot.TrackId == 0)) Apply(_lyrics.Snapshot); });
    }
    public void SetVisible(bool visible)
    {
        if (_closed || _visible == visible) return;
        _visible = visible;
        if (visible) { Apply(_lyrics.Snapshot); RefreshJump(); }
    }
    private void PreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(UiPreferences.ShowTranslation)) OnPropertyChanged(nameof(ShowTranslation)); }
    private void Apply(LyricsSnapshot snapshot)
    {
        if (_snapshot is { } old && snapshot.Revision <= old.Revision) return;
        var previous = CurrentLine;
        if (_snapshot?.TrackId != snapshot.TrackId || _snapshot?.AccountEpoch != snapshot.AccountEpoch) Following = true;
        var documentChanged = !ReferenceEquals(_snapshot?.Document, snapshot.Document);
        var oldMessage = Message; var oldFailed = Failed; var oldLoading = _snapshot?.Loading;
        if (documentChanged)
        { Selected = null; JumpMessage = ""; Lines.Clear(); foreach (var line in snapshot.Document.Lines) Lines.Add(new(line)); }
        _snapshot = snapshot;
        if (previous != CurrentLine && previous >= 0 && previous < Lines.Count) { Lines[previous].IsCurrent = false; Lines[previous].UpdatePosition(0); }
        if (CurrentLine >= 0 && CurrentLine < Lines.Count) { Lines[CurrentLine].IsCurrent = true; Lines[CurrentLine].UpdatePosition(snapshot.PositionMs); }
        if (previous != CurrentLine || documentChanged) OnPropertyChanged(nameof(CurrentLine));
        if (documentChanged) foreach (var property in new[] { nameof(PlainText), nameof(UnmatchedTranslation), nameof(HasTranslation) }) OnPropertyChanged(property);
        if (oldMessage != Message || documentChanged || oldFailed != Failed || oldLoading != snapshot.Loading) { OnPropertyChanged(nameof(Message)); OnPropertyChanged(nameof(Failed)); RetryCommand.NotifyCanExecuteChanged(); }
        if (documentChanged || oldFailed != Failed || oldLoading != snapshot.Loading) RefreshJump();
    }
    public void Dispose() { if (_closed) return; _closed = true; _closing.Cancel(); _lyrics.Changed -= Changed; if (_player is not null) _player.Changed -= PlaybackChanged; if (Preferences is not null) Preferences.PropertyChanged -= PreferencesChanged; Lines.Clear(); _closing.Dispose(); }
}
