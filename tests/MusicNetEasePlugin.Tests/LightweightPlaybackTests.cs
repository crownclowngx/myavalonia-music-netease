using MusicNetEasePlugin.Application.Playback;
using Xunit;

namespace MusicNetEasePlugin.Tests;

/// <summary>V5 的就地播放是新业务意图；这里验证真实协调器，而不是以视图按钮文字代替行为证明。</summary>
public sealed class LightweightPlaybackTests
{
    [Theory, InlineData(PlaybackMode.Sequential), InlineData(PlaybackMode.RepeatList), InlineData(PlaybackMode.RepeatOne), InlineData(PlaybackMode.Shuffle)]
    [Trait("V5", "Q01,Q02")]
    public async Task 立即播放保留原队列和下一首优先顺序(PlaybackMode mode)
    {
        await using var f = new PlaybackFixture();
        var original = Entries(1, 2, 3);
        await f.Queue.ReplaceAsync(original, 0, default);
        f.Queue.SetMode(mode);
        var priority = Entries(4, 5);
        await f.Queue.EnqueueAsync(priority, true, default);
        var temporary = Entries(9)[0];
        await f.Queue.PlayNowAsync(temporary, default);
        Assert.Equal(new long[] { 1, 9, 4, 5, 2, 3 }, f.Queue.Snapshot.Entries.Select(e => e.TrackId));
        Assert.Equal(temporary.EntryId, f.Queue.Snapshot.CurrentEntryId);
        Assert.Equal(PlaybackState.Playing, f.Queue.Snapshot.Playback.State);
        Assert.Equal(mode, f.Queue.Snapshot.Mode);
        await f.Queue.NextAsync(false, default);
        Assert.Equal(priority[0].EntryId, f.Queue.Snapshot.CurrentEntryId);
        await f.Queue.NextAsync(false, default);
        Assert.Equal(priority[1].EntryId, f.Queue.Snapshot.CurrentEntryId);
        Assert.All(original, entry => Assert.Contains(f.Queue.Snapshot.Entries, item => item.EntryId == entry.EntryId));
    }

    [Fact, Trait("V5", "Q01,Q02")]
    public async Task 当前同曲继续而不重复入队或从头播放()
    {
        await using var f = new PlaybackFixture();
        await f.Queue.ReplaceAsync(Entries(1, 2, 1), 0, default);
        var current = f.Queue.Snapshot.CurrentEntryId;
        f.Audio.Emit(f.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 12000);
        await f.Queue.PlayNowAsync(Entries(1)[0], default);
        Assert.Single(f.Audio.Opened);
        await f.Queue.PauseAsync(true, default);
        await f.Queue.PlayNowAsync(Entries(1)[0], default);
        Assert.Equal(PlaybackState.Playing, f.Queue.Snapshot.Playback.State);
        Assert.Equal(current, f.Queue.Snapshot.CurrentEntryId);
        Assert.Equal(3, f.Queue.Snapshot.Entries.Count);
        Assert.Single(f.Audio.Opened);
        Assert.Equal(12000, f.Queue.Snapshot.Playback.PositionMs);
    }

    [Fact, Trait("V5", "Q01,Q03")]
    public async Task 空队列立即播放且其他位置同曲不被擅自合并()
    {
        await using var f = new PlaybackFixture();
        await f.Queue.PlayNowAsync(Entries(1)[0], default);
        Assert.Equal(PlaybackState.Playing, f.Queue.Snapshot.Playback.State);
        var oldDuplicate = Entries(2)[0];
        await f.Queue.EnqueueAsync([oldDuplicate], false, default);
        await f.Queue.StopAsync();
        var newEntry = Entries(2)[0];
        await f.Queue.PlayNowAsync(newEntry, default);
        Assert.Equal(newEntry.EntryId, f.Queue.Snapshot.CurrentEntryId);
        Assert.Contains(oldDuplicate, f.Queue.Snapshot.Entries);
        Assert.Equal(3, f.Queue.Snapshot.Entries.Count);
    }

    [Fact, Trait("V5", "Q03")]
    public async Task 满队列和冲突条目标识拒绝后保持原播放()
    {
        await using var f = new PlaybackFixture();
        var entries = Enumerable.Range(1, 10000).Select(id => QueueEntry.FromTrack(MusicCatalog.Track(id))).ToArray();
        await f.Queue.ReplaceAsync(entries, 0, default);
        await Assert.ThrowsAsync<MusicException>(() => f.Queue.PlayNowAsync(Entries(10001)[0], default));
        Assert.Equal(entries[0].EntryId, f.Queue.Snapshot.CurrentEntryId);
        Assert.Single(f.Audio.Opened);
        await f.Queue.PlayNowAsync(Entries(1)[0], default);
        Assert.Equal(10000, f.Queue.Snapshot.Entries.Count);
        await f.Queue.ReplaceAsync(Entries(1, 2), 0, default);
        var duplicateId = f.Queue.Snapshot.Entries[1] with { TrackId = 9 };
        await Assert.ThrowsAsync<MusicException>(() => f.Queue.PlayNowAsync(duplicateId, default));
        Assert.Equal(new long[] { 1, 2 }, f.Queue.Snapshot.Entries.Select(e => e.TrackId));
    }

