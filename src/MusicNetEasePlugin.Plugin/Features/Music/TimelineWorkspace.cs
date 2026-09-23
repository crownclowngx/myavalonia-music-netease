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
    private bool _visible = true;
    private long? _preview;
    internal Func<long, string>? PreviewLyrics { get; set; }
    public void SetVisible(bool visible) { _visible = visible; if (visible) Apply(_player.Snapshot); else Cancel(); }
    public TimelineWorkspace(IPlayerSession player, ILoginUiDispatcher ui)
    { (_player, _ui) = (player, ui); player.Changed += Changed; Apply(player.Snapshot); }
    public bool CanSeek => _snapshot.Playback.CanSeek && _snapshot.Playback.DurationMs > 0 && _snapshot.Playback.State is PlaybackState.Playing or PlaybackState.Paused;
    public string SeekReason => CanSeek ? "" : _snapshot.Playback.Track is null ? "选择歌曲后可定位" :
        _snapshot.Playback.State == PlaybackState.Loading ? "音频加载完成后可定位" : _snapshot.Playback.DurationMs <= 0 ? "时长未知，暂时无法定位" :
        !_snapshot.Playback.CanSeek ? "当前音频不支持定位" : "开始或暂停播放后可定位";
    public string PreviewText
    {
        get
        {
            var position = _editing ? (long?)_draft : _preview;
            if (position is not { } target) return SeekReason;
            var lyric = PreviewLyrics?.Invoke(target) ?? "";
            return $"{target / 60000:00}:{target / 1000 % 60:00}" + (lyric.Length == 0 ? "" : " · " + lyric);
        }
    }
    /// <summary>悬停只生成页面草稿，不改变媒体；拖动和键盘提交仍走同一条身份校验路径。</summary>
    public void PreviewAtRatio(double ratio)
    {
        if (!CanSeek || !double.IsFinite(ratio)) { ClearPreview(); return; }
        _preview = (long)(Math.Clamp(ratio, 0, 1) * Maximum); OnPropertyChanged(nameof(PreviewText));
    }
    public void ClearPreview() { _preview = null; OnPropertyChanged(nameof(PreviewText)); }
    public double Maximum => Math.Max(1, _snapshot.Playback.DurationMs - 1);
    public string PositionText => $"{(long)Position / 60000:00}:{(long)Position / 1000 % 60:00} / {_snapshot.Playback.DurationMs / 60000:00}:{_snapshot.Playback.DurationMs / 1000 % 60:00}";
    public double Position { get => _editing ? _draft : _snapshot.Playback.PositionMs; set { if (_editing && double.IsFinite(value)) { _draft = Math.Clamp(value, 0, Maximum); OnPropertyChanged(); OnPropertyChanged(nameof(PositionText)); OnPropertyChanged(nameof(DraftText)); OnPropertyChanged(nameof(PreviewText)); } } }
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
    public void Cancel() { _editing = false; ClearPreview(); OnPropertyChanged(nameof(IsEditing)); OnPropertyChanged(nameof(Position)); OnPropertyChanged(nameof(PositionText)); OnPropertyChanged(nameof(DraftText)); }
    private void Changed(object? sender, PlayerSessionSnapshot snapshot) { if (_visible) _ui.Post(() => { if (!_closed && _visible) Apply(snapshot); }); }
    private void Apply(PlayerSessionSnapshot snapshot)
    {
        if (snapshot.Revision < _snapshot.Revision) return;
        if (snapshot.CurrentEntryId != _snapshot.CurrentEntryId || snapshot.Playback.Generation != _snapshot.Playback.Generation) { _editing = false; _preview = null; }
        _snapshot = snapshot;
        if (!CanSeek) { _editing = false; _preview = null; }
        foreach (var name in new[] { nameof(Position), nameof(PositionText), nameof(Maximum), nameof(CanSeek), nameof(SeekReason), nameof(PreviewText), nameof(IsEditing), nameof(DraftText), nameof(BufferText) }) OnPropertyChanged(name);
    }
    public void Dispose() { if (_closed) return; _closed = true; _player.Changed -= Changed; _closing.Cancel(); _closing.Dispose(); }
}
