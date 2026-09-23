using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Persistence;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class PlaybackPersistenceTests
{
    [Fact, Trait("M2", "B08,H07,H08")]
    public async Task 续播失败保留目标且权限范围缩短会从有效起点开始()
    {
        var store = new MemoryStore(); store.Data[123] = State(123, 1, 4500);
        await using var f = new Fixture(store); await f.Activate();
        f.Audio.Opening = _ => throw new MusicException(MusicError.Device, "设备不可用"); await f.Queue.PauseAsync(false, default);
        Assert.Equal(PlaybackState.Failed, f.Queue.Snapshot.Playback.State); Assert.Equal(4500, f.Queue.Snapshot.Playback.PositionMs); Assert.Empty(f.Audio.Opened);
        f.Audio.Opening = null; f.Catalog.Resource = (id, _) => Task.FromResult(new PlaybackResource(id, new("https://m1.music.126.net/fixture"), "mp3", "standard", true, 2000, 60000));
        await f.Queue.PauseAsync(false, default); Assert.Equal(0, f.Queue.Snapshot.Playback.PositionMs); Assert.Equal(2000, f.Queue.Snapshot.Playback.DurationMs);
        Assert.Contains("超出", f.Queue.Snapshot.Playback.Message); Assert.Equal(2, f.Catalog.Resolves);
        await f.Queue.ClearAsync(); f.Time.Advance(TimeSpan.FromMilliseconds(500)); await f.Persistence.Pending;
        Assert.Empty(f.Store.Data[123].Entries); Assert.Equal(0, f.Store.Data[123].PositionMs);
    }
    [Fact, Trait("M2", "A01,A02,A04,H07,H08,C05")]
    public async Task 生产账号事件只在核验后恢复且容器关闭等待最终位置保存()
    {
        using var directory = new TestDirectory(); var store = new MemoryStore(); store.Data[123] = State(123, 1, 3000);
        var loginStore = new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()) };
        var catalog = new MusicCatalog(); var audio = new MusicAudio(); var services = new ServiceCollection();
        services.AddSingleton<ILoginSessionStore>(loginStore); services.AddSingleton<INeteaseAuthApi>(new FakeAuthApi());
        services.AddSingleton<IPlaybackStateStore>(store); services.AddSingleton<IMusicCatalogApi>(catalog); services.AddSingleton<IPlaybackResourceResolver>(catalog);
        services.AddSingleton<IAudioOutput>(audio); services.AddSingleton<IMediaBuffer>(new MusicBuffer());
        services.AddMusicNetEasePluginServices(directory.Path);
        using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }))
        {
            var accounts = provider.GetRequiredService<PlayerAccountCoordinator>(); var login = provider.GetRequiredService<LoginCoordinator>();
            var queue = provider.GetRequiredService<PlaybackQueueCoordinator>(); var persistence = provider.GetRequiredService<PlaybackPersistence>();
            Assert.Equal(0, persistence.Snapshot.AccountId); Assert.Empty(queue.Snapshot.Entries);
            await login.RestoreAsync(Guid.NewGuid(), default); await accounts.Pending;
            Assert.Equal(PlaybackState.Paused, queue.Snapshot.Playback.State); Assert.Empty(audio.Opened); Assert.Equal(0, catalog.Resolves);
            await queue.PauseAsync(false, default); audio.Emit(queue.Snapshot.Playback.Generation, PlaybackState.Playing, 3400);
            await login.InvalidateMusicSessionAsync(login.CaptureMusicSession()); await accounts.Pending;
            Assert.Empty(queue.Snapshot.Entries); Assert.Equal(3400, store.Data[123].PositionMs);
            loginStore.Saved = FakeAuthApi.Authorized(AuthContext.Create()); await login.RestoreAsync(Guid.NewGuid(), default, true); await accounts.Pending;
            Assert.Equal(PlaybackState.Paused, queue.Snapshot.Playback.State);
            await login.LogoutAsync(Guid.NewGuid(), default); await accounts.Pending; Assert.Empty(store.Data[123].Entries); Assert.Single(store.Data[123].Recent);
            loginStore.Saved = FakeAuthApi.Authorized(AuthContext.Create()); await login.RestoreAsync(Guid.NewGuid(), default, true); await accounts.Pending;
            await queue.PlaySingleAsync(MusicCatalog.Track(2), default); audio.Emit(queue.Snapshot.Playback.Generation, PlaybackState.Playing, 1200);
        }
        Assert.Null(audio.Current); Assert.Equal(1200, store.Data[123].PositionMs); Assert.Equal(2, store.Data[123].Entries.Single().TrackId);
    }
    [Fact, Trait("M2", "H01,H06")]
    public async Task 只有非定位正常推进登记历史且重播去重和上限二百()
    {
        await using var f = new Fixture(); await f.Activate();
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default); Assert.Empty(f.Persistence.Snapshot.Recent);
        var s = f.Queue.Snapshot;
        await f.Queue.SeekAsync(s.CurrentEntryId!.Value, s.Playback.Generation, 3000, default); Assert.Empty(f.Persistence.Snapshot.Recent);
        f.Audio.Emit(s.Playback.Generation, PlaybackState.Playing, 3200); Assert.Single(f.Persistence.Snapshot.Recent);
        for (var id = 2; id <= 202; id++)
        {
            await f.Queue.PlaySingleAsync(MusicCatalog.Track(id), default);
            f.Audio.Emit(f.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 500);
        }
        Assert.Equal(200, f.Persistence.Snapshot.Recent.Count); Assert.Equal(202, f.Persistence.Snapshot.Recent[0].TrackId);
        Assert.DoesNotContain(f.Persistence.Snapshot.Recent, item => item.TrackId == 1);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(201), default); f.Audio.Emit(f.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 500);
        Assert.Equal(200, f.Persistence.Snapshot.Recent.Count); Assert.Equal(201, f.Persistence.Snapshot.Recent[0].TrackId);
        f.Time.Advance(TimeSpan.FromMilliseconds(500)); await f.Persistence.Pending;
        Assert.Single(f.Store.Saves);
    }
    [Fact, Trait("M2", "H02,H06,H07,B08,A01")]
    public async Task 重启恢复队列重复身份与位置保持静默直到用户继续()
    {
        var store = new MemoryStore(); Guid current;
        await using (var first = new Fixture(store))
        {
            await first.Activate(); var entries = new[] { QueueEntry.FromTrack(MusicCatalog.Track(1)), QueueEntry.FromTrack(MusicCatalog.Track(1)) };
            current = entries[1].EntryId; await first.Queue.ReplaceAsync(entries, 1, default); first.Queue.SetMode(PlaybackMode.RepeatList);
            await first.Queue.SetVolumeAsync(36, default); first.Audio.Emit(first.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 4500);
        }
        Assert.Equal(4500, store.Data[123].PositionMs); Assert.Equal(current, store.Data[123].CurrentEntryId);
        await using var second = new Fixture(store); await second.Activate();
        Assert.Empty(second.Audio.Opened); Assert.Equal(0, second.Catalog.Resolves);
        Assert.Equal(PlaybackState.Paused, second.Queue.Snapshot.Playback.State); Assert.Equal(4500, second.Queue.Snapshot.Playback.PositionMs);
        Assert.Equal(2, second.Queue.Snapshot.Entries.Count); Assert.Equal(2, second.Queue.Snapshot.Entries.Select(entry => entry.EntryId).Distinct().Count());
        Assert.Equal(36, second.Queue.Snapshot.Playback.Volume); Assert.Equal(PlaybackMode.RepeatList, second.Queue.Snapshot.Mode);
        await second.Queue.PauseAsync(false, default);
        Assert.Single(second.Audio.Opened); Assert.Equal(1, second.Catalog.Resolves); Assert.Equal(4500, second.Queue.Snapshot.Playback.PositionMs);
    }
    [Fact, Trait("M2", "H06,C05,A04")]
    public async Task 高频进度五秒保存且暂停和重复关闭保存停止前位置()
    {
        await using var f = new Fixture(); await f.Activate(); await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        f.Time.Advance(TimeSpan.FromMilliseconds(500)); await f.Persistence.Pending; var count = f.Store.Saves.Count;
        var generation = f.Queue.Snapshot.Playback.Generation;
        for (var i = 1; i <= 50; i++) f.Audio.Emit(generation, PlaybackState.Playing, i * 50);
        f.Time.Advance(TimeSpan.FromMilliseconds(500)); await f.Persistence.Pending; Assert.InRange(f.Store.Saves.Count - count, 0, 1);
        count = f.Store.Saves.Count; f.Time.Advance(TimeSpan.FromSeconds(5)); f.Audio.Emit(generation, PlaybackState.Playing, 6000); await f.Persistence.Pending;
        Assert.Equal(count + 1, f.Store.Saves.Count); Assert.Equal(6000, f.Store.Data[123].PositionMs);
        f.Audio.Emit(generation, PlaybackState.Playing, 6300); await f.Queue.PauseAsync(true, default); await f.Persistence.Pending;
        Assert.Equal(6300, f.Store.Data[123].PositionMs);
        var first = f.Queue.DisposeAsync().AsTask(); var second = f.Queue.DisposeAsync().AsTask(); Assert.Same(first, second); await first;
        Assert.Equal(6300, f.Store.Data[123].PositionMs); Assert.Null(f.Audio.Current);
    }
    [Fact, Trait("M2", "H04,H05,H07")]
    public async Task 损坏文件不被初始化或音量覆盖但明确新建队列允许保存()
    {
        var store = new MemoryStore { Read = (_, _) => Task.FromResult(new PlaybackStateRead(null, "损坏", true)) };
        await using var f = new Fixture(store); await f.Activate(); await f.Queue.SetVolumeAsync(12, default); await f.Persistence.FlushAsync(); Assert.Empty(store.Saves);
        Assert.Contains("损坏", f.Persistence.Snapshot.Message);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(7), default); await f.Persistence.FlushAsync(); Assert.Single(store.Saves); Assert.Equal(7, store.Saves[0].Entries[0].TrackId);
        store.FailSave = true; await f.Queue.SetVolumeAsync(14, default); await f.Persistence.FlushAsync(); Assert.Contains("未保存", f.Persistence.Snapshot.Message); Assert.Equal(PlaybackState.Playing, f.Queue.Snapshot.Playback.State);
        store.FailSave = false; await f.Persistence.RetryAsync(); Assert.Empty(f.Persistence.Snapshot.Message); Assert.Equal(14, store.Data[123].Volume);
    }
    [Fact, Trait("M2", "H07,A02")]
    public async Task 迟到恢复不能覆盖用户新队列也不能跨账号()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<PlaybackStateRead>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemoryStore { Read = (_, _) => { entered.TrySetResult(); return release.Task; } };
        await using var f = new Fixture(store); var session = f.Sessions.Capture(); var expected = f.Queue.Snapshot.Revision;
        var load = f.Persistence.ActivateAsync(session, default); await entered.Task;
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(9), default);
        var saved = State(123, 4, 2500); release.SetResult(new(saved)); var read = await load;
        Assert.False(await f.Queue.RestoreAsync(session, read!, expected)); Assert.Equal(9, f.Queue.Snapshot.Playback.Track!.Id);
        f.Sessions.Relogin(); f.Sessions.AccountId = 456;
        Assert.False(await f.Queue.RestoreAsync(session, saved, f.Queue.Snapshot.Revision)); Assert.Empty(f.Queue.Snapshot.Entries);
    }
    [Fact, Trait("M2", "H08,C05")]
    public async Task 退出清理排在不响应取消的旧保存之后并保留账号历史()
    {
        await using var f = new Fixture(); await f.Activate(); await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        f.Audio.Emit(f.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 500);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.BeforeSave = async _ => { entered.TrySetResult(); await release.Task; };
        var old = f.Persistence.FlushAsync(); await entered.Task; f.Sessions.Revoke();
        var clear = f.Persistence.EndAccountAsync(true); Assert.Empty(f.Persistence.Snapshot.Recent);
        f.Store.BeforeSave = null; release.SetResult(); await Task.WhenAll(old, clear);
        Assert.Empty(f.Store.Data[123].Entries); Assert.Equal(0, f.Store.Data[123].PositionMs); Assert.Single(f.Store.Data[123].Recent);
        f.Sessions.Relogin(); await f.Activate(); Assert.Empty(f.Queue.Snapshot.Entries); Assert.Single(f.Persistence.Snapshot.Recent);
    }
    [Fact, Trait("M2", "H08")]
    public async Task 会话失效保留恢复而显式清理失败可见可重试()
    {
        await using var f = new Fixture(); await f.Activate(); await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default);
        f.Audio.Emit(f.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 800); f.Sessions.Revoke(); await f.Persistence.EndAccountAsync(false);
        Assert.Single(f.Store.Data[123].Entries); Assert.Equal(800, f.Store.Data[123].PositionMs);
        f.Sessions.Relogin(); await f.Activate(); Assert.Equal(PlaybackState.Paused, f.Queue.Snapshot.Playback.State);
        f.Store.FailSave = true; f.Sessions.Revoke(); await f.Persistence.EndAccountAsync(true);
        Assert.True(f.Persistence.Snapshot.CleanupRequired); Assert.Contains("清理失败", f.Persistence.Snapshot.Message);
        f.Store.FailSave = false; await f.Persistence.RetryAsync(); Assert.Empty(f.Store.Data[123].Entries); Assert.False(f.Persistence.Snapshot.CleanupRequired);
    }
    [Fact, Trait("M2", "H08,A02")]
    public async Task 多账号清理失败分别保留且重登不恢复已退出的旧队列()
    {
        await using var f = new Fixture();
        foreach (var id in new long[] { 123, 456 })
        {
            f.Sessions.Relogin(); f.Sessions.AccountId = id; await f.Activate();
            await f.Queue.PlaySingleAsync(MusicCatalog.Track(id), default); await f.Persistence.FlushAsync();
            f.Store.FailSave = true; f.Sessions.Revoke(); await f.Persistence.EndAccountAsync(true); f.Store.FailSave = false;
        }
        f.Sessions.Relogin(); f.Sessions.AccountId = 123; await f.Activate();
        Assert.Empty(f.Queue.Snapshot.Entries); Assert.True(f.Persistence.Snapshot.CleanupRequired);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(9), default); await f.Persistence.FlushAsync();
        // 用户明确新建 A 队列后只取消 A 的旧清理，B 仍须清空，重试不能误删 A 新队列。
        Assert.True(f.Persistence.Snapshot.CleanupRequired); await f.Persistence.RetryAsync();
        Assert.Equal(9, f.Store.Data[123].Entries.Single().TrackId); Assert.Empty(f.Store.Data[456].Entries);
        Assert.False(f.Persistence.Snapshot.CleanupRequired);
    }
    internal static PlaybackStateData State(long accountId, long trackId, long position)
    {
        var entry = StoredEntry.From(QueueEntry.FromTrack(MusicCatalog.Track(trackId)));
        return PlaybackStateData.Empty(accountId) with { Entries = new[] { entry }, CurrentEntryId = entry.EntryId, PositionMs = position };
    }
    internal sealed class MemoryStore : IPlaybackStateStore
    {
        internal Dictionary<long, PlaybackStateData> Data { get; } = [];
        internal List<PlaybackStateData> Saves { get; } = [];
        internal Func<long, CancellationToken, Task<PlaybackStateRead>>? Read { get; set; }
        internal Func<PlaybackStateData, Task>? BeforeSave { get; set; }
        internal bool FailSave { get; set; }
        public Task<PlaybackStateRead> LoadAsync(long accountId, CancellationToken ct) => Read?.Invoke(accountId, ct) ?? Task.FromResult(new PlaybackStateRead(Data.GetValueOrDefault(accountId) ?? PlaybackStateData.Empty(accountId)));
        public async Task SaveAsync(PlaybackStateData state, CancellationToken ct)
        { if (BeforeSave is { } before) await before(state); if (FailSave) throw new IOException("fixture"); Saves.Add(state); Data[state.AccountId] = state; }
    }
    internal sealed class Fixture : IAsyncDisposable
    {
        internal MusicSessions Sessions { get; } = new(); internal MusicCatalog Catalog { get; } = new(); internal MusicAudio Audio { get; } = new();
        internal FakeTimeProvider Time { get; } = new(); internal MemoryStore Store { get; }
        internal PlaybackPersistence Persistence { get; } internal PlaybackCoordinator Single { get; } internal PlaybackQueueCoordinator Queue { get; }
        internal Fixture(MemoryStore? store = null)
        { Store = store ?? new(); Persistence = new(Store, Time); Single = new(Sessions, Catalog, Catalog, new MusicBuffer(), Audio, Time); Queue = new(Sessions, Single, Persistence); }
        internal async Task Activate()
        { var session = Sessions.Capture(); var revision = Queue.Snapshot.Revision; var data = await Persistence.ActivateAsync(session, default); if (data is not null) await Queue.RestoreAsync(session, data, revision); }
        public async ValueTask DisposeAsync() { await Queue.DisposeAsync(); await Persistence.DisposeAsync(); await Single.DisposeAsync(); await Audio.DisposeAsync(); Sessions.Dispose(); }
    }
}
