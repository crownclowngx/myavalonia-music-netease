using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Application.Appearance;

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
    public LyricsWorkspace(LyricsCoordinator lyrics, ILoginUiDispatcher ui, UiPreferences? preferences = null)
    {
        (_lyrics, _ui) = (lyrics, ui);
        Preferences = preferences;
        if (preferences is not null) preferences.PropertyChanged += PreferencesChanged;
        FollowCommand = new RelayCommand(() => { Following = true; OnPropertyChanged(nameof(CurrentLine)); });
        RetryCommand = new AsyncRelayCommand(lyrics.RetryAsync, () => _snapshot is { TrackId: > 0, Loading: false });
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
        if (visible) Apply(_lyrics.Snapshot);
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
        { Lines.Clear(); foreach (var line in snapshot.Document.Lines) Lines.Add(new(line)); }
        _snapshot = snapshot;
        if (previous != CurrentLine && previous >= 0 && previous < Lines.Count) { Lines[previous].IsCurrent = false; Lines[previous].UpdatePosition(0); }
        if (CurrentLine >= 0 && CurrentLine < Lines.Count) { Lines[CurrentLine].IsCurrent = true; Lines[CurrentLine].UpdatePosition(snapshot.PositionMs); }
        if (previous != CurrentLine || documentChanged) OnPropertyChanged(nameof(CurrentLine));
        if (documentChanged) foreach (var property in new[] { nameof(PlainText), nameof(UnmatchedTranslation), nameof(HasTranslation) }) OnPropertyChanged(property);
        if (oldMessage != Message || documentChanged || oldFailed != Failed || oldLoading != snapshot.Loading) { OnPropertyChanged(nameof(Message)); OnPropertyChanged(nameof(Failed)); RetryCommand.NotifyCanExecuteChanged(); }
    }
    public void Dispose() { if (_closed) return; _closed = true; _lyrics.Changed -= Changed; if (Preferences is not null) Preferences.PropertyChanged -= PreferencesChanged; Lines.Clear(); }
}
