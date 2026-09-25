using Microsoft.Extensions.DependencyInjection;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Plugin;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class PlaybackLifetimeTests
{
    [Fact]
    public async Task 最后页面关闭取消歌词且歌词收尾不会延迟原生资源释放()
    {
        await using var f = new PlaybackFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<RawLyrics>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken request = default;
        var api = new LyricsTests.LyricsFake { Get = (_, ct) => { request = ct; entered.TrySetResult(); return finish.Task; } };
        await using var lyrics = new LyricsCoordinator(f.Queue, f.Sessions, api);
        await using var owner = new MusicPlaybackLifetime(f.Queue, lyrics);
        using var life = new MusicLifetime(); using var lease = new MusicPlaybackLease(owner, life);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Audio.ReleasingSession = () => { released.TrySetResult(); return Task.CompletedTask; };
        life.Close(); await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(request.IsCancellationRequested); Assert.Null(f.Audio.Current);
        Assert.False(owner.Pending.IsCompleted);
        finish.SetResult(new("[00:01]迟到的歌词")); await owner.Pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(lyrics.Snapshot.Document.Lines); Assert.False(lyrics.Snapshot.Loading);
        api.Get = (_, _) => Task.FromResult(new RawLyrics("[00:01]重新打开后的歌词"));
        using var secondLife = new MusicLifetime(); using var second = new MusicPlaybackLease(owner, secondLife);
        await lyrics.Pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(lyrics.Snapshot.Document.Lines); Assert.Single(f.Audio.Opened);
    }

    [Fact]
    public async Task 最后页面关闭保存位置且重开只在用户继续后播放()
    {
        await using var f = new PlaybackPersistenceTests.Fixture();
        await f.Activate();
        await using var owner = new MusicPlaybackLifetime(f.Queue);
        using var life = new MusicLifetime();
        using var lease = new MusicPlaybackLease(owner, life);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        f.Audio.Emit(f.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 4200);
        life.Close(); await owner.Pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(f.Audio.Current);
        Assert.Equal(4200, f.Store.Data[123].PositionMs);
        Assert.Equal(4200, f.Queue.Snapshot.Playback.PositionMs);
        Assert.Equal(1, f.Audio.SessionReleases);
        lease.Dispose(); life.Close(); Assert.Equal(1, f.Audio.SessionReleases);
        using var reopenedLife = new MusicLifetime();
        using var reopened = new MusicPlaybackLease(owner, reopenedLife);
        Assert.Single(f.Audio.Opened);
        await f.Queue.PauseAsync(false, default);
        Assert.Equal(4200, f.Single.Snapshot.PositionMs);
        Assert.Equal(2, f.Audio.Opened.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 加载或原生启动期间关闭不会因迟到结果再次播放(bool opening)
    {
        await using var f = new PlaybackFixture();
        await using var owner = new MusicPlaybackLifetime(f.Queue);
        using var life = new MusicLifetime(); using var lease = new MusicPlaybackLease(owner, life);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (opening) f.Audio.Opening = async _ => { entered.TrySetResult(); await release.Task; };
        else f.Catalog.Detail = async (id, _) => { entered.TrySetResult(); await release.Task; return MusicCatalog.Track(id); };
        var play = f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        life.Close(); Assert.False(owner.Pending.IsCompleted);
        release.SetResult();
        await Task.WhenAll(play, owner.Pending).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(f.Audio.Opened); Assert.Null(f.Audio.Current);
        Assert.Equal(1, f.Audio.SessionReleases);
        if (opening) Assert.Single(f.Buffer.Deleted);
    }

    [Fact]
    public async Task 立即重开等待旧引擎释放且旧关闭任务不会停止新播放()
    {
        await using var f = new PlaybackFixture();
        await using var owner = new MusicPlaybackLifetime(f.Queue);
        using var firstLife = new MusicLifetime(); using var first = new MusicPlaybackLease(owner, firstLife);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Audio.ReleasingSession = async () => { entered.TrySetResult(); await release.Task; };
        firstLife.Close(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var secondLife = new MusicLifetime(); using var second = new MusicPlaybackLease(owner, secondLife);
        var next = f.Queue.PlaySingleAsync(MusicCatalog.Track(2), default);
        Assert.False(next.IsCompleted); Assert.Single(f.Audio.Opened);
        release.SetResult(); await next.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("2.media", f.Audio.Current);
        first.Dispose(); Assert.Equal("2.media", f.Audio.Current);
    }

    [Fact]
    public async Task 关闭重开再关闭不会启动排队中的新曲()
    {
        await using var f = new PlaybackFixture();
        await using var owner = new MusicPlaybackLifetime(f.Queue);
        using var firstLife = new MusicLifetime(); using var first = new MusicPlaybackLease(owner, firstLife);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Audio.ReleasingSession = async () => { entered.TrySetResult(); await release.Task; };
        firstLife.Close(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var secondLife = new MusicLifetime(); using var second = new MusicPlaybackLease(owner, secondLife);
        var next = f.Queue.PlaySingleAsync(MusicCatalog.Track(2), default);
        secondLife.Close(); release.SetResult();
        await Task.WhenAll(next, owner.Pending).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(f.Audio.Opened); Assert.Null(f.Audio.Current);
    }

    [Fact]
    public async Task 最后页面关闭后忽略结束事件及新的播放命令()
    {
        await using var f = new PlaybackFixture();
        await using var owner = new MusicPlaybackLifetime(f.Queue);
        using var life = new MusicLifetime(); using var lease = new MusicPlaybackLease(owner, life);
        await f.Queue.ReplaceAsync([QueueEntry.FromTrack(MusicCatalog.Track(1)), QueueEntry.FromTrack(MusicCatalog.Track(2))], 0, default);
        var oldGeneration = f.Queue.Snapshot.Playback.Generation;
        life.Close(); await owner.Pending.WaitAsync(TimeSpan.FromSeconds(5));
        f.Audio.Emit(oldGeneration, PlaybackState.Ended);
        await f.Queue.PauseAsync(false, default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Queue.PlaySingleAsync(MusicCatalog.Track(3), default));
        await f.Player.StopAsync(); // 排空迟到终态进入的单曲尾任务。
        Assert.Single(f.Audio.Opened); Assert.Null(f.Audio.Current);
    }

    [Fact]
    public async Task Document构造失败和只释放Scope也能归还租约()
    {
        await using var f = new PlaybackFixture();
        var services = new ServiceCollection();
        services.AddSingleton(f.Queue);
        services.AddSingleton<MusicPlaybackLifetime>();
        services.AddScoped<IDocumentLifetime, MusicLifetime>();
        services.AddScoped<MusicPlaybackLease>();
        services.AddScoped<BrokenDocument>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using (var scope = provider.CreateScope())
        {
            Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<BrokenDocument>());
            await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        }
        Assert.Null(f.Audio.Current); Assert.Equal(1, f.Audio.SessionReleases);
    }

    private sealed class BrokenDocument
    {
        public BrokenDocument(MusicPlaybackLease lease) => throw new InvalidOperationException("fixture constructor failure");
    }

    [Fact]
    public async Task 插件关闭与Document关闭共用清理任务且容器彼此独立()
    {
        await using var a = new PlaybackFixture(); await using var b = new PlaybackFixture();
        await using var ownerA = new MusicPlaybackLifetime(a.Queue); await using var ownerB = new MusicPlaybackLifetime(b.Queue);
        using var lifeA = new MusicLifetime(); using var leaseA = new MusicPlaybackLease(ownerA, lifeA);
        using var lifeB = new MusicLifetime(); using var leaseB = new MusicPlaybackLease(ownerB, lifeB);
        await Task.WhenAll(a.Queue.PlaySingleAsync(MusicCatalog.Track(1), default), b.Queue.PlaySingleAsync(MusicCatalog.Track(2), default));
        lifeA.Close(); await ownerA.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, a.Audio.SessionReleases); Assert.Null(a.Audio.Current);
        Assert.Equal("2.media", b.Audio.Current); Assert.Equal(0, b.Audio.SessionReleases);
    }

    [Fact]
    public async Task 停止失败保留缓冲并报告关闭失败且禁止重开时绕过清理()
    {
        await using var f = new PlaybackFixture();
        var owner = new MusicPlaybackLifetime(f.Queue);
        using var life = new MusicLifetime(); var lease = new MusicPlaybackLease(owner, life);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        f.Audio.Stopping = () => throw new IOException("stop failed");
        life.Close();
        await Assert.ThrowsAsync<MusicException>(() => owner.Pending);
        Assert.Equal(PlaybackState.Failed, f.Queue.Snapshot.Playback.State);
        Assert.Empty(f.Buffer.Deleted); Assert.Equal(0, f.Audio.SessionReleases);
        using var reopenedLife = new MusicLifetime(); var reopened = new MusicPlaybackLease(owner, reopenedLife);
        await Assert.ThrowsAsync<MusicException>(() => f.Queue.PlaySingleAsync(MusicCatalog.Track(2), default));
        Assert.Single(f.Audio.Opened);
        f.Audio.Stopping = null;
        await Record.ExceptionAsync(() => owner.ShutdownAsync());
        Assert.NotNull(Record.Exception(lease.Dispose));
        Assert.NotNull(Record.Exception(reopened.Dispose));
        Assert.Null(f.Audio.Current);
    }
}
