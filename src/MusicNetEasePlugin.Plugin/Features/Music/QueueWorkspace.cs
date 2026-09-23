using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Features.Music;

public sealed class QueueRow(QueueEntry entry, bool isCurrent) : ObservableObject
{
    public QueueEntry Entry { get; private set; } = entry;
    private bool _current = isCurrent;
    public bool IsCurrent { get => _current; private set => SetProperty(ref _current, value); }
    public Guid Id => Entry.EntryId;
    public string Display => (IsCurrent ? "▶ " : "") + Entry.Display;
    public string Source => Entry.Source;
    public string Title => Entry.Track?.Name ?? $"歌曲 {Entry.TrackId}";
    public string Detail => (IsCurrent ? "当前 · " : "") + (Entry.Track?.Artists is { Length: > 0 } artist ? artist + " · " : "") + Source;
    internal void Update(QueueEntry value, bool current)
    {
        if (Entry != value) { Entry = value; OnPropertyChanged(nameof(Entry)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Source)); OnPropertyChanged(nameof(Display)); OnPropertyChanged(nameof(Detail)); }
        if (IsCurrent != current) { IsCurrent = current; OnPropertyChanged(nameof(Display)); OnPropertyChanged(nameof(Detail)); }
    }
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
    private bool _confirmClear;
    private long _clearRevision;
    public QueueWorkspace(IPlayerSession player, ILoginUiDispatcher ui)
    {
        (_player, _ui) = (player, ui);
        NextCommand = new AsyncRelayCommand(() => Execute(() => player.NextAsync(false, default)), () => _snapshot.CanNext);
        PreviousCommand = new AsyncRelayCommand(() => Execute(() => player.NextAsync(true, default)), () => _snapshot.CanPrevious);
        PlayCommand = new AsyncRelayCommand(() => Execute(() => Selected is { } row ? player.SelectAsync(row.Id, default) : Task.CompletedTask), () => Selected is not null);
        RemoveCommand = new AsyncRelayCommand(() => Execute(() => Selected is { } row ? player.RemoveAsync(row.Id, default) : Task.CompletedTask), () => Selected is not null);
        ClearCommand = new AsyncRelayCommand(() => Execute(player.ClearAsync), () => Rows.Count > 0);
        RequestClearCommand = new RelayCommand(() => { _clearRevision = _queueRevision; ConfirmClear = Rows.Count > 0; });
        CancelClearCommand = new RelayCommand(() => ConfirmClear = false);
        ConfirmClearCommand = new AsyncRelayCommand(async () =>
        {
            if (_clearRevision != _queueRevision) { ConfirmClear = false; Message = "队列已经变化，请重新确认。"; return; }
            ConfirmClear = false; await Execute(() => player.ClearIfUnchangedAsync(_clearRevision));
        }, () => ConfirmClear);
        UpCommand = new RelayCommand(() => { if (Selected is { } row) player.Move(row.Id, -1); }, () => Selected is not null);
        DownCommand = new RelayCommand(() => { if (Selected is { } row) player.Move(row.Id, 1); }, () => Selected is not null);
        UndoCommand = new RelayCommand(() =>
        {
            var undo = _snapshot.Undo;
            Message = undo is not null && player.UndoQueueChange(undo.Id, _snapshot.AccountEpoch) ? "已撤销队列操作，播放保持不变。" : "队列已经变化，这次操作无法撤销。";
        }, () => _snapshot.Undo is not null);
        player.Changed += Changed; Apply(player.Snapshot);
    }
    public ObservableCollection<QueueRow> Rows { get; } = [];
    public string[] Modes { get; } = ["顺序播放", "列表循环", "单曲循环", "随机播放"];
    public int ModeIndex { get => (int)_snapshot.Mode; set { if (!_closed && value is >= 0 and <= 3 && value != ModeIndex) _player.SetMode((PlaybackMode)value); } }
    public QueueRow? Selected { get => _selected; set { SetProperty(ref _selected, value); UpdateCommands(); } }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string CountText => $"播放队列 · {Rows.Count} 首";
    public string ButtonText => $"队列 {Rows.Count}";
    public bool IsEmpty => Rows.Count == 0;
    public long QueueRevision => _snapshot.QueueRevision;
    public long AccountEpoch => _snapshot.AccountEpoch;
    public bool CanUndo => _snapshot.Undo is not null;
    public string UndoText => _snapshot.Undo?.Description ?? "撤销";
    public IRelayCommand UndoCommand { get; }
    public bool CommitMove(QueueMoveIntent intent)
    {
        if (_closed) return false;
        var moved = _player.MoveTo(intent);
        Message = moved ? "已调整队列顺序。" : "队列已经变化或位置未改变，未执行移动。";
        return moved;
    }
    public void CancelMove() { if (!_closed) Message = "队列已经变化，已取消拖动，请重新调整。"; }
    public string OrderHint => ModeIndex == 3 ? "队列顺序 · 随机模式的实际下一首由播放器选择" : "队列顺序 · 当前项之后继续播放";
    public bool ConfirmClear { get => _confirmClear; private set { if (SetProperty(ref _confirmClear, value)) ConfirmClearCommand.NotifyCanExecuteChanged(); } }
    public string ClearText => $"清空 {Rows.Count} 首并停止播放？";
    public QueueRow? Current => Rows.FirstOrDefault(row => row.IsCurrent);
    public IRelayCommand RequestClearCommand { get; }
    public IRelayCommand CancelClearCommand { get; }
    public IAsyncRelayCommand ConfirmClearCommand { get; }
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
    private void Changed(object? sender, PlayerSessionSnapshot snapshot)
    { if (snapshot.QueueRevision != _queueRevision || snapshot.Undo != _snapshot.Undo || snapshot.CanNext != _snapshot.CanNext || snapshot.CanPrevious != _snapshot.CanPrevious) _ui.Post(() => { if (!_closed) Apply(snapshot); }); }
    private void Apply(PlayerSessionSnapshot snapshot)
    {
        if (snapshot.Revision <= _revision) return;
        _revision = snapshot.Revision; _snapshot = snapshot;
        if (snapshot.QueueRevision != _queueRevision)
        {
            _queueRevision = snapshot.QueueRevision; var selected = Selected?.Id;
            // 完整换单允许重建；小操作按 EntryId 复用行，避免选中、滚动和图标状态全部抖动。
            var existing = Rows.ToDictionary(row => row.Id);
            var wanted = snapshot.Entries.Select(entry => entry.EntryId).ToHashSet();
            if (existing.Keys.Count(id => !wanted.Contains(id)) > Math.Max(64, Rows.Count / 4)) Rows.Clear();
            else for (var i = Rows.Count - 1; i >= 0; i--) if (!wanted.Contains(Rows[i].Id)) Rows.RemoveAt(i);
            for (var i = 0; i < snapshot.Entries.Count; i++)
            {
                var entry = snapshot.Entries[i];
                var row = existing.GetValueOrDefault(entry.EntryId) ?? new(entry, false);
                row.Update(entry, entry.EntryId == snapshot.CurrentEntryId);
                if (i < Rows.Count && ReferenceEquals(Rows[i], row)) continue;
                var oldIndex = existing.ContainsKey(entry.EntryId) && i < Rows.Count ? Rows.IndexOf(row) : -1;
                if (oldIndex >= 0) Rows.Move(oldIndex, i); else Rows.Insert(i, row);
            }
            Selected = Rows.FirstOrDefault(row => row.Id == selected);
            foreach (var property in new[] { nameof(CountText), nameof(ButtonText), nameof(ClearText), nameof(IsEmpty), nameof(Current) }) OnPropertyChanged(property);
        }
        OnPropertyChanged(nameof(QueueRevision)); OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(UndoText));
        OnPropertyChanged(nameof(ModeIndex)); OnPropertyChanged(nameof(OrderHint)); UpdateCommands();
    }
    private void UpdateCommands()
    {
        NextCommand.NotifyCanExecuteChanged(); PreviousCommand.NotifyCanExecuteChanged(); PlayCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged(); ClearCommand.NotifyCanExecuteChanged(); UpCommand.NotifyCanExecuteChanged(); DownCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }
    public void Dispose() { if (_closed) return; _closed = true; _player.Changed -= Changed; Rows.Clear(); }
}
