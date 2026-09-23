using MusicNetEasePlugin.Application.Playback;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class QueueTests
{
    private static QueueEntry[] Entries(params long[] ids) => ids.Select(id => QueueEntry.FromTrack(MusicCatalog.Track(id))).ToArray();
    [Theory, InlineData(PlaybackMode.Sequential), InlineData(PlaybackMode.RepeatList), InlineData(PlaybackMode.RepeatOne), InlineData(PlaybackMode.Shuffle)]
    [Trait("M2", "Q01,Q02,Q03,Q07,Q08,Q09,Q10")]
    public void 优先段保持操作顺序且重复歌曲有独立身份(PlaybackMode mode)
    {
        var nav = new QueueNavigator(_ => 0); var entries = Entries(1, 2, 1);
        nav.Replace(entries, 0); nav.SetMode(mode);
        var nextA = Entries(4)[0]; var nextB = Entries(5)[0]; nav.Add([nextA], true); nav.Add([nextB], true);
        Assert.Equal(nextA.EntryId, nav.Next(true)); Assert.Equal(nextB.EntryId, nav.Next(true));
        Assert.Equal(5, nav.Entries.Count); Assert.Equal(5, nav.Entries.Select(e => e.EntryId).Distinct().Count());
        var current = nav.CurrentId; nav.Remove(entries[0].EntryId); nav.Move(entries[1].EntryId, 1);
        Assert.Equal(current, nav.CurrentId); Assert.Contains(nav.Entries, e => e.EntryId == entries[2].EntryId);
    }
    [Fact, Trait("M2", "Q06,Q07,Q08,Q09,Q10")]
    public void 模式首尾和随机访问历史有确定行为()
    {
        var nav = new QueueNavigator(_ => 0); var entries = Entries(1, 2, 3); nav.Replace(entries, 0);
        Assert.False(nav.CanPrevious); Assert.Equal(entries[1].EntryId, nav.Next(true));
        Assert.Equal(entries[2].EntryId, nav.Next(true)); Assert.False(nav.CanNext); Assert.Null(nav.Next(true));
        nav.SetMode(PlaybackMode.RepeatList); Assert.Equal(entries[0].EntryId, nav.Next(true)); Assert.Equal(entries[2].EntryId, nav.Previous());
        nav.SetMode(PlaybackMode.RepeatOne); Assert.Equal(entries[2].EntryId, nav.Next(true)); Assert.Equal(entries[0].EntryId, nav.Next(false));
        nav.SetMode(PlaybackMode.Shuffle); var first = nav.CurrentId; var second = nav.Next(true); var third = nav.Next(true);
        Assert.Equal(3, new[] { first, second, third }.Distinct().Count());
        Assert.Equal(second, nav.Previous()); Assert.Equal(third, nav.Next(false)); Assert.NotEqual(third, nav.Next(true));
        var deleted = nav.CurrentId!.Value; nav.Remove(deleted); Assert.NotEqual(deleted, nav.Previous());
        nav.Replace(Entries(1), 0); Assert.False(nav.CanNext); Assert.False(nav.CanPrevious); Assert.NotNull(nav.Next(true));
        nav.Replace([], 0); Assert.Null(nav.Next(true)); Assert.Null(nav.Previous());
    }
    [Fact, Trait("M2", "Q01,Q02,Q03,Q04,Q05,C02,C03,A03")]
    public async Task 连续播放去重终态且修改队列不重启当前曲()
    {
        await using var f = new PlaybackFixture(); var entries = Entries(1, 2, 3);
        await f.Queue.ReplaceAsync(entries, 0, default);
        var first = f.Audio.Opened.Last();
        await f.Queue.EnqueueAsync(Entries(4), false, default); Assert.Single(f.Audio.Opened);
        var next = Wait(f.Queue, s => s.Playback.State == PlaybackState.Playing && s.Playback.Track?.Id == 2);
        f.Audio.Emit(first.Generation, PlaybackState.Ended); f.Audio.Emit(first.Generation, PlaybackState.Ended);
        await next; Assert.Equal(2, f.Audio.Opened.Count);
        await f.Queue.RemoveAsync(entries[0].EntryId, default); Assert.Equal(2, f.Audio.Opened.Count);
        await f.Queue.PauseAsync(true, default);
        await f.Queue.RemoveAsync(entries[1].EntryId, default); Assert.Equal(PlaybackState.Paused, f.Queue.Snapshot.Playback.State);
        Assert.Null(f.Audio.Current); Assert.Equal(entries[2].EntryId, f.Queue.Snapshot.CurrentEntryId);
        await f.Queue.PauseAsync(false, default); Assert.Equal(3, f.Queue.Snapshot.Playback.Track!.Id);
        await f.Queue.ClearAsync(); Assert.Empty(f.Queue.Snapshot.Entries); Assert.Null(f.Audio.Current);
        f.Audio.Emit(first.Generation, PlaybackState.Ended); Assert.Empty(f.Queue.Snapshot.Entries);
        await f.Queue.EnqueueAsync(Entries(9), false, default); Assert.Null(f.Audio.Current);
        await f.Queue.StopAsync(); Assert.Single(f.Queue.Snapshot.Entries); Assert.Equal(0, f.Queue.Snapshot.Playback.PositionMs);
    }
    [Theory, InlineData(3, 3), InlineData(30, 20)]
    [Trait("M2", "Q11,C02")]
    public async Task 全部不可播按候选预算停止而不是循环(int count, int attempts)
    {
        await using var f = new PlaybackFixture();
        f.Catalog.Resource = (id, _) => Task.FromResult(new PlaybackResource(id, null, "mp3", "standard", false, null));
        var stopped = Wait(f.Queue, s => s.Playback.Message.StartsWith("已尝试", StringComparison.Ordinal));
        f.Queue.SetMode(PlaybackMode.RepeatOne);
        await f.Queue.ReplaceAsync(Entries(Enumerable.Range(1, count).Select(i => (long)i).ToArray()), 0, default);
        await stopped; Assert.Equal(attempts, f.Catalog.Resolves); Assert.Null(f.Audio.Current);
        Assert.Equal(PlaybackState.Failed, f.Queue.Snapshot.Playback.State);
    }
    [Theory, InlineData(MusicError.Network), InlineData(MusicError.Device), InlineData(MusicError.RateLimited), InlineData(MusicError.Protocol)]
    [Trait("M2", "Q12")]
    public async Task 全局错误不扫描其他歌曲(MusicError error)
    {
        await using var f = new PlaybackFixture(); f.Catalog.Resource = (_, _) => throw new MusicException(error, "失败");
        await f.Queue.ReplaceAsync(Entries(1, 2, 3), 0, default);
        Assert.Equal(1, f.Catalog.Resolves); Assert.Equal(1, f.Queue.Snapshot.Playback.Track!.Id); Assert.Equal(error, f.Queue.Snapshot.Playback.Error);
    }
    [Fact, Trait("M2", "C01,C03,C05,A02,A04")]
    public async Task 迟到加载不能反转用户意图且账号撤销清空队列()
    {
        await using var f = new PlaybackFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<MusicTrack>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Catalog.Detail = (id, _) => { if (id == 1) { entered.TrySetResult(); return release.Task; } return Task.FromResult(MusicCatalog.Track(id)); };
        var old = f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default); await entered.Task;
        var b = f.Queue.PlaySingleAsync(MusicCatalog.Track(2), default); var c = f.Queue.PlaySingleAsync(MusicCatalog.Track(3), default);
        release.SetResult(MusicCatalog.Track(1)); await Task.WhenAll(old, b, c);
        Assert.Equal(3, f.Queue.Snapshot.Playback.Track!.Id); Assert.Single(f.Audio.Opened);
        f.Sessions.Revoke(); await f.Queue.StopAsync(); Assert.Empty(f.Queue.Snapshot.Entries); Assert.Equal(0, f.Queue.Snapshot.AccountId); Assert.Null(f.Audio.Current);
        await f.Queue.DisposeAsync(); await f.Queue.DisposeAsync();
    }
    private static Task Wait(IPlayerSession session, Func<PlayerSessionSnapshot, bool> predicate)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? _, PlayerSessionSnapshot snapshot) { if (predicate(snapshot)) done.TrySetResult(); }
        session.Changed += Changed; Changed(null, session.Snapshot);
        return Complete();
        async Task Complete() { try { await done.Task.WaitAsync(TimeSpan.FromSeconds(5)); } finally { session.Changed -= Changed; } }
    }
}