    [Fact, Trait("V5", "Q03,Q04")]
    public async Task 试听网络失败保留原队列且撤销账号后拒绝新意图()
    {
        await using var f = new PlaybackFixture();
        await f.Queue.ReplaceAsync(Entries(1, 2), 0, default);
        f.Catalog.Resource = (_, _) => throw new MusicException(MusicError.Network, "网络中断");
        await f.Queue.PlayNowAsync(Entries(9)[0], default);
        Assert.Equal(new long[] { 1, 9, 2 }, f.Queue.Snapshot.Entries.Select(e => e.TrackId));
        Assert.Equal(PlaybackState.Failed, f.Queue.Snapshot.Playback.State);
        Assert.Equal(9, f.Queue.Snapshot.Playback.Track!.Id);
        f.Sessions.Revoke();
        await Assert.ThrowsAsync<MusicException>(() => f.Queue.PlayNowAsync(Entries(3)[0], default));
        Assert.Empty(f.Queue.Snapshot.Entries);
    }

    [Fact, Trait("V5", "Q04")]
    public async Task 连续接纳试听时旧加载不能覆盖新曲并且旧队列仍在()
    {
        await using var f = new PlaybackFixture();
        await f.Queue.ReplaceAsync(Entries(1, 2), 0, default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<MusicTrack>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Catalog.Detail = (id, _) => { if (id == 8) { entered.TrySetResult(); return release.Task; } return Task.FromResult(MusicCatalog.Track(id)); };
        var first = f.Queue.PlayNowAsync(Entries(8)[0], default);
        await entered.Task;
        var latest = f.Queue.PlayNowAsync(Entries(9)[0], default);
        release.SetResult(MusicCatalog.Track(8));
        await Task.WhenAll(first, latest);
        Assert.Equal(new long[] { 1, 8, 9, 2 }, f.Queue.Snapshot.Entries.Select(e => e.TrackId));
        Assert.Equal(9, f.Queue.Snapshot.Playback.Track!.Id);
        Assert.Equal(2, f.Audio.Opened.Count);
    }

    [Fact, Trait("V5", "Q02")]
    public async Task 加载中再次点同曲不会重复取资源或打开媒体()
    {
        await using var f = new PlaybackFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<MusicTrack>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        f.Catalog.Detail = (_, _) => { calls++; entered.TrySetResult(); return reply.Task; };
        var first = f.Queue.PlayNowAsync(Entries(1)[0], default); await entered.Task;
        await f.Queue.PlayNowAsync(Entries(1)[0], default); Assert.Single(f.Queue.Snapshot.Entries); Assert.Equal(1, calls);
        reply.SetResult(MusicCatalog.Track(1)); await first; Assert.Single(f.Audio.Opened);
    }

    [Fact, Trait("V5", "Q06")]
    public async Task 临时不可播曲目按原规则推进而试听仍保留真实范围()
    {
        await using var f = new PlaybackFixture(); await f.Queue.ReplaceAsync(Entries(1, 2), 0, default);
        f.Catalog.Resource = (id, _) => Task.FromResult(new PlaybackResource(id, id == 9 ? null : new Uri("https://m1.music.126.net/fixture"), "mp3", "standard", id == 8, id == 8 ? 2000 : null, id == 8 ? 60000 : null));
        var advanced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, PlayerSessionSnapshot state) { if (state.Playback.Track?.Id == 2 && state.Playback.State == PlaybackState.Playing) advanced.TrySetResult(); }
        f.Queue.Changed += Changed;
        try { await f.Queue.PlayNowAsync(Entries(9)[0], default); await advanced.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { f.Queue.Changed -= Changed; }
        Assert.Equal(new long[] { 1, 9, 2 }, f.Queue.Snapshot.Entries.Select(e => e.TrackId));
        await f.Queue.PlayNowAsync(Entries(8)[0], default); Assert.True(f.Queue.Snapshot.Playback.IsTrial);
        Assert.Equal(2000, f.Queue.Snapshot.Playback.DurationMs); Assert.Equal(4, f.Queue.Snapshot.Entries.Count);
    }

    private static QueueEntry[] Entries(params long[] ids) => ids.Select(id => QueueEntry.FromTrack(MusicCatalog.Track(id))).ToArray();
}
