using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Features.Music;

public sealed record HistoryRow(RecentTrack Track, string GroupTitle)
{
    public long TrackId => Track.TrackId;
    public string Name => Track.Name;
    public string Artists => Track.Artists;
    public string Album => Track.Album;
    public long DurationMs => Track.DurationMs;
    public MusicTrack ToTrack() => Track.ToTrack();
}

/// <summary>仅显示本机当前账号历史；选择不会写入历史，播放意图仍经共享播放器处理。</summary>
public sealed class HistoryWorkspace : ObservableObject, IDisposable
{
    private readonly PlaybackPersistence _storage;
    private readonly ILoginUiDispatcher _ui;
    private PlaybackStorageSnapshot? _snapshot;
    private bool _closed;
    private HistoryRow? _selected;
    private readonly IPlayerSession _player;
    private long? _currentTrack;
    private string _message = "";
    private readonly TimeProvider _time;
    private DateTime _groupDate;
    public HistoryWorkspace(PlaybackPersistence storage, IPlayerSession player, ILoginUiDispatcher ui, TimeProvider? time = null)
    {
        (_storage, _ui, _player) = (storage, ui, player); _time = time ?? TimeProvider.System;
        PlayCommand = new AsyncRelayCommand(() => Run(() => Selected is { } item ? player.PlayNowAsync(QueueEntry.FromTrack(item.ToTrack(), "最近播放"), default) : Task.CompletedTask), () => Selected is not null);
        AppendCommand = new AsyncRelayCommand(() => Enqueue(false), () => Selected is not null);
        PlayNextCommand = new AsyncRelayCommand(() => Enqueue(true), () => Selected is not null);
        player.Changed += PlayerChanged; PlayerChanged(player, player.Snapshot);
        RetryCommand = new AsyncRelayCommand(storage.RetryAsync);
        storage.Changed += Changed; Apply(storage.Snapshot);
    }
    public ObservableCollection<HistoryRow> Rows { get; } = [];
    public HistoryRow? Selected { get => _selected; set { SetProperty(ref _selected, value); PlayCommand.NotifyCanExecuteChanged(); AppendCommand.NotifyCanExecuteChanged(); PlayNextCommand.NotifyCanExecuteChanged(); } }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string CountText => Rows.Count == 0 ? "本机还没有最近播放记录。" : $"本机最近播放 · {Rows.Count} 首";
    public bool IsEmpty => Rows.Count == 0;
    public IAsyncRelayCommand PlayCommand { get; }
    public IAsyncRelayCommand AppendCommand { get; }
    public IAsyncRelayCommand PlayNextCommand { get; }
    public long? CurrentTrackId { get => _currentTrack; private set => SetProperty(ref _currentTrack, value); }
    public IAsyncRelayCommand RetryCommand { get; }
    private Task Enqueue(bool next) => Run(async () =>
    {
        if (Selected is not { } item) return;
        var result = await _player.EnqueueAsync([QueueEntry.FromTrack(item.ToTrack(), "最近播放")], next, default).ConfigureAwait(false);
        _ui.Post(() => { if (!_closed && _player.Snapshot.AccountEpoch == result.AccountEpoch) Message = result.Message; });
    });
    private async Task Run(Func<Task> work)
    {
        try { await work().ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (MusicException ex) { _ui.Post(() => { if (!_closed) Message = ex.Message; }); }
    }
    private void Changed(object? sender, PlaybackStorageSnapshot snapshot) => _ui.Post(() => { if (!_closed) Apply(snapshot); });
    private void Apply(PlaybackStorageSnapshot snapshot)
    {
        var today = _time.GetLocalNow().Date;
        if (_snapshot is { } previous && previous.Revision >= snapshot.Revision && today == _groupDate) return;
        if (!ReferenceEquals(_snapshot?.Recent, snapshot.Recent) || today != _groupDate)
        {
            var selected = Selected?.TrackId; Rows.Clear(); string? previousGroup = null;
            _groupDate = today;
            // 按本机日期分段，保留原有“每首歌最近一次”的历史含义，不生成新的播放事件。
            foreach (var track in snapshot.Recent)
            {
                var date = TimeZoneInfo.ConvertTime(track.PlayedAt, _time.LocalTimeZone).Date;
                var group = date >= today ? "今天" : date == today.AddDays(-1) ? "昨天" : "更早";
                Rows.Add(new(track, group == previousGroup ? "" : group)); previousGroup = group;
            }
            Selected = Rows.FirstOrDefault(row => row.TrackId == selected); OnPropertyChanged(nameof(CountText)); OnPropertyChanged(nameof(IsEmpty));
        }
        _snapshot = snapshot; Message = snapshot.Message;
    }
    public void RefreshDate() => Apply(_storage.Snapshot);
    private void PlayerChanged(object? sender, PlayerSessionSnapshot snapshot)
    { if (CurrentTrackId != snapshot.Playback.Track?.Id) _ui.Post(() => { if (!_closed) CurrentTrackId = snapshot.Playback.Track?.Id; }); }
    public void Dispose() { if (_closed) return; _closed = true; _storage.Changed -= Changed; _player.Changed -= PlayerChanged; Rows.Clear(); }
}
