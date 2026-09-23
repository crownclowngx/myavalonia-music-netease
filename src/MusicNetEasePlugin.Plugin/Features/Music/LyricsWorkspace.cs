using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Lyrics;

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
    public LyricsWorkspace(LyricsCoordinator lyrics, ILoginUiDispatcher ui)
    {
        (_lyrics, _ui) = (lyrics, ui);
        FollowCommand = new RelayCommand(() => { Following = true; OnPropertyChanged(nameof(CurrentLine)); });
        RetryCommand = new AsyncRelayCommand(lyrics.RetryAsync, () => _snapshot is { TrackId: > 0, Loading: false });
        lyrics.Changed += Changed; Apply(lyrics.Snapshot);
    }
    public ObservableCollection<LyricRow> Lines { get; } = [];
    public bool Following { get => _following; private set => SetProperty(ref _following, value); }
    public int CurrentLine => _snapshot?.CurrentLine ?? -1;
    public string PlainText => _snapshot?.Document.Lines.Count == 0 ? _snapshot.Document.PlainText : "";
    public string UnmatchedTranslation => _snapshot?.Document.UnmatchedTranslation ?? "";
    public string Message => _snapshot is not { } s ? "尚未选择歌曲。" : s.Loading || s.Failed ? s.Message :
        s.SyncLimited ? "试听片段：时间映射未经确认，仅显示歌词。" : s.Document.Status;
    public IRelayCommand FollowCommand { get; }
    public IAsyncRelayCommand RetryCommand { get; }
    public void StopFollowing() => Following = false;
    private void Changed(object? sender, LyricsSnapshot snapshot) => _ui.Post(() => { if (!_closed) Apply(snapshot); });
    private void Apply(LyricsSnapshot snapshot)
    {
        if (_snapshot is { } old && snapshot.Revision <= old.Revision) return;
        var previous = CurrentLine;
        if (_snapshot?.TrackId != snapshot.TrackId || _snapshot?.AccountEpoch != snapshot.AccountEpoch) Following = true;
        if (!ReferenceEquals(_snapshot?.Document, snapshot.Document))
        { Lines.Clear(); foreach (var line in snapshot.Document.Lines) Lines.Add(new(line)); }
        _snapshot = snapshot;
        if (previous != CurrentLine && previous >= 0 && previous < Lines.Count) { Lines[previous].IsCurrent = false; Lines[previous].UpdatePosition(0); }
        if (CurrentLine >= 0 && CurrentLine < Lines.Count) { Lines[CurrentLine].IsCurrent = true; Lines[CurrentLine].UpdatePosition(snapshot.PositionMs); }
        foreach (var property in new[] { nameof(CurrentLine), nameof(Message), nameof(PlainText), nameof(UnmatchedTranslation) }) OnPropertyChanged(property);
        RetryCommand.NotifyCanExecuteChanged();
    }
    public void Dispose() { if (_closed) return; _closed = true; _lyrics.Changed -= Changed; Lines.Clear(); }
}
