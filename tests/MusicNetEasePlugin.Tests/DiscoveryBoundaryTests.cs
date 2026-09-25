using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Discovery;
using MusicNetEasePlugin.Features.Music;
using Xunit;

namespace MusicNetEasePlugin.Tests;

/// <summary>把取消、限流和终态竞争安排到真实异步边界，避免只断言配置常量。</summary>
public sealed class DiscoveryBoundaryTests
{
    [Fact, Trait("V8", "C04,C05,R01")]
    public async Task FM页面二十次挂卸释放订阅非最后页面关闭不停止共享播放()
    {
        await using var f = new FmFixture(requireDocument: true); using var owner = new MusicPlaybackLifetime(f.Queue);
        using var firstLife = new MusicLifetime(); using var secondLife = new MusicLifetime();
        using var first = new MusicPlaybackLease(owner, firstLife); using var second = new MusicPlaybackLease(owner, secondLife);
        await f.Queue.StartFmAsync(default); var session = f.Queue.Snapshot.FmSessionId;
        var peak = 0; var rounds = 0;
        for (var i = 0; i < 20; i++)
        {
            var page = new PrivateFmWorkspace(f.Fm, f.Queue, f.Queue, new ImmediateUi()); peak = Math.Max(peak, f.Fm.SubscriberCount);
            page.Dispose(); page.Dispose(); Assert.Equal(0, f.Fm.SubscriberCount); rounds++;
        }
        firstLife.Close(); Assert.Equal(session, f.Queue.Snapshot.FmSessionId); Assert.NotNull(f.Audio.Current);
        secondLife.Close(); await owner.Pending; Assert.Null(f.Audio.Current); Assert.Equal(Guid.Empty, f.Queue.Snapshot.FmSessionId);
        TestEvidence.Write("v8-fm-lifetime.json", new { schemaVersion = 1, realHost = false, rounds, peakSubscriptions = peak,
            retainedSubscriptions = f.Fm.SubscriberCount, activeReads = f.Api.Active, currentMedia = f.Audio.Current is not null,
            fmActive = f.Queue.Snapshot.FmSessionId != Guid.Empty });
    }
    [Fact, Trait("V8", "F10,H04,C05,R03")]
    public async Task FM恢复不恢复供应资格远端读取不写入本机历史()
    {
        var store = new PlaybackPersistenceTests.MemoryStore(); var time = new FakeTimeProvider();
        await using (var persistence = new PlaybackPersistence(store, time))
        {
            await using var f = new FmFixture(time, persistence: persistence); var account = f.Sessions.Capture();
            var saved = await persistence.ActivateAsync(account, default); await f.Queue.RestoreAsync(account, saved!, f.Queue.Snapshot.Revision);
            f.Queue.SetMode(PlaybackMode.RepeatList); await f.Queue.StartFmAsync(default);
            f.Audio.Emit(f.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 3000); await persistence.FlushAsync();
            Assert.Equal(PlaybackMode.RepeatList, store.Data[123].Mode); Assert.Single(store.Data[123].Recent);
        }
        await using var restored = new PlaybackPersistence(store, time); await using var second = new FmFixture(time, persistence: restored);
        var session = second.Sessions.Capture(); var state = await restored.ActivateAsync(session, default); await second.Queue.RestoreAsync(session, state!, second.Queue.Snapshot.Revision);
        Assert.Equal(Guid.Empty, second.Queue.Snapshot.FmSessionId); Assert.Equal(Guid.Empty, second.Fm.Snapshot.SessionId);
        Assert.Equal(PlaybackState.Paused, second.Queue.Snapshot.Playback.State); Assert.Equal(PlaybackMode.RepeatList, second.Queue.Snapshot.Mode);
        Assert.Equal(0, second.Api.Reads); Assert.Empty(second.Api.Writes); Assert.Empty(second.Audio.Opened);
        var api = new DiscoveryFake(); using var page = new DiscoveryWorkspace(api, api, api, second.Catalog, second.Sessions, second.Queue, new ImmediateUi(), time);
        await restored.FlushAsync(); // 先收口恢复本身的保存，再观察远端读取是否新增写入。
        var before = restored.Snapshot.Recent.ToArray(); var writes = store.Saves.Count;
        await page.RecentCommand.ExecuteAsync(null); await page.WeekCommand.ExecuteAsync(null); await page.AllTimeCommand.ExecuteAsync(null);
        time.Advance(TimeSpan.FromSeconds(5)); await restored.Pending;
        Assert.Equal(before, restored.Snapshot.Recent); Assert.Equal(writes, store.Saves.Count);
        TestEvidence.Write("v8-discovery-restore.json", new { schemaVersion = 1, realHost = false, restoredEntries = second.Queue.Snapshot.Entries.Count,
            fmReads = second.Api.Reads, feedbackWrites = second.Api.Writes.Count, mediaOpens = second.Audio.Opened.Count,
            localSavesFromRemoteRead = store.Saves.Count - writes, recentBefore = before.Length, recentAfter = restored.Snapshot.Recent.Count,
            fmActive = second.Queue.Snapshot.FmSessionId != Guid.Empty });
    }
    [Fact, Trait("V8", "F04,C03")]
    public async Task FM结束后重新开始仍遵守同账号限流窗口()
    {
        var time = new FakeTimeProvider(); await using var f = new FmFixture(time);
        f.Api.Content = (_, _) => throw new MusicException(MusicError.RateLimited, "等待", TimeSpan.FromSeconds(30));
        await f.Queue.StartFmAsync(default); await f.Queue.StartFmAsync(default); Assert.Equal(1, f.Api.Reads);
        Assert.Empty(f.Queue.Snapshot.Entries); Assert.Contains("受限", f.Fm.Snapshot.Message);
        time.Advance(TimeSpan.FromSeconds(30)); f.Api.Content = (_, _) => Task.FromResult<IReadOnlyList<MusicTrack>>([DiscoveryFake.Track(10), DiscoveryFake.Track(11), DiscoveryFake.Track(12)]);
        await f.Queue.StartFmAsync(default); Assert.Equal(2, f.Api.Reads); Assert.Single(f.Audio.Opened);
    }

