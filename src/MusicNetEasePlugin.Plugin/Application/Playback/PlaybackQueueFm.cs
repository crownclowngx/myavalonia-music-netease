using MusicNetEasePlugin.Application.Discovery;

namespace MusicNetEasePlugin.Application.Playback;

/// <summary>
/// 唯一队列写入者的 FM 衔接部分，仅处理原子接纳与导航。网络、重试和反馈分类在独立内容服务。
/// 拆分文件避免将浏览或 HTTP 细节放入队列；仍共用同一把结构锁和 AttemptId 终态防重规则。
/// </summary>
public sealed partial class PlaybackQueueCoordinator
{
    private readonly PrivateFmCoordinator? _fm;
    private Guid _fmId;
    private bool _fmAccepted;
    private PlaybackMode _fmNormalMode;
    private Task? _fmAdvance;
    private Task? _fmPrefetch;
    private bool FmActive => _fmId != Guid.Empty && _fmAccepted;
    public async Task StartFmAsync(CancellationToken ct)
    {
        if (_fm is null) return;
        var session = Capture(ct); Guid id; long revision;
        lock (_sync)
        {
            BindAccountForIntent(session, ct); EndFmCore();
            id = _fmId = _fm.Begin(session); revision = _snapshot.QueueRevision;
        }
        using var cancel = ct.Register(() => { lock (_sync) { if (_fmId == id && !_fmAccepted) EndFmCore(); } });
        var songs = await _fm.ReadAsync(id, [], true).ConfigureAwait(false);
        Task play;
        lock (_sync)
        {
            if (_closed || !_sessionOpen || _fmId != id || !_sessions.IsCurrent(session) || ct.IsCancellationRequested) return;
            // 首批在途期间用户可能只调整了普通队列顺序；这个新意图也撤销旧 FM 的接纳资格。
            if (_snapshot.QueueRevision != revision) { EndFmCore(); return; }
            if (songs.Count == 0) return;
            _fmNormalMode = _order.Mode; _fmAccepted = true;
            _order.SetMode(PlaybackMode.Sequential); _order.Replace(songs.Select(t => QueueEntry.FromTrack(t, "私人 FM")).ToArray(), 0);
            _failedCandidates.Clear(); PublishOrder(); play = StartCurrent();
        }
        Notify(explicitReplacement: true); await play.ConfigureAwait(false);
    }
    public void EndFm() { lock (_sync) { EndFmCore(); PublishOrder(); } Notify(); }
    private void EndFmCore()
    {
        if (_fmId == Guid.Empty) return;
        if (_fmAccepted) _order.SetMode(_fmNormalMode);
        _fmAccepted = false; _fmId = Guid.Empty; _fmAdvance = null; _fmPrefetch = null;
        _snapshot = _snapshot with { FmSessionId = Guid.Empty, Mode = _order.Mode, CanNext = _order.CanNext, CanPrevious = _order.CanPrevious };
        _fm?.End();
    }
    private int FmPending => _order.Entries.Count - _order.IndexOf(_order.CurrentId ?? Guid.Empty) - 1;
    private void PrefetchFmLocked()
    {
        if (_fm is null || !FmActive || FmPending >= 2 || _fmPrefetch is { IsCompleted: false } || _fm.Snapshot.State is FmContentState.Exhausted or FmContentState.Failed) return;
        var id = _fmId;
        _fmPrefetch = Task.Run(async () => { await SupplyFmAsync(id, false).ConfigureAwait(false); }); Track(_fmPrefetch);
    }
    private async Task SupplyFmAsync(Guid id, bool explicitRetry)
    {
        if (_fm is null) return;
        long[] excluded;
        lock (_sync) { if (_fmId != id || !FmActive) return; excluded = _order.Entries.Select(e => e.TrackId).ToArray(); }
        var songs = await _fm.ReadAsync(id, excluded, explicitRetry).ConfigureAwait(false);
        lock (_sync)
        {
            if (_closed || !_sessionOpen || _accountToken.IsCancellationRequested || _fmId != id || !_fm.IsCurrent(id)) return;
            var existing = _order.Entries.Select(e => e.TrackId).ToHashSet();
            var entries = songs.Where(t => existing.Add(t.Id)).Take(Math.Max(0, 10 - FmPending)).Select(t => QueueEntry.FromTrack(t, "私人 FM")).ToArray();
            if (entries.Length > 0) { _order.Add(entries, false); PublishOrder(); }
        }
        Notify();
    }
    private Task AdvanceFmLocked(bool explicitRetry)
    {
        if (_fmAdvance is { IsCompleted: false }) return _fmAdvance;
        var id = _fmId; var entry = _order.CurrentId;
        if (explicitRetry) _failedCandidates.Clear();
        var work = Task.Run(async () =>
        {
            bool need;
            lock (_sync) need = FmPending == 0;
            if (need) await SupplyFmAsync(id, explicitRetry).ConfigureAwait(false);
            Task play = Task.CompletedTask;
            lock (_sync)
            {
                if (!FmActive || _fmId != id || _order.CurrentId != entry || _closed || !_sessionOpen || _accountToken.IsCancellationRequested) return;
                var state = _snapshot.Playback.State;
                // 本轮只拥有一次导航。启动媒体后若立刻失败，应允许新的终态链继续，而不是等待自己。
                _fmAdvance = null;
                if (_order.Next(false, _failedCandidates) is not null)
                {
                    // 只保留当前及最近 99 个已消费条目，淘汰不触碰当前项与待播内容。
                    while (_order.IndexOf(_order.CurrentId!.Value) >= 100) _order.Remove(_order.Entries[0].EntryId);
                    PublishOrder();
                    play = state == PlaybackState.Paused ? StopToPending(PlaybackState.Paused) : StartCurrent();
                }
            }
            Notify(); await play.ConfigureAwait(false);
        });
        _fmAdvance = work; Track(work); return work;
    }
    public async Task DislikeFmAsync(FmFeedbackTarget target, CancellationToken ct)
    {
        if (_fm is null) return;
        lock (_sync)
        {
            if (!FmActive || target.SessionId != _fmId || target.EntryId != _order.CurrentId || target.TrackId != _order.Current?.TrackId || target.AccountEpoch != _snapshot.AccountEpoch) return;
        }
        var result = await _fm.DislikeAsync(target, ct).ConfigureAwait(false);
        Task next = Task.CompletedTask;
        lock (_sync)
        {
            // 写回执是旧目标的事实，不能因成功回包跳过已经由 Next 或自然结束选中的新曲。
            if (result.State == FmFeedbackState.Accepted && FmActive && _fmId == target.SessionId && _order.CurrentId == target.EntryId && !_accountToken.IsCancellationRequested)
                next = AdvanceFmLocked(true);
        }
        await next.ConfigureAwait(false);
    }
}
