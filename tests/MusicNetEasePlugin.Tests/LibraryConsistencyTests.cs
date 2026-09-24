using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Features.Library;
using MusicNetEasePlugin.Plugin;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class LibraryConsistencyTests
{
    [Fact]
    public async Task 实测串行去重旧代次丢弃与部分回查计数生成一致性证据()
    {
        await using var f = new LibraryFixture(); var release = new TaskCompletionSource<LibraryWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var maximum = 0;
        f.Api.Write = async (_, _) => { maximum = Math.Max(maximum, Interlocked.Increment(ref active)); try { return await release.Task; } finally { Interlocked.Decrement(ref active); } };
        var write = f.Library.SetLikedAsync(1, true); var duplicates = await Task.WhenAll(f.Library.SetLikedAsync(1, true), f.Library.SetLikedAsync(2, true));
        Assert.All(duplicates, r => Assert.Equal(LibraryOutcome.Busy, r.Outcome)); f.Api.Likes = f.Api.Likes.Add(1); release.SetResult(new(LibraryReceiptState.Accepted)); var result = await write;
        Assert.Equal(LibraryOutcome.Confirmed, result.Outcome); Assert.Equal(1, maximum); Assert.Single(f.Api.Writes);
        var oldRead = new TaskCompletionSource<ImmutableHashSet<long>>(TaskCreationOptions.RunContinuationsAsynchronously); f.Api.ReadLikes = _ => oldRead.Task; var reading = f.Library.RefreshLikesAsync();
        f.Api.ReadLikes = null; f.Api.Write = null; await f.Library.SetLikedAsync(2, true); oldRead.SetResult(ImmutableHashSet<long>.Empty); await reading;
        var lateOverwrites = f.Library.Snapshot.IsLiked(2) == true ? 0 : 1; Assert.Equal(0, lateOverwrites);
        var oldWrite = new TaskCompletionSource<LibraryWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously); f.Api.Write = (_, _) => oldWrite.Task;
        var old = f.Library.SetLikedAsync(3, true); f.Sessions.Relogin(); f.Sessions.AccountId = 456; f.Library.CaptureSession(); oldWrite.SetResult(new(LibraryReceiptState.Accepted));
        var oldResult = await old; var oldAccountApplied = oldResult.Outcome == LibraryOutcome.StaleSession ? 0 : 1; Assert.Equal(0, oldAccountApplied); Assert.Null(f.Library.Snapshot.Likes);

        var time = new FakeTimeProvider(); await using var partial = new LibraryFixture(time); var started = time.GetUtcNow();
        partial.Api.Write = (i, _) => { partial.Api.Apply(i with { TrackIds = [3] }); return Task.FromResult(new LibraryWriteReceipt(LibraryReceiptState.Uncertain)); };
        var operation = partial.Library.ExecuteAsync(new(LibraryOperationKind.AddTracks, 7, TrackIds: [1, 3, 4, 4])); await LibraryCoordinatorTests.AdvanceUntil(time, operation); var answer = await operation;
        Assert.Equal(LibraryOutcome.PartiallyConfirmed, answer.Outcome); Assert.Equal(3, answer.Items!.Count); Assert.Single(partial.Api.Writes); Assert.Equal(4, partial.Api.ReadIds.Count);
        var operationWrites = partial.Api.Writes.Count; partial.Api.Playlists[7] = partial.Api.Playlists[7] with { TrackIds = [1, 2, 3, 4], TrackCount = 4 };
        var reread = await partial.Library.RecheckAsync("playlist:7"); Assert.Equal(LibraryOutcome.Confirmed, reread.Outcome); Assert.Equal(operationWrites, partial.Api.Writes.Count);
        TestEvidence.Write("v7-library-consistency.json", new
        {
            schemaVersion = 1, realHost = false, maximumConcurrentWrites = maximum, duplicateWrites = f.Api.Writes.Count(i => i.TargetId == 1) - 1,
            oldAccountApplied, lateOverwrites,
            operations = new[] { new { operationId = answer.OperationId, targetId = 7, writes = operationWrites, recheckRounds = partial.Api.ReadIds.Count - 2,
                elapsedMs = (time.GetUtcNow() - started).TotalMilliseconds, inputUnique = new long[] { 1, 3, 4, 4 }.Distinct().Count(),
                items = answer.Items.Select(i => new { trackId = i.TrackId, state = i.State.ToString() }).ToArray() } }
        });
    }

    [Fact]
    public async Task 二十秒预算终止无响应回查且不重复写入()
    {
        var time = new FakeTimeProvider(); await using var f = new LibraryFixture(time);
        var never = new TaskCompletionSource<LibraryPlaylist>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Api.Write = (_, _) => { f.Api.ReadPlaylist = (_, _) => never.Task; return Task.FromResult(new LibraryWriteReceipt(LibraryReceiptState.Accepted)); };
        var started = time.GetUtcNow(); var operation = f.Library.ExecuteAsync(new(LibraryOperationKind.Rename, 7, Text: "新名称"));
        time.Advance(TimeSpan.FromSeconds(20)); var result = await operation.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(LibraryOutcome.NeedsRecheck, result.Outcome); Assert.Equal(TimeSpan.FromSeconds(20), time.GetUtcNow() - started); Assert.Single(f.Api.Writes); Assert.Equal(2, f.Api.ReadIds.Count);
        never.SetResult(f.Api.Playlists[7]); Assert.Single(f.Library.Snapshot.Pending!);
    }

    [Theory, InlineData(LibraryReadError.RateLimited), InlineData(LibraryReadError.Restricted), InlineData(LibraryReadError.SignedOut)]
    public async Task 回查限流验证或失效立即停止并保留回执(LibraryReadError error)
    {
        await using var f = new LibraryFixture();
        f.Api.Write = (_, _) => { f.Api.ReadLikes = _ => throw new LibraryReadException(error, "停止回查", TimeSpan.FromSeconds(5)); return Task.FromResult(new LibraryWriteReceipt(LibraryReceiptState.Accepted, CredentialSaveFailed: true)); };
        var result = await f.Library.SetLikedAsync(1, true); Assert.Equal(LibraryOutcome.NeedsRecheck, result.Outcome); Assert.True(result.Receipt!.CredentialSaveFailed);
        Assert.Contains("保存失败", result.Message); Assert.Single(f.Api.Writes); Assert.Equal(2, f.Api.LikeReads);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task 忽略取消的外部读取或写入不阻塞Shutdown(bool write)
    {
        using var sessions = new MusicSessions(); var api = new LibraryFake(); var library = new MusicLibraryCoordinator(sessions, api, api, api);
        var read = new TaskCompletionSource<ImmutableHashSet<long>>(TaskCreationOptions.RunContinuationsAsynchronously); var send = new TaskCompletionSource<LibraryWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (write) api.Write = (_, _) => send.Task; else api.ReadLikes = _ => read.Task;
        var task = write ? (Task)library.SetLikedAsync(1, true) : library.RefreshLikesAsync();
        await library.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); await task; Assert.Equal(0, library.PendingTasks);
        send.TrySetResult(new(LibraryReceiptState.Accepted)); read.TrySetResult(ImmutableHashSet<long>.Empty);
        Assert.Equal(LibraryOutcome.StaleSession, (await library.SetLikedAsync(2, true)).Outcome);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task 回执受理后等待点取消只保留待核实(bool beforeSecondRound)
    {
        var time = new FakeTimeProvider(); await using var f = new LibraryFixture(time); using var ct = new CancellationTokenSource();
        f.Api.Write = (_, _) => Task.FromResult(new LibraryWriteReceipt(LibraryReceiptState.Accepted));
        var work = f.Library.SetLikedAsync(1, true, ct.Token);
        if (beforeSecondRound) { time.Advance(TimeSpan.FromMilliseconds(500)); await Task.Delay(10); }
        ct.Cancel(); var result = await work; Assert.Equal(LibraryOutcome.NeedsRecheck, result.Outcome); Assert.Single(f.Api.Writes); Assert.False(f.Library.Snapshot.IsBusy);
    }

    [Fact]
    public void Host与Standalone共用注册入口且库共享草稿按Document隔离()
    {
        using var dir = new TestDirectory(); var services = new ServiceCollection().AddMusicNetEasePluginServices(dir.Path); services.AddMusicNetEasePluginServices(dir.Path);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(MusicLibraryCoordinator)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(PlaylistEditor)).Lifetime);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var a = provider.CreateScope(); using var b = provider.CreateScope();
        Assert.Same(a.ServiceProvider.GetRequiredService<MusicLibraryCoordinator>(), b.ServiceProvider.GetRequiredService<MusicLibraryCoordinator>());
        Assert.NotSame(a.ServiceProvider.GetRequiredService<PlaylistEditor>(), b.ServiceProvider.GetRequiredService<PlaylistEditor>());
    }
}
