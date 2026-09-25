using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;
using Xunit;
namespace MusicNetEasePlugin.Tests;

public sealed class PrivateFmTests
{
    [Fact, Trait("V8", "F01,F02,F06,P03")]
    public async Task FM显式开始共享播放器下一首及重复终态只推进一次()
    {
        await using var f = new FmFixture(); f.Queue.SetMode(PlaybackMode.Shuffle); await f.Queue.StartFmAsync(default);
        Assert.NotEqual(Guid.Empty, f.Queue.Snapshot.FmSessionId); Assert.Equal(PlaybackMode.Sequential, f.Queue.Snapshot.Mode); Assert.False(f.Queue.Snapshot.CanPrevious);
        var generation = f.Queue.Snapshot.Playback.Generation; f.Audio.Emit(generation, PlaybackState.Ended); f.Audio.Emit(generation, PlaybackState.Ended);
        await DiscoveryTestWait.Until(() => f.Queue.Snapshot.Playback.Track?.Id == 11 && f.Queue.Snapshot.Playback.State == PlaybackState.Playing);
        Assert.Equal(2, f.Audio.Opened.Count); await f.Queue.NextAsync(false, default); Assert.Equal(12, f.Queue.Snapshot.Playback.Track?.Id); Assert.Empty(f.Api.Writes);
        f.Queue.EndFm(); Assert.Equal(PlaybackMode.Shuffle, f.Queue.Snapshot.Mode); Assert.Equal(Guid.Empty, f.Queue.Snapshot.FmSessionId);
    }
    [Fact, Trait("V8", "F01,C03")]
    public async Task FM首批迟到不能替换期间用户选择的新队列()
    {
        await using var f = new FmFixture(); var response = new TaskCompletionSource<IReadOnlyList<MusicTrack>>(); f.Api.Content = (_, _) => response.Task;
        await f.Queue.PlayNowAsync(QueueEntry.FromTrack(DiscoveryFake.Track(1)), default); var start = f.Queue.StartFmAsync(default); await DiscoveryTestWait.Until(() => f.Api.Reads == 1);
        await f.Queue.PlayNowAsync(QueueEntry.FromTrack(DiscoveryFake.Track(2)), default); response.SetResult([DiscoveryFake.Track(10)]); await start;
        Assert.Equal(2, f.Queue.Snapshot.Playback.Track!.Id); Assert.Equal(Guid.Empty, f.Queue.Snapshot.FmSessionId);
    }
    [Fact, Trait("V8", "F03,A06")]
    public async Task 空批三轮和两次等待有界耗尽后进度不能重新补取()
    {
        var time = new FakeTimeProvider(); await using var f = new FmFixture(time); f.Api.Content = (_, _) => Task.FromResult<IReadOnlyList<MusicTrack>>([]);
        var id = f.Fm.Begin(f.Sessions.Capture()); var read = f.Fm.ReadAsync(id, []);
        await DiscoveryTestWait.Until(() => f.Fm.WaitingDelayMilliseconds == 500); time.Advance(TimeSpan.FromMilliseconds(500));
        await DiscoveryTestWait.Until(() => f.Fm.WaitingDelayMilliseconds == 1500); time.Advance(TimeSpan.FromMilliseconds(1500)); await read;
        Assert.Equal(3, f.Api.Reads); Assert.Equal(1, f.Api.Maximum); Assert.Equal(FmContentState.Exhausted, f.Fm.Snapshot.State);
        await f.Fm.ReadAsync(id, []); Assert.Equal(3, f.Api.Reads);
        TestEvidence.Write("v8-fm-budget.json", new { schemaVersion = 1, realHost = false, requests = f.Fm.LastRead.Requests, elapsedMs = f.Fm.LastRead.ElapsedMs, delays = f.Fm.LastRead.Delays });
    }
    [Fact, Trait("V8", "F03,C06")]
    public async Task 二十秒总预算和关闭不受忽略取消的读取阻塞()
    {
        var time = new FakeTimeProvider(); await using var f = new FmFixture(time); var pending = new TaskCompletionSource<IReadOnlyList<MusicTrack>>(); f.Api.Content = (_, _) => pending.Task;
        var id = f.Fm.Begin(f.Sessions.Capture()); var read = f.Fm.ReadAsync(id, []); await DiscoveryTestWait.Until(() => f.Api.Reads == 1);
        time.Advance(TimeSpan.FromSeconds(20)); await read.WaitAsync(TimeSpan.FromSeconds(2)); Assert.Equal(FmContentState.Exhausted, f.Fm.Snapshot.State);
        f.Fm.End(); pending.SetResult([DiscoveryFake.Track(10)]); Assert.Equal(Guid.Empty, f.Fm.Snapshot.SessionId);
    }
    [Fact, Trait("V8", "F03,F04")]
    public async Task 单批截断与去重以及网络错误立即停止补取()
    {
        await using var f = new FmFixture(); f.Api.Content = (_, _) => Task.FromResult<IReadOnlyList<MusicTrack>>(Enumerable.Range(1, 30).Select(i => DiscoveryFake.Track(i / 2 + 1)).ToArray());
        var id = f.Fm.Begin(f.Sessions.Capture()); var tracks = await f.Fm.ReadAsync(id, [1, 2]); Assert.Equal(10, tracks.Count); Assert.Equal(10, tracks.Select(t => t.Id).Distinct().Count()); Assert.DoesNotContain(tracks, t => t.Id is 1 or 2);
        f.Api.Content = (_, _) => throw new MusicException(MusicError.Network, "断网"); await f.Fm.ReadAsync(id, []); Assert.Equal(2, f.Api.Reads); Assert.Equal(FmContentState.Failed, f.Fm.Snapshot.State);
    }
    [Fact, Trait("V8", "F04")]
    public async Task 不可播放候选跨批次最多二十个不重置失败链()
    {
        await using var f = new FmFixture(); f.Catalog.Resource = (id, _) => Task.FromResult(new PlaybackResource(id, null, "mp3", "standard", false, null));
        await f.Queue.StartFmAsync(default);
        await DiscoveryTestWait.Until(() => f.Catalog.Resolves >= 20);
        Assert.Equal(20, f.Catalog.Resolves); Assert.Equal(PlaybackState.Failed, f.Queue.Snapshot.Playback.State); Assert.Empty(f.Audio.Opened);
        TestEvidence.Write("v8-fm-failures.json", new { schemaVersion = 1, realHost = false, candidates = f.Catalog.Resolves, batches = f.Api.Reads, mediaOpens = f.Audio.Opened.Count });
    }
    [Fact, Trait("V8", "F05,F10,C05")]
    public async Task 暂停下一首保持静默停止与最后页关闭撤销FM资格()
    {
        await using var f = new FmFixture(requireDocument: true); using var lifetime = new MusicLifetime(); using var owner = new MusicPlaybackLifetime(f.Queue); using var lease = new MusicPlaybackLease(owner, lifetime);
        await f.Queue.StartFmAsync(default); await f.Queue.PauseAsync(true, default); var before = f.Audio.Opened.Count;
        await f.Queue.NextAsync(false, default); Assert.Equal(PlaybackState.Paused, f.Queue.Snapshot.Playback.State); Assert.Equal(before, f.Audio.Opened.Count);
        await f.Queue.NextAsync(true, default); Assert.Equal(11, f.Queue.Snapshot.Playback.Track!.Id);
        lifetime.Close(); await owner.Pending; Assert.Equal(Guid.Empty, f.Fm.Snapshot.SessionId); Assert.Equal(Guid.Empty, f.Queue.Snapshot.FmSessionId); Assert.Null(f.Audio.Current);
    }
    [Fact, Trait("V8", "F07,F09,C04")]
    public async Task 同目标重复反馈只发送一次迟到成功不跳过新曲()
    {
        await using var f = new FmFixture(); await f.Queue.StartFmAsync(default); var target = f.Target(); var receipt = new TaskCompletionSource<FmFeedbackResult>(); f.Api.Feedback = (_, _) => receipt.Task;
        var first = f.Queue.DislikeFmAsync(target, default); await f.Queue.DislikeFmAsync(target, default); Assert.Single(f.Api.Writes);
        await f.Queue.NextAsync(false, default); var next = f.Queue.Snapshot.CurrentEntryId; receipt.SetResult(new(FmFeedbackState.Accepted, "已反馈")); await first;
        Assert.Equal(next, f.Queue.Snapshot.CurrentEntryId); Assert.Equal(target.TrackId, Assert.Single(f.Api.Writes));
        await f.Queue.DislikeFmAsync(target, default); Assert.Single(f.Api.Writes);
    }
    [Theory, InlineData(FmFeedbackState.Uncertain), InlineData(FmFeedbackState.Rejected), Trait("V8", "F08,F09")]
    public async Task 不确定和拒绝不换曲不自动重发但普通下一首仍可用(FmFeedbackState result)
    {
        await using var f = new FmFixture(); f.Api.Feedback = (_, _) => Task.FromResult(new FmFeedbackResult(result, "反馈结果")); await f.Queue.StartFmAsync(default); var target = f.Target();
        await f.Queue.DislikeFmAsync(target, default); Assert.Equal(target.EntryId, f.Queue.Snapshot.CurrentEntryId); await f.Queue.DislikeFmAsync(target, default); Assert.Single(f.Api.Writes);
        await f.Queue.NextAsync(false, default); Assert.NotEqual(target.EntryId, f.Queue.Snapshot.CurrentEntryId); Assert.Single(f.Api.Writes);
    }
    [Fact, Trait("V8", "C01,C02,F07,F10")]
    public async Task 退出旧账号后读取和反馈均不能污染重登的新会话()
    {
        await using var f = new FmFixture(); await f.Queue.StartFmAsync(default); var target = f.Target(); var receipt = new TaskCompletionSource<FmFeedbackResult>(); f.Api.Feedback = (_, _) => receipt.Task;
        var old = f.Queue.DislikeFmAsync(target, default); f.Sessions.Relogin(); await f.Queue.StartFmAsync(default); var current = f.Queue.Snapshot.CurrentEntryId;
        receipt.SetResult(new(FmFeedbackState.Accepted, "旧成功")); await old; Assert.Equal(current, f.Queue.Snapshot.CurrentEntryId); Assert.Null(f.Fm.Snapshot.Feedback);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); await f.Queue.DislikeFmAsync(f.Target(), cancel.Token); Assert.Single(f.Api.Writes);
    }
    [Fact, Trait("V8", "F01,F03,F06,F07,C04")]
    public async Task FM观察证据来自实际请求推进与反馈记录()
    {
        await using var f = new FmFixture(); await f.Queue.StartFmAsync(default); await f.Queue.NextAsync(false, default); var writesAfterNext = f.Api.Writes.Count;
        var target = f.Target(); await f.Queue.DislikeFmAsync(target, default); await f.Queue.DislikeFmAsync(target, default);
        Assert.Equal(0, writesAfterNext); Assert.Single(f.Api.Writes); Assert.Equal(1, f.Api.Maximum);
        TestEvidence.Write("v8-fm-session.json", new { schemaVersion = 1, realHost = false, maximumConcurrentReads = f.Api.Maximum, nextWrites = writesAfterNext,
            feedbackWrites = f.Api.Writes.Count, targetId = target.TrackId, actualTargetId = f.Api.Writes.Single(), currentId = f.Queue.Snapshot.Playback.Track!.Id,
            targetEntry = target.EntryId, sessionId = target.SessionId, pending = f.Queue.Snapshot.Entries.Count - f.Queue.Snapshot.Entries.ToList().FindIndex(e => e.EntryId == f.Queue.Snapshot.CurrentEntryId) - 1 });
    }
}