    [Fact, Trait("V8", "F01,F03,C03,C06")]
    public async Task 旧会话忽略取消时新FM仍只有一个物理读取()
    {
        await using var f = new FmFixture(); var old = new TaskCompletionSource<IReadOnlyList<MusicTrack>>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Api.Content = (call, _) => call == 1 ? old.Task : Task.FromResult<IReadOnlyList<MusicTrack>>([DiscoveryFake.Track(20), DiscoveryFake.Track(21), DiscoveryFake.Track(22)]);
        var first = f.Queue.StartFmAsync(default); await DiscoveryTestWait.Until(() => f.Api.Reads == 1);
        var second = f.Queue.StartFmAsync(default); await first.WaitAsync(TimeSpan.FromSeconds(2)); Assert.Equal(1, f.Api.Reads);
        old.SetResult([DiscoveryFake.Track(10)]); await second;
        Assert.Equal(1, f.Api.Maximum); Assert.Equal(20, f.Queue.Snapshot.Playback.Track!.Id); Assert.Single(f.Audio.Opened);
    }

    [Fact, Trait("V8", "F02,F03,F06")]
    public async Task 等待补取期间Next与重复Ended合并为一次推进()
    {
        await using var f = new FmFixture(); var batch = new TaskCompletionSource<IReadOnlyList<MusicTrack>>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Api.Content = (call, _) => call == 1 ? Task.FromResult<IReadOnlyList<MusicTrack>>([DiscoveryFake.Track(10)]) : batch.Task;
        await f.Queue.StartFmAsync(default); await DiscoveryTestWait.Until(() => f.Api.Reads == 2);
        var generation = f.Queue.Snapshot.Playback.Generation; var next = f.Queue.NextAsync(false, default);
        f.Audio.Emit(generation, PlaybackState.Ended); f.Audio.Emit(generation, PlaybackState.Ended);
        batch.SetResult([DiscoveryFake.Track(20), DiscoveryFake.Track(21), DiscoveryFake.Track(22)]); await next;
        Assert.Equal(20, f.Queue.Snapshot.Playback.Track!.Id); Assert.Equal(2, f.Audio.Opened.Count); Assert.Empty(f.Api.Writes);
        TestEvidence.Write("v8-fm-race.json", new { schemaVersion = 1, realHost = false, reads = f.Api.Reads, maximumConcurrentReads = f.Api.Maximum,
            opens = f.Audio.Opened.Count, currentId = f.Queue.Snapshot.Playback.Track.Id, feedbackWrites = f.Api.Writes.Count });
    }

