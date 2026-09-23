using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Music;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class SeekingTests
{
    [Fact, Trait("M2", "B03,B04,B05,C04")]
    public async Task 草稿不被进度回推且暂停定位失败不伪造事实()
    {
        await using var f = new PlaybackFixture(); await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        using var timeline = new TimelineWorkspace(f.Queue, new ImmediateUi());
        var generation = f.Queue.Snapshot.Playback.Generation;
        timeline.Begin(); timeline.Position = 30000; timeline.Position = 40000;
        f.Audio.Emit(generation, PlaybackState.Playing, 2000); Assert.Equal(40000, timeline.Position); Assert.Empty(f.Audio.SeekPositions);
        await timeline.CommitAsync(); Assert.Equal(40000, f.Queue.Snapshot.Playback.PositionMs); Assert.Single(f.Audio.SeekPositions);
        await f.Queue.PauseAsync(true, default); timeline.Begin(); timeline.Position = -50; await timeline.CommitAsync();
        Assert.Equal(PlaybackState.Paused, f.Queue.Snapshot.Playback.State); Assert.Equal(0, f.Queue.Snapshot.Playback.PositionMs);
        timeline.Begin(); timeline.Position = 999999; await timeline.CommitAsync(); Assert.Equal(119999, f.Queue.Snapshot.Playback.PositionMs);
        f.Audio.Seeking = _ => throw new MusicException(MusicError.Timeout, "定位超时");
        timeline.Begin(); timeline.Position = 4000; await timeline.CommitAsync();
        Assert.Equal(119999, timeline.Position); Assert.Equal(PlaybackState.Paused, f.Queue.Snapshot.Playback.State); Assert.Contains("超时", f.Queue.Snapshot.Playback.Message);
        timeline.Begin(); timeline.Position = 6000; await f.Queue.PlaySingleAsync(MusicCatalog.Track(2), default);
        Assert.False(timeline.IsEditing); await timeline.CommitAsync(); Assert.Equal(3, f.Audio.SeekPositions.Count);
        f.Audio.CanSeek = false; f.Audio.Emit(f.Queue.Snapshot.Playback.Generation, PlaybackState.Playing); Assert.False(timeline.CanSeek);
        f.Sessions.Revoke(); Assert.False(timeline.CanSeek);
    }

    [Fact, Trait("M2", "B04,B05,C04,C05")]
    public async Task 定位等待时新目标胜出且换曲拒绝旧控制()
    {
        await using var f = new PlaybackFixture(); await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        var initial = f.Queue.Snapshot; var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Audio.Seeking = async ct => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); };
        var old = f.Queue.SeekAsync(initial.CurrentEntryId!.Value, initial.Playback.Generation, 1000, default); await entered.Task;
        f.Audio.Seeking = null;
        await f.Queue.SeekAsync(initial.CurrentEntryId.Value, initial.Playback.Generation, 2000, default); await old;
        Assert.Equal(new long[] { 2000 }, f.Audio.SeekPositions);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(2), default);
        await f.Queue.SeekAsync(initial.CurrentEntryId.Value, initial.Playback.Generation, 3000, default);
        Assert.Equal(0, f.Queue.Snapshot.Playback.PositionMs); Assert.Single(f.Audio.SeekPositions);
    }

    [Theory, InlineData(false), InlineData(true), Trait("M2", "B06,B07")]
    public async Task 过期与网络恢复共享三次预算并只取两次地址(bool expiredFirst)
    {
        using var sessions = new MusicSessions(); var catalog = new MusicCatalog(); var buffer = new MusicBuffer(); var time = new FakeTimeProvider(); var calls = 0;
        buffer.Download = (_, _) => ++calls <= 2 ? throw new MusicException((calls == 1) == expiredFirst ? MusicError.AddressExpired : MusicError.Network, "失败") : Task.FromResult(new BufferedMedia("ok", _ => { }));
        var loader = new MediaLoader(catalog, buffer, time);
        var work = loader.LoadAsync(1, sessions.Capture(), default, _ => { }, _ => { });
        time.Advance(TimeSpan.FromMilliseconds(500)); using var result = (await work.WaitAsync(TimeSpan.FromSeconds(3))).Media;
        Assert.Equal(3, calls); Assert.Equal(2, catalog.Resolves);
    }

    [Theory, InlineData(MusicError.Restricted), InlineData(MusicError.Protocol), InlineData(MusicError.Decode), InlineData(MusicError.SignedOut), InlineData(MusicError.Storage), InlineData(MusicError.RateLimited)]
    [Trait("M2", "B06,B07")]
    public async Task 不可重试错误只请求一次(MusicError kind)
    {
        using var sessions = new MusicSessions(); var catalog = new MusicCatalog(); var calls = 0;
        var buffer = new MusicBuffer { Download = (_, _) => { calls++; throw new MusicException(kind, "失败"); } };
        var loader = new MediaLoader(catalog, buffer, new FakeTimeProvider());
        Assert.Equal(kind, (await Assert.ThrowsAsync<MusicException>(() => loader.LoadAsync(1, sessions.Capture(), default, _ => { }, _ => { }))).Kind);
        Assert.Equal(1, calls); Assert.Equal(1, catalog.Resolves);
    }

    [Fact, Trait("M2", "B06,B07")]
    public async Task 重试延时可取消且持续网络失败不耗尽第三次无关预算()
    {
        using var sessions = new MusicSessions(); using var ct = new CancellationTokenSource(); var time = new FakeTimeProvider(); var calls = 0;
        var buffer = new MusicBuffer { Download = (_, _) => { calls++; throw new MusicException(MusicError.Network, "失败"); } };
        var loader = new MediaLoader(new MusicCatalog(), buffer, time);
        var work = loader.LoadAsync(1, sessions.Capture(), ct.Token, _ => { }, _ => { }); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work); Assert.Equal(1, calls);
        calls = 0; work = loader.LoadAsync(1, sessions.Capture(), default, _ => { }, _ => { }); time.Advance(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAsync<MusicException>(() => work); Assert.Equal(2, calls);
    }
}
