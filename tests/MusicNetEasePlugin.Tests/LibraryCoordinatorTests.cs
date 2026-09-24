using System.Collections.Immutable;
using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Library;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class LibraryCoordinatorTests
{
    [Fact]
    public async Task 收藏与取消的无变化以及权限中途改变不重复发送()
    {
        await using var f = new LibraryFixture();
        Assert.Equal(LibraryOutcome.Confirmed, (await f.Library.ExecuteAsync(new(LibraryOperationKind.Subscribe, 8, true))).Outcome);
        Assert.True((await f.Library.ExecuteAsync(new(LibraryOperationKind.Subscribe, 8, true))).NoChange);
        Assert.Equal(LibraryOutcome.Confirmed, (await f.Library.ExecuteAsync(new(LibraryOperationKind.Subscribe, 8, false))).Outcome);
        Assert.True((await f.Library.ExecuteAsync(new(LibraryOperationKind.Subscribe, 8, false))).NoChange);
        Assert.Equal(2, f.Api.Writes.Count); var baseline = f.Api.Playlists[7]; f.Api.Playlists[7] = baseline with { CreatorId = 456 };
        Assert.Equal(LibraryOutcome.NotSent, (await f.Library.ExecuteAsync(new(LibraryOperationKind.Delete, 7, Baseline: baseline))).Outcome); Assert.Equal(2, f.Api.Writes.Count);
    }
    [Fact]
    public async Task 初始未知与真实空喜欢集不同且多个读取共用在途任务()
    {
        await using var f = new LibraryFixture(); var pending = new TaskCompletionSource<ImmutableHashSet<long>>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Api.ReadLikes = _ => pending.Task;
        Assert.Null(f.Library.Snapshot.IsLiked(1));
        var a = f.Library.RefreshLikesAsync(); var b = f.Library.RefreshLikesAsync(); Assert.Equal(1, f.Api.LikeReads);
        pending.SetResult(ImmutableHashSet<long>.Empty); await Task.WhenAll(a, b);
        Assert.False(f.Library.Snapshot.IsLiked(1));
    }
    [Fact]
    public async Task 喜欢取消及无变化均来自回查且错误读取不变成未喜欢()
    {
        await using var f = new LibraryFixture();
        Assert.Equal(LibraryOutcome.Confirmed, (await f.Library.SetLikedAsync(11, true)).Outcome);
        Assert.True(f.Library.Snapshot.IsLiked(11));
        Assert.True((await f.Library.SetLikedAsync(11, true)).NoChange); Assert.Single(f.Api.Writes);
        f.Api.ReadLikes = _ => throw new LibraryReadException(LibraryReadError.Network, "断网");
        await f.Library.RefreshLikesAsync(); Assert.True(f.Library.Snapshot.IsLiked(11));
        f.Api.ReadLikes = null; await f.Library.SetLikedAsync(11, false);
        Assert.False(f.Library.Snapshot.IsLiked(11)); Assert.Equal(2, f.Api.Writes.Count);
    }
    [Fact]
    public async Task 重复与反向点击直接忙碌不排队且两个目标也串行()
    {
        await using var f = new LibraryFixture(); var send = new TaskCompletionSource<LibraryWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Api.Write = (_, _) => send.Task;
        var first = f.Library.SetLikedAsync(1, true);
        Assert.Equal(LibraryOutcome.Busy, (await f.Library.SetLikedAsync(1, false)).Outcome);
        Assert.Equal(LibraryOutcome.Busy, (await f.Library.SetLikedAsync(2, true)).Outcome);
        send.SetResult(new(LibraryReceiptState.Rejected, "拒绝")); await first;
        Assert.Single(f.Api.Writes); Assert.False(f.Library.Snapshot.IsBusy);
    }
    [Theory, InlineData(false), InlineData(true)]
    public async Task 旧账号迟到成功和清理不能污染新账号或相同账号新代次(bool sameAccount)
    {
        await using var f = new LibraryFixture(); var old = new TaskCompletionSource<LibraryWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Api.Write = (_, _) => old.Task; var a = f.Library.SetLikedAsync(1, true);
        f.Sessions.Relogin(); if (!sameAccount) f.Sessions.AccountId = 456;
        var current = new TaskCompletionSource<LibraryWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Api.Write = (_, _) => current.Task; var b = f.Library.SetLikedAsync(2, true);
        old.SetResult(new(LibraryReceiptState.Accepted)); Assert.Equal(LibraryOutcome.StaleSession, (await a).Outcome);
        Assert.True(f.Library.Snapshot.IsBusy); Assert.Equal(f.Sessions.AccountId, f.Library.Snapshot.AccountId);
        current.SetResult(new(LibraryReceiptState.Rejected, "当前拒绝")); await b; Assert.False(f.Library.Snapshot.IsBusy);
    }
    [Fact]
    public async Task 写前旧全量读取不能覆盖新的喜欢确认()
    {
        await using var f = new LibraryFixture(); var read = new TaskCompletionSource<ImmutableHashSet<long>>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Api.ReadLikes = _ => read.Task; var old = f.Library.RefreshLikesAsync();
        f.Api.ReadLikes = null; await f.Library.SetLikedAsync(7, true);
        read.SetResult(ImmutableHashSet<long>.Empty); await old; Assert.True(f.Library.Snapshot.IsLiked(7));
    }
    [Theory, InlineData(LibraryOperationKind.Subscribe, 8, ""), InlineData(LibraryOperationKind.Create, 0, "新歌单"), InlineData(LibraryOperationKind.Rename, 7, "新名称"), InlineData(LibraryOperationKind.Description, 7, ""), InlineData(LibraryOperationKind.Delete, 7, "")]
    public async Task 收藏创建两字段与删除各自完成写后确认(LibraryOperationKind kind, long id, string text)
    {
        await using var f = new LibraryFixture(); var result = await f.Library.ExecuteAsync(new(kind, id, true, text));
        Assert.Equal(LibraryOutcome.Confirmed, result.Outcome); Assert.Single(f.Api.Writes);
        if (kind == LibraryOperationKind.Create) Assert.Equal(99, result.Playlist!.Id);
        if (kind == LibraryOperationKind.Description) Assert.Equal("", f.Api.Playlists[7].Description);
        if (kind == LibraryOperationKind.Delete) Assert.False(f.Api.Playlists.ContainsKey(7));
    }
    [Fact]
    public async Task 所有权系统类型缺字段不完整和过期确认均阻止写入()
    {
        await using var f = new LibraryFixture();
        Assert.Equal(LibraryOutcome.NotSent, (await f.Library.ExecuteAsync(new(LibraryOperationKind.Delete, 8))).Outcome);
        f.Api.Playlists[7] = f.Api.Playlists[7] with { Kind = LibraryPlaylistKind.Liked };
        Assert.Equal(LibraryOutcome.NotSent, (await f.Library.ExecuteAsync(new(LibraryOperationKind.Rename, 7, Text: "x"))).Outcome);
        f.Api.Playlists[7] = f.Api.Playlists[7] with { Kind = LibraryPlaylistKind.Normal, DescriptionKnown = false, IsComplete = false };
        Assert.Equal(LibraryOutcome.NotSent, (await f.Library.ExecuteAsync(new(LibraryOperationKind.Description, 7, Text: ""))).Outcome);
        Assert.Equal(LibraryOutcome.NotSent, (await f.Library.ExecuteAsync(new(LibraryOperationKind.AddTracks, 7, TrackIds: [3]))).Outcome);
        Assert.Equal(LibraryOutcome.StaleSession, (await f.Library.ExecuteAsync(new(LibraryOperationKind.Delete, 7, ExpectedEpoch: 99, ExpectedAccountId: 123))).Outcome);
        Assert.Empty(f.Api.Writes);
    }
    [Fact]
    public async Task 名称保存不重复提交描述且外部修改产生字段级冲突()
    {
        await using var f = new LibraryFixture(); var before = f.Api.Playlists[7];
        var name = await f.Library.ExecuteAsync(new(LibraryOperationKind.Rename, 7, Text: "新名称", Baseline: before));
        Assert.Equal(LibraryOutcome.Confirmed, name.Outcome);
        f.Api.Write = (_, _) => Task.FromResult(new LibraryWriteReceipt(LibraryReceiptState.Rejected, "描述拒绝"));
        await f.Library.ExecuteAsync(new(LibraryOperationKind.Description, 7, Text: "新描述", Baseline: before));
        Assert.Equal("新名称", f.Api.Playlists[7].Name); Assert.Equal("原描述", f.Api.Playlists[7].Description);
        Assert.Equal(LibraryOutcome.Conflict, (await f.Library.ExecuteAsync(new(LibraryOperationKind.Rename, 7, Text: "覆盖", Baseline: before))).Outcome);
        Assert.Equal(2, f.Api.Writes.Count);
    }
    [Theory, InlineData(true), InlineData(false)]
    public async Task 曲目去重差集写入并按回查分类原有或原不存在(bool add)
    {
        await using var f = new LibraryFixture();
        var result = await f.Library.ExecuteAsync(new(add ? LibraryOperationKind.AddTracks : LibraryOperationKind.RemoveTracks, 7, TrackIds: [1, 3, 3]));
        Assert.Equal(LibraryOutcome.Confirmed, result.Outcome); Assert.Equal(2, result.Items!.Count);
        Assert.Equal(new long[] { add ? 3 : 1 }, f.Api.Writes.Single().TrackIds);
        Assert.Equal(add ? 3 : 1, f.Api.Playlists[7].TrackCount);
    }
    [Fact]
    public async Task 创建丢失回执ID不按同名认领也不自动重放()
    {
        await using var f = new LibraryFixture();
        f.Api.Write = (i, _) => { f.Api.Apply(i); return Task.FromResult(new LibraryWriteReceipt(LibraryReceiptState.Uncertain)); };
        var result = await f.Library.ExecuteAsync(new(LibraryOperationKind.Create, Text: "同名"));
        Assert.Equal(LibraryOutcome.NeedsRecheck, result.Outcome);
        await f.Library.RecheckAsync("create"); await f.Library.ExecuteAsync(new(LibraryOperationKind.Create, Text: "同名"));
        Assert.Single(f.Api.Writes); Assert.Null(result.Playlist);
        f.Library.AcknowledgeUncertainCreate(123, f.Sessions.Epoch); Assert.Empty(f.Library.Snapshot.Pending!);
    }
    [Fact]
    public async Task 发送前取消零请求发送后取消保留待核实而非再次发送()
    {
        await using var f = new LibraryFixture(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal(LibraryOutcome.NotSent, (await f.Library.SetLikedAsync(1, true, cancelled.Token)).Outcome); Assert.Empty(f.Api.Writes);
        using var ct = new CancellationTokenSource();
        f.Api.Write = (_, token) => { ct.Cancel(); return Task.FromCanceled<LibraryWriteReceipt>(token); };
        Assert.Equal(LibraryOutcome.NeedsRecheck, (await f.Library.SetLikedAsync(1, true, ct.Token)).Outcome); Assert.Single(f.Api.Writes);
        Assert.Single(f.Library.Snapshot.Pending!);
    }
    [Fact]
    public async Task 回查三轮有界且部分曲目不会触发补偿或复制ID重试()
    {
        var time = new FakeTimeProvider(); await using var f = new LibraryFixture(time);
        f.Api.Write = (i, _) => { f.Api.Apply(i with { TrackIds = [3] }); return Task.FromResult(new LibraryWriteReceipt(LibraryReceiptState.Uncertain)); };
        var work = f.Library.ExecuteAsync(new(LibraryOperationKind.AddTracks, 7, TrackIds: [3, 4]));
        await AdvanceUntil(time, work);
        var result = await work; Assert.Equal(LibraryOutcome.PartiallyConfirmed, result.Outcome);
        Assert.Equal(new[] { LibraryItemState.Added, LibraryItemState.NeedsRecheck }, result.Items!.Select(i => i.State));
        Assert.Single(f.Api.Writes); Assert.Equal(4, f.Api.ReadIds.Count); Assert.False(f.Library.Snapshot.IsBusy);
    }
    [Fact]
    public async Task 删除不可访问和超时不能伪装为明确删除()
    {
        var time = new FakeTimeProvider(); await using var f = new LibraryFixture(time);
        f.Api.Write = (i, _) => { f.Api.Playlists.Remove(i.TargetId); return Task.FromResult(new LibraryWriteReceipt(LibraryReceiptState.Uncertain)); };
        var work = f.Library.ExecuteAsync(new(LibraryOperationKind.Delete, 7)); await AdvanceUntil(time, work);
        Assert.Equal(LibraryOutcome.NeedsRecheck, (await work).Outcome); Assert.Single(f.Api.Writes);
    }
    [Fact]
    public async Task 限流停止自动回查并在等待期后仅重新读取()
    {
        var time = new FakeTimeProvider(); await using var f = new LibraryFixture(time);
        f.Api.Write = (i, _) => { f.Api.Apply(i); return Task.FromResult(new LibraryWriteReceipt(LibraryReceiptState.Uncertain, "限流", StopRecheck: true, RetryAfter: TimeSpan.FromSeconds(10))); };
        await f.Library.SetLikedAsync(1, true); await f.Library.RecheckAsync("song:1"); Assert.Equal(1, f.Api.LikeReads);
        time.Advance(TimeSpan.FromSeconds(10)); var result = await f.Library.RecheckAsync("song:1");
        Assert.Equal(LibraryOutcome.Confirmed, result.Outcome); Assert.Single(f.Api.Writes);
    }
    [Fact]
    public async Task 关闭取消并等待同一个尾任务且重建服务不会重放()
    {
        using var sessions = new MusicSessions(); var api = new LibraryFake();
        var library = new MusicLibraryCoordinator(sessions, api, api, api);
        api.Write = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(LibraryReceiptState.Accepted); };
        var write = library.SetLikedAsync(1, true); var a = library.DisposeAsync().AsTask(); var b = library.DisposeAsync().AsTask();
        await Task.WhenAll(a, b, write); Assert.Equal(LibraryOutcome.StaleSession, (await write).Outcome); Assert.Equal(0, library.PendingTasks);
        await using var fresh = new MusicLibraryCoordinator(sessions, api, api, api); await fresh.RefreshLikesAsync(); Assert.Single(api.Writes);
    }
    internal static async Task AdvanceUntil(FakeTimeProvider time, Task work)
    {
        // 业务时间只由可控时钟推进。短暂让出线程池，避免忙循环在延续调度前耗尽整个虚拟预算。
        for (var i = 0; i < 1000 && !work.IsCompleted; i++) { time.Advance(TimeSpan.FromMilliseconds(100)); await Task.Delay(1); }
        Assert.True(work.IsCompleted, "可控时间预算内操作没有结束。");
    }
}