    [Theory, InlineData(500), InlineData(1500), Trait("V8", "F03,C06")]
    public async Task 两个补取等待点关闭都会取消且不再发送(int delay)
    {
        var time = new FakeTimeProvider(); await using var f = new FmFixture(time);
        f.Api.Content = (_, _) => Task.FromResult<IReadOnlyList<MusicTrack>>([]);
        var read = f.Fm.ReadAsync(f.Fm.Begin(f.Sessions.Capture()), []);
        await DiscoveryTestWait.Until(() => f.Fm.WaitingDelayMilliseconds == 500);
        if (delay == 1500) { time.Advance(TimeSpan.FromMilliseconds(500)); await DiscoveryTestWait.Until(() => f.Fm.WaitingDelayMilliseconds == 1500); }
        var calls = f.Api.Reads; f.Fm.End(); await read.WaitAsync(TimeSpan.FromSeconds(2)); time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(calls, f.Api.Reads); Assert.Equal(Guid.Empty, f.Fm.Snapshot.SessionId); Assert.Equal(0, f.Api.Active);
    }

    [Fact, Trait("V8", "F05,U03")]
    public async Task 普通同曲意图退出FM后队列投影恢复编辑()
    {
        await using var f = new FmFixture(); using var projection = new QueueWorkspace(f.Queue, new ImmediateUi());
        await f.Queue.StartFmAsync(default); Assert.False(projection.CanEdit); var opens = f.Audio.Opened.Count;
        await f.Queue.PlayNowAsync(QueueEntry.FromTrack(DiscoveryFake.Track(10)), default);
        Assert.True(projection.CanEdit); Assert.Equal(opens, f.Audio.Opened.Count); Assert.Equal(Guid.Empty, f.Fm.Snapshot.SessionId);
    }

    [Fact, Trait("V8", "D06,C02,C03,C06")]
    public async Task 发现连续刷新最多两个物理读取且关闭立即完成调用()
    {
        await using var f = new PlaybackFixture(); var api = new DiscoveryFake(); var responses = new List<TaskCompletionSource<CatalogPage<MusicTrack>>>();
        api.Daily = _ => { var response = new TaskCompletionSource<CatalogPage<MusicTrack>>(TaskCreationOptions.RunContinuationsAsynchronously); responses.Add(response); return response.Task; };
        using var page = new DiscoveryWorkspace(api, api, api, f.Catalog, f.Sessions, f.Queue, new ImmediateUi());
        var first = page.DailyCommand.ExecuteAsync(null); var second = page.RefreshCommand.ExecuteAsync(null); var third = page.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, api.Reads); page.Dispose(); await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(2));
        foreach (var response in responses) response.SetResult(new([DiscoveryFake.Track(1)]));
        Assert.Empty(page.Songs); Assert.Empty(f.Audio.Opened);
    }

    [Fact, Trait("V8", "F08,F09,C06")]
    public async Task 反馈超过预算保留未知迟到成功不推进也不重发()
    {
        var time = new FakeTimeProvider(); await using var f = new FmFixture(time); await f.Queue.StartFmAsync(default);
        var response = new TaskCompletionSource<FmFeedbackResult>(TaskCreationOptions.RunContinuationsAsynchronously); f.Api.Feedback = (_, _) => response.Task;
        var target = f.Target(); var feedback = f.Queue.DislikeFmAsync(target, default); Assert.Single(f.Api.Writes);
        time.Advance(TimeSpan.FromSeconds(15)); await feedback.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(FmFeedbackState.Uncertain, f.Fm.Snapshot.Feedback!.State); Assert.Equal(target.EntryId, f.Queue.Snapshot.CurrentEntryId);
        response.SetResult(new(FmFeedbackState.Accepted, "迟到成功")); await f.Queue.DislikeFmAsync(target, default);
        Assert.Single(f.Api.Writes); Assert.Equal(target.EntryId, f.Queue.Snapshot.CurrentEntryId);
    }
}
