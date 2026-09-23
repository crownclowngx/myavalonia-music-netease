using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>仅显示本机当前账号历史；选择不会写入历史，播放意图仍经共享播放器处理。</summary>
public sealed class HistoryWorkspace : ObservableObject, IDisposable
{
    private readonly PlaybackPersistence _storage;
    private readonly ILoginUiDispatcher _ui;
    private PlaybackStorageSnapshot? _snapshot;
    private bool _closed;
    private RecentTrack? _selected;
    private string _message = "";
    public HistoryWorkspace(PlaybackPersistence storage, IPlayerSession player, ILoginUiDispatcher ui)
    {
        (_storage, _ui) = (storage, ui);
        PlayCommand = new AsyncRelayCommand(() => Run(() => Selected is { } item ? player.PlaySingleAsync(item.ToTrack(), default) : Task.CompletedTask), () => Selected is not null);
        AppendCommand = new AsyncRelayCommand(() => Run(() => Selected is { } item ? player.EnqueueAsync([QueueEntry.FromTrack(item.ToTrack(), "最近播放")], false, default) : Task.CompletedTask), () => Selected is not null);
        RetryCommand = new AsyncRelayCommand(storage.RetryAsync);
        storage.Changed += Changed; Apply(storage.Snapshot);
    }
    public ObservableCollection<RecentTrack> Rows { get; } = [];
    public RecentTrack? Selected { get => _selected; set { SetProperty(ref _selected, value); PlayCommand.NotifyCanExecuteChanged(); AppendCommand.NotifyCanExecuteChanged(); } }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string CountText => Rows.Count == 0 ? "本机还没有最近播放记录。" : $"本机最近播放 · {Rows.Count} 首";
    public IAsyncRelayCommand PlayCommand { get; }
    public IAsyncRelayCommand AppendCommand { get; }
    public IAsyncRelayCommand RetryCommand { get; }
    private async Task Run(Func<Task> work)
    {
        try { await work().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { _ui.Post(() => { if (!_closed) Message = ex.Message; }); }
    }
    private void Changed(object? sender, PlaybackStorageSnapshot snapshot) => _ui.Post(() => { if (!_closed) Apply(snapshot); });
    private void Apply(PlaybackStorageSnapshot snapshot)
    {
        if (_snapshot is { } previous && previous.Revision >= snapshot.Revision) return;
        if (!ReferenceEquals(_snapshot?.Recent, snapshot.Recent))
        { var selected = Selected?.TrackId; Rows.Clear(); foreach (var row in snapshot.Recent) Rows.Add(row); Selected = Rows.FirstOrDefault(row => row.TrackId == selected); OnPropertyChanged(nameof(CountText)); }
        _snapshot = snapshot; Message = snapshot.Message;
    }
    public void Dispose() { if (_closed) return; _closed = true; _storage.Changed -= Changed; Rows.Clear(); }
}
