using MusicNetEasePlugin.Application.Playback;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class PlaybackCoordinatorTests
{
    [Fact, Trait("M1", "L01,L02,L07,L08")]
    public async Task 引擎确认前保持加载且控制和旧事件不能复活已停止歌曲()
    {
        await using var f = new PlaybackFixture();
        f.Audio.AutoStart = false;
        await f.Play();
        Assert.Equal(PlaybackState.Loading, f.Player.Snapshot.State);
        var generation = Assert.Single(f.Audio.Opened).Generation;
        f.Audio.Emit(generation, PlaybackState.Playing, 4500);
        await f.Player.PauseAsync(true, default);
        Assert.Equal(PlaybackState.Paused, f.Player.Snapshot.State);
        Assert.Equal(4500, f.Player.Snapshot.PositionMs);
        await f.Player.SetVolumeAsync(0, default);
        Assert.Equal(0, f.Audio.Volume);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Player.SetVolumeAsync(101, default));
        await f.Player.PauseAsync(false, default);
        Assert.Equal(PlaybackState.Playing, f.Player.Snapshot.State);
        await f.Player.StopAsync();
        f.Audio.Emit(generation, PlaybackState.Playing, 10000);
        Assert.Equal(PlaybackState.Stopped, f.Player.Snapshot.State);
        Assert.Equal(0, f.Player.Snapshot.PositionMs);
        Assert.Single(f.Buffer.Deleted);
        await f.Play();
        Assert.Equal(2, f.Catalog.Resolves);
    }

    [Theory, InlineData("detail"), InlineData("resource"), InlineData("buffer"), InlineData("open")]
    [Trait("M2", "C01")]
    [Trait("M1", "A03,C05,L03,L04,B04")]
    public async Task 快速三次切歌只允许最终意图打开音源(string stage)
    {
        await using var f = new PlaybackFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Wait(long id) { if (id == 1) { entered.SetResult(); await release.Task; } }
        f.Catalog.Detail = async (id, _) => { if (stage == "detail") await Wait(id); return MusicCatalog.Track(id); };
        f.Catalog.Resource = async (id, _) => { if (stage == "resource") await Wait(id); return new(id, new Uri("https://m1.music.126.net/file"), "mp3", "standard", false, null); };
        f.Buffer.Download = async (r, _) => { if (stage == "buffer") await Wait(r.Id); return new(r.Id + ".media", f.Buffer.Deleted.Enqueue); };
        f.Audio.Opening = _ => stage == "open" && !entered.Task.IsCompleted ? Wait(1) : Task.CompletedTask;
        var first = f.Play(1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = f.Play(2);
        var third = f.Play(3);
        release.SetResult();
        await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("3.media", Assert.Single(f.Audio.Opened).Path);
        Assert.Equal(3, f.Player.Snapshot.Track!.Id);
        Assert.Equal(PlaybackState.Playing, f.Player.Snapshot.State);
        Assert.DoesNotContain("2.media", f.Audio.Opened.Select(p => p.Path));
    }

    [Fact, Trait("M1", "L04,L09,A03,B04")]
    public async Task 账号撤销取消加载并清除曲目且关闭无关页面不停止当前歌曲()
    {
        await using var f = new PlaybackFixture();
        await f.Play();
        await f.Player.StopAsync(Guid.NewGuid());
        Assert.Equal(PlaybackState.Playing, f.Player.Snapshot.State);
        await f.Player.StopAsync();
        Assert.NotNull(f.Player.Snapshot.Track);
        f.Sessions.Revoke();
        await f.Player.StopAsync();
        Assert.Null(f.Player.Snapshot.Track);
        Assert.Null(f.Audio.Current);
        Assert.Single(f.Buffer.Deleted);
        f.Sessions.Relogin();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Catalog.Detail = async (_, ct) => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return MusicCatalog.Track(2); };
        var play = f.Play(2);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        f.Sessions.Revoke();
        await f.Player.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await play;
        Assert.Single(f.Audio.Opened);
    }

    [Theory, InlineData(PlaybackState.Ended), InlineData(PlaybackState.Failed)]
    [Trait("M1", "L05,B06,E05")]
    public async Task 原生终态在回调外收口并先停止后删文件(PlaybackState state)
    {
        await using var f = new PlaybackFixture();
        var cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Buffer.Download = (r, _) => Task.FromResult(new BufferedMedia("fixture", _ => { Assert.Null(f.Audio.Current); cleaned.SetResult(); }));
        await f.Play();
        f.Audio.Emit(Assert.Single(f.Audio.Opened).Generation, state);
        await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(state, f.Player.Snapshot.State);
        Assert.Single(f.Audio.Opened);
    }

    [Theory, InlineData(MusicError.AddressExpired, 2), InlineData(MusicError.Network, 1)]
    [Trait("M1", "B07,L06")]
    public async Task 只有明确地址过期重取一次且再次失败就结束(MusicError kind, int expected)
    {
        await using var f = new PlaybackFixture();
        f.Buffer.Download = (_, _) => throw new MusicException(kind, "资源不可用");
        await f.Play();
        Assert.Equal(expected, f.Catalog.Resolves);
        Assert.Equal(PlaybackState.Failed, f.Player.Snapshot.State);
        Assert.Empty(f.Audio.Opened);
        Assert.True(f.Sessions.SignedIn);
    }

    [Fact, Trait("M1", "L06")]
    public async Task 无地址不启动引擎且试听显示来自资源事实()
    {
        await using var f = new PlaybackFixture();
        f.Catalog.Resource = (id, _) => Task.FromResult(new PlaybackResource(id, null, "", "standard", false, null));
        await f.Play();
        Assert.Empty(f.Audio.Opened);
        f.Catalog.Resource = (id, _) => Task.FromResult(new PlaybackResource(id, new Uri("https://m1.music.126.net/file"), "mp3", "standard", true, 30000));
        await f.Play();
        Assert.True(f.Player.Snapshot.IsTrial);
        Assert.Contains("试听", f.Player.Snapshot.Message);
    }

    [Fact, Trait("M1", "L04,B06")]
    public async Task 停止故障保留文件并允许再次停止收口()
    {
        await using var f = new PlaybackFixture();
        await f.Play();
        f.Audio.Stopping = () => throw new MusicException(MusicError.Device, "停止失败");
        await f.Player.StopAsync();
        Assert.Equal(PlaybackState.Failed, f.Player.Snapshot.State);
        Assert.Empty(f.Buffer.Deleted);
        f.Audio.Stopping = null;
        await f.Player.StopAsync();
        Assert.Single(f.Buffer.Deleted);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Audio.Stopping = async () => { stopping.TrySetResult(); await release.Task; };
        var firstDispose = f.Player.DisposeAsync().AsTask();
        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var secondDispose = f.Player.DisposeAsync().AsTask();
        Assert.False(firstDispose.IsCompleted); Assert.False(secondDispose.IsCompleted);
        release.SetResult(); await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact, Trait("M1", "L04,L08")]
    public async Task 旧暂停排队期间切歌会撤销旧控制意图()
    {
        await using var f = new PlaybackFixture();
        await f.Play();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Audio.Pausing = async ct => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); };
        var pause = f.Player.PauseAsync(true, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await f.Play(2);
        await pause.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(PlaybackState.Playing, f.Player.Snapshot.State);
        Assert.Equal(2, f.Player.Snapshot.Track!.Id);
    }
}
