using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Features.Music;

public sealed record QueueRow(QueueEntry Entry, bool IsCurrent)
{
    public Guid Id => Entry.EntryId;
    public string Display => (IsCurrent ? "▶ " : "") + Entry.Display;
    public string Source => Entry.Source;
}

/// <summary>队列页面投影只转发意图和展示快照；队列规则和歌曲寿命始终属于插件级播放器。</summary>
public sealed class QueueWorkspace : ObservableObject, IDisposable
{
    private readonly IPlayerSession _player;
    private readonly ILoginUiDispatcher _ui;
    private PlayerSessionSnapshot _snapshot = PlayerSessionSnapshot.Empty;
    private long _revision = -1;
    private long _queueRevision = -1;
    private bool _closed;
    private QueueRow? _selected;
    private string _message = "";
    public QueueWorkspace(IPlayerSession player, ILoginUiDispatcher ui)
    {
        (_player, _ui) = (player, ui);
        NextCommand = new AsyncRelayCommand(() => Execute(() => player.NextAsync(false, default)), () => _snapshot.CanNext);
        PreviousCommand = new AsyncRelayCommand(() => Execute(() => player.NextAsync(true, default)), () => _snapshot.CanPrevious);
        PlayCommand = new AsyncRelayCommand(() => Execute(() => Selected is { } row ? player.SelectAsync(row.Id, default) : Task.CompletedTask), () => Selected is not null);
        RemoveCommand = new AsyncRelayCommand(() => Execute(() => Selected is { } row ? player.RemoveAsync(row.Id, default) : Task.CompletedTask), () => Selected is not null);
        ClearCommand = new AsyncRelayCommand(() => Execute(player.ClearAsync), () => Rows.Count > 0);
        UpCommand = new RelayCommand(() => { if (Selected is { } row) player.Move(row.Id, -1); }, () => Selected is not null);
        DownCommand = new RelayCommand(() => { if (Selected is { } row) player.Move(row.Id, 1); }, () => Selected is not null);
        player.Changed += Changed; Apply(player.Snapshot);
    }
    public ObservableCollection<QueueRow> Rows { get; } = [];
    public string[] Modes { get; } = ["顺序播放", "列表循环", "单曲循环", "随机播放"];
    public int ModeIndex { get => (int)_snapshot.Mode; set { if (!_closed && value is >= 0 and <= 3 && value != ModeIndex) _player.SetMode((PlaybackMode)value); } }
    public QueueRow? Selected { get => _selected; set { SetProperty(ref _selected, value); UpdateCommands(); } }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string CountText => $"播放队列 · {Rows.Count} 首";
    public IAsyncRelayCommand NextCommand { get; }
    public IAsyncRelayCommand PreviousCommand { get; }
    public IAsyncRelayCommand PlayCommand { get; }
    public IAsyncRelayCommand RemoveCommand { get; }
    public IAsyncRelayCommand ClearCommand { get; }
    public IRelayCommand UpCommand { get; }
    public IRelayCommand DownCommand { get; }
    private async Task Execute(Func<Task> work)
    {
        if (_closed) return;
        try { await work().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { _ui.Post(() => { if (!_closed) Message = ex.Message; }); }
    }
    private void Changed(object? sender, PlayerSessionSnapshot snapshot) => _ui.Post(() => { if (!_closed) Apply(snapshot); });
    private void Apply(PlayerSessionSnapshot snapshot)
    {
        if (snapshot.Revision <= _revision) return;
        _revision = snapshot.Revision; _snapshot = snapshot;
        if (snapshot.QueueRevision != _queueRevision)
        {
            _queueRevision = snapshot.QueueRevision; var selected = Selected?.Id;
            Rows.Clear(); foreach (var entry in snapshot.Entries) Rows.Add(new(entry, entry.EntryId == snapshot.CurrentEntryId));
            Selected = Rows.FirstOrDefault(row => row.Id == selected);
            OnPropertyChanged(nameof(CountText));
        }
        OnPropertyChanged(nameof(ModeIndex)); UpdateCommands();
    }
    private void UpdateCommands()
    {
        NextCommand.NotifyCanExecuteChanged(); PreviousCommand.NotifyCanExecuteChanged(); PlayCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged(); ClearCommand.NotifyCanExecuteChanged(); UpCommand.NotifyCanExecuteChanged(); DownCommand.NotifyCanExecuteChanged();
    }
    public void Dispose() { if (_closed) return; _closed = true; _player.Changed -= Changed; Rows.Clear(); }
}
