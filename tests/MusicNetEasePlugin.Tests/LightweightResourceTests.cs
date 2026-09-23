using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Infrastructure.Persistence;
using MusicNetEasePlugin.Infrastructure.Ui;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class LightweightResourceTests
{
    [Fact, Trait("V5", "R01,R02,R03")]
    public async Task 下载最多两个且合并请求的取消只影响本消费者()
    {
        var source = new ControlledImages(); using var cache = new AccountImageCache(source);
        using var firstCancellation = new CancellationTokenSource();
        var first = cache.LoadAsync("a", firstCancellation.Token); var shared = cache.LoadAsync("a", default);
        var second = cache.LoadAsync("b", default); var waiting = cache.LoadAsync("c", default);
        var priority = cache.LoadPriorityAsync("current", default);
        await Until(() => source.Started.Count == 2); Assert.Equal(2, source.Started.Count);
        firstCancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(source.Cancelled.ContainsKey("a")); source.Complete("a", [1]);
        Assert.Equal(new byte[] { 1 }, await shared); await Until(() => source.Started.Count == 3);
        Assert.Equal("current", source.Started.Last()); source.Complete("current", [4]); await priority;
        await Until(() => source.Started.Count == 4); source.Complete("b", [2]); source.Complete("c", [3]);
        await Task.WhenAll(second, waiting);
        TestEvidence.Write("v5-image-transfer.json", new { schemaVersion = 1, realHost = false, observedPeakRequests = source.Peak, pendingAfterCompletion = cache.PendingCount, sharedDownloadCalls = source.Started.Count(x => x == "a"), consumerCancellationIsolated = true });
        Assert.Equal(2, source.Peak); Assert.Single(source.Started, x => x == "a");
        Assert.Equal(new byte[] { 1 }, await cache.LoadAsync("a", default)); Assert.Equal(4, source.Started.Count);
    }

    [Fact, Trait("V5", "R02,R03")]
    public async Task 最后消费者取消及账户清空均不允许迟到响应入缓存()
    {
        var source = new ControlledImages(); using var cache = new AccountImageCache(source);
        using var cancellation = new CancellationTokenSource(); var abandoned = cache.LoadAsync("old", cancellation.Token);
        await Until(() => source.Started.Count == 1); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        Assert.True(source.Cancelled.ContainsKey("old")); source.Complete("old", [9]);
        var accountRequest = cache.LoadAsync("account", default); await Until(() => source.Started.Count == 2);
        cache.Clear(); source.Complete("account", [8]); Assert.Null(await accountRequest);
        Assert.Equal(0, cache.CachedBytes); Assert.Equal(0, cache.PendingCount);
    }

    [Fact, Trait("V5", "R01,R02")]
    public async Task 压缩缓存按字节和条数淘汰且超大响应不驻留()
    {
        var calls = new ConcurrentDictionary<string, int>();
        using var cache = new AccountImageCache(new ImmediateImages(address =>
        { calls.AddOrUpdate(address!, 1, (_, n) => n + 1); return new byte[address == "oversize" ? 2 * 1024 * 1024 + 1 : 2 * 1024 * 1024]; }));
        for (var i = 0; i < 6; i++) await cache.LoadAsync(i.ToString(), default);
        Assert.Equal(AccountImageCache.ByteLimit, cache.CachedBytes);
        await cache.LoadAsync("0", default); Assert.Equal(2, calls["0"]);
        Assert.Null(await cache.LoadAsync("oversize", default)); Assert.Equal(AccountImageCache.ByteLimit, cache.CachedBytes);
        TestEvidence.Write("v5-image-compressed.json", new { schemaVersion = 1, realHost = false, observedCachedBytes = cache.CachedBytes, oldestWasFetchedAgain = calls["0"] == 2, oversizeRejected = true });
    }

    [Fact, Trait("V5", "R04,P02")]
    public async Task 界面偏好兼容旧版本且三个开关独立持久化()
    {
        using var directory = new TestDirectory(); var path = Path.Combine(directory.Path, "ui-preferences.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"reduceMotion\":true}");
        var store = new UiPreferencesStore(directory.Path);
        Assert.Equal(new(true, true, true), await store.LoadAsync(default));
        await store.SaveAsync(new(false, false, false), default);
        Assert.Equal(new(false, false, false), await store.LoadAsync(default));
        Assert.Contains("\"schemaVersion\":3", (await File.ReadAllTextAsync(path)).Replace(" ", ""));
    }
    internal sealed class ImmediateImages(Func<string?, byte[]?> load) : IAccountImageSource
    { public Task<byte[]?> LoadAsync(string? address, CancellationToken cancellationToken) => Task.FromResult(load(address)); }
    private sealed class ControlledImages : IAccountImageSource
    {
        public ConcurrentQueue<string> Started { get; } = new();
        public ConcurrentDictionary<string, bool> Cancelled { get; } = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]?>> _replies = new();
        private int _active; public int Peak;
        public async Task<byte[]?> LoadAsync(string? address, CancellationToken cancellationToken)
        {
            var reply = _replies.GetOrAdd(address!, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
            var active = Interlocked.Increment(ref _active); Peak = Math.Max(Peak, active); Started.Enqueue(address!);
            using var registration = cancellationToken.Register(() => Cancelled[address!] = true);
            try { return await reply.Task; } finally { Interlocked.Decrement(ref _active); }
        }
        public void Complete(string address, byte[] value) => _replies[address].TrySetResult(value);
    }
    internal static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10); Assert.True(condition()); }
}

