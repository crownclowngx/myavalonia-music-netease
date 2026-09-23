using CommunityToolkit.Mvvm.ComponentModel;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>
/// 定位草稿属于页面，实际位置属于共享播放器。按下/键盘开始记录目标身份，松开只提交一次；
/// 播放进度不会回推拖动值，换曲/退出立即丢弃草稿。不创建自己的播放计时器。
/// </summary>
public sealed class TimelineWorkspace : ObservableObject, IDisposable
{
    private readonly IPlayerSession _player;
    private readonly ILoginUiDispatcher _ui;
    private readonly CancellationTokenSource _closing = new();
    private PlayerSessionSnapshot _snapshot = PlayerSessionSnapshot.Empty;
    private bool _editing;
    private double _draft;
    private bool _closed;
    public TimelineWorkspace(IPlayerSession player, ILoginUiDispatcher ui)
    { (_player, _ui) = (player, ui); player.Changed += Changed; Apply(player.Snapshot); }
    public bool CanSeek => _snapshot.Playback.CanSeek && _snapshot.Playback.DurationMs > 0 && _snapshot.Playback.State is PlaybackState.Playing or PlaybackState.Paused;
    public double Maximum => Math.Max(1, _snapshot.Playback.DurationMs - 1);
    public double Position { get => _editing ? _draft : _snapshot.Playback.PositionMs; set { if (_editing) { _draft = Math.Clamp(value, 0, Maximum); OnPropertyChanged(); OnPropertyChanged(nameof(DraftText)); } } }
    public bool IsEditing => _editing;
    public string DraftText => _editing ? $"定位到 {(long)_draft / 60000:00}:{(long)_draft / 1000 % 60:00}" : _snapshot.Playback.IsSeeking ? "正在定位…" : "";
    public string BufferText => _snapshot.Playback.State != PlaybackState.Loading ? "" : _snapshot.Playback.Buffer is not { } buffer ? "等待音频…" :
        buffer.TotalBytes is > 0 ? $"已下载 {buffer.BytesRead * 100 / buffer.TotalBytes}% · {buffer.BytesRead / 1024} KiB" : $"已下载 {buffer.BytesRead / 1024} KiB · 总大小未知";
    public void Begin()
    {
        if (_closed || !CanSeek || _editing) return;
        _draft = _snapshot.Playback.PositionMs; _editing = true; OnPropertyChanged(nameof(IsEditing));
    }
    public async Task CommitAsync()
    {
        if (!_editing || _closed) return;
        var entry = _snapshot.CurrentEntryId; var generation = _snapshot.Playback.Generation; var target = (long)_draft;
        Cancel();
        if (entry is not { } id) return;
        try { await _player.SeekAsync(id, generation, target, _closing.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
    public void Cancel() { _editing = false; OnPropertyChanged(nameof(IsEditing)); OnPropertyChanged(nameof(Position)); OnPropertyChanged(nameof(DraftText)); }
    private void Changed(object? sender, PlayerSessionSnapshot snapshot) => _ui.Post(() => { if (!_closed) Apply(snapshot); });
    private void Apply(PlayerSessionSnapshot snapshot)
    {
        if (snapshot.Revision < _snapshot.Revision) return;
        if (snapshot.CurrentEntryId != _snapshot.CurrentEntryId || snapshot.Playback.Generation != _snapshot.Playback.Generation) _editing = false;
        _snapshot = snapshot;
        if (!CanSeek) _editing = false;
        foreach (var name in new[] { nameof(Position), nameof(Maximum), nameof(CanSeek), nameof(IsEditing), nameof(DraftText), nameof(BufferText) }) OnPropertyChanged(name);
    }
    public void Dispose() { if (_closed) return; _closed = true; _player.Changed -= Changed; _closing.Cancel(); _closing.Dispose(); }
}