[Collection("AvaloniaHeadless")]
public sealed class LightweightArtworkTests
{
    [Fact, Trait("V5", "R01,R03,R04,R05")]
    public async Task 解码有界且跨页面租约共享和缓存清理不释放正在使用的位图()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(() =>
        {
            using var cache = new ArtworkCache(); var leases = new List<ArtworkCache.Lease>();
            var first = Png(0); var a = cache.Acquire(first)!; var b = cache.Acquire(first)!;
            Assert.NotNull(a); Assert.Same(a.Image, b.Image); a.Dispose(); Assert.Equal(1, cache.Count);
            Assert.True(b.Image.PixelSize.Width <= 256); leases.Add(b);
            for (var i = 1; i < 24; i++) leases.Add(cache.Acquire(Png(i))!);
            Assert.All(leases, Assert.NotNull); Assert.Equal(24, cache.Count); Assert.Null(cache.Acquire(Png(25)));
            leases[1].Dispose(); using var replacement = cache.Acquire(Png(25)); Assert.NotNull(replacement);
            Assert.True(cache.Bytes <= ArtworkCache.ByteLimit); var observedEntries = cache.Count; var observedBytes = cache.Bytes;
            cache.Clear(); Assert.Null(cache.Acquire(first)); // 旧账号的活动引用尚未归还，不能从退休项重新取图。
            foreach (var lease in leases) lease.Dispose(); replacement!.Dispose(); Assert.Equal(0, cache.Count); Assert.Equal(0, cache.Bytes);
            Assert.Null(cache.Acquire([1, 2, 3]));
            var huge = first.ToArray(); System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(huge.AsSpan(16), int.MaxValue);
            Assert.Null(cache.Acquire(huge));
            TestEvidence.Write("v5-artwork-budget.json", new { schemaVersion = 1, realHost = false, observedEntries, observedBytes, maxEntries = 24, maxDecodedBytes = ArtworkCache.ByteLimit,
                maxNetworkRequests = 2, maxCompressedBytes = AccountImageCache.ByteLimit, retainedEntriesAfterRelease = cache.Count, sharedLease = true, oversizeRejected = true });
            return true;
        }, default);
    }

    [Fact, Trait("V5", "R03,R04,R05")]
    public async Task 图片随隐藏偏好和重挂释放引用且账户退出清空缓存()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var login = TestLogin.Create(new(), new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()) }, TimeProvider.System, LoginOptions.Default);
            await login.RestoreAsync(Guid.NewGuid(), default);
            using var preferences = new UiPreferences(new MemoryUiPreferences(), new ImmediateUi());
            using var context = new ArtworkContext(new LightweightResourceTests.ImmediateImages(_ => Png(1)), preferences, login, new LoginUiDispatcher());
            var image = new ArtworkImage { Bytes = Png(1), Width = 40, Height = 40 };
            ArtworkImage.SetContext(image, context); var window = new Window { Content = image };
            try
            {
                window.Show(); DesktopUiTests.Pump(window); Assert.NotNull(image.Source);
                image.IsVisible = false; Assert.Null(image.Source); image.IsVisible = true; Assert.NotNull(image.Source);
                preferences.ShowArtwork = false; Assert.Null(image.Source); preferences.ShowArtwork = true; Assert.NotNull(image.Source);
                for (var i = 0; i < 20; i++) { window.Content = null; Assert.Null(image.Source); window.Content = image; DesktopUiTests.Pump(window); Assert.NotNull(image.Source); }
                await login.LogoutAsync(Guid.NewGuid(), default); DesktopUiTests.Pump(window);
                Assert.Null(image.Source); Assert.Equal(0, context.Cache.Count);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    internal static byte[] Png(int shade)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 160));
        using (var drawing = bitmap.CreateDrawingContext()) drawing.FillRectangle(new SolidColorBrush(Color.FromRgb((byte)shade, 80, 100)), new Rect(0, 0, 320, 160));
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); return stream.ToArray();
    }
}
