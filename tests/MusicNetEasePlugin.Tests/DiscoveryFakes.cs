using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Discovery;
namespace MusicNetEasePlugin.Tests;

/// <summary>测试替身允许精确安排迟到和失败，不访问账号或业务网络。</summary>
internal sealed class DiscoveryFake : IDiscoveryCatalogApi, IArtistAlbumApi, IRemoteHistoryApi
{
    public int Reads;
    public Func<CancellationToken, Task<CatalogPage<MusicTrack>>> Daily = _ => Task.FromResult(new CatalogPage<MusicTrack>([Track(1), Track(2)]));
    public Func<long, int, ArtistSongOrder, CancellationToken, Task<CatalogPage<MusicTrack>>> Songs = (_, offset, _, _) => Task.FromResult(new CatalogPage<MusicTrack>([Track(offset + 1)], offset + 50, offset == 0, false));
    public Func<RecommendationKind, Task<CatalogPage<MusicCard>>> Lists = kind => Task.FromResult(new CatalogPage<MusicCard>([new(7, kind == RecommendationKind.Personal ? "个性歌单" : "通用歌单")]));
    public static MusicTrack Track(long id) => MusicCatalog.Track(id) with { ArtistRefs = [new(81, "第一歌手"), new(82, "第二歌手")], AlbumRef = new(91, "专辑") };
    public Task<CatalogPage<MusicTrack>> DailyAsync(MusicSession s, CancellationToken ct) { Reads++; return Daily(ct); }
    public Task<CatalogPage<MusicCard>> PlaylistsAsync(RecommendationKind kind, MusicSession s, CancellationToken ct) { Reads++; return Lists(kind); }
    public Task<CatalogPage<MusicCard>> ChartsAsync(MusicSession s, CancellationToken ct) { Reads++; return Task.FromResult(new CatalogPage<MusicCard>([new(9, "榜单", Description: "每周更新")])); }
    public Task<ArtistProfile> ArtistAsync(long id, MusicSession s, CancellationToken ct) { Reads++; return Task.FromResult(new ArtistProfile(id, "第一歌手", "介绍", null)); }
    public Task<CatalogPage<MusicTrack>> SongsAsync(long id, int offset, ArtistSongOrder order, MusicSession s, CancellationToken ct) { Reads++; return Songs(id, offset, order, ct); }
    public Task<CatalogPage<MusicCard>> AlbumsAsync(long id, int offset, MusicSession s, CancellationToken ct) { Reads++; return Task.FromResult(new CatalogPage<MusicCard>([new(91 + offset, "专辑")], offset + 50, offset == 0, false)); }
    public Task<AlbumContent> AlbumAsync(long id, MusicSession s, CancellationToken ct) { Reads++; return Task.FromResult(new AlbumContent(new(id, "作品专辑"), new([Track(3), Track(1)]))); }
    public Task<CatalogPage<RemoteListeningRecord>> ReadAsync(RemoteHistoryKind kind, MusicSession s, CancellationToken ct)
    { Reads++; return Task.FromResult(new CatalogPage<RemoteListeningRecord>([new(Track(3)), new(Track(3), DateTimeOffset.UnixEpoch, 4, 7)], Complete: false)); }
}
internal sealed class FmFake : IPrivateFmContentApi, IPrivateFmFeedbackApi
{
    public int Reads, Active, Maximum;
    public List<long> Writes { get; } = [];
    public Func<int, CancellationToken, Task<IReadOnlyList<MusicTrack>>> Content = (call, _) => Task.FromResult<IReadOnlyList<MusicTrack>>(Enumerable.Range(call * 10, 3).Select(i => DiscoveryFake.Track(i)).ToArray());
    public Func<long, CancellationToken, Task<FmFeedbackResult>> Feedback = (_, _) => Task.FromResult(new FmFeedbackResult(FmFeedbackState.Accepted, "已反馈"));
    public async Task<IReadOnlyList<MusicTrack>> ReadAsync(MusicSession s, CancellationToken ct)
    {
        var call = Interlocked.Increment(ref Reads); var active = Interlocked.Increment(ref Active); Maximum = Math.Max(Maximum, active);
        try { return await Content(call, ct); } finally { Interlocked.Decrement(ref Active); }
    }
    public Task<FmFeedbackResult> DislikeAsync(long id, MusicSession s, CancellationToken ct) { Writes.Add(id); return Feedback(id, ct); }
}
internal sealed class FmFixture : IAsyncDisposable
{
    public MusicSessions Sessions { get; } = new();
    public FmFake Api { get; } = new();
    public MusicCatalog Catalog { get; } = new();
    public MusicAudio Audio { get; } = new();
    public PlaybackCoordinator Single { get; }
    public PrivateFmCoordinator Fm { get; }
    public PlaybackQueueCoordinator Queue { get; }
    public FmFixture(TimeProvider? time = null, bool requireDocument = false, PlaybackPersistence? persistence = null)
    { Fm = new(Api, Api, Sessions, time ?? TimeProvider.System); Single = new(Sessions, Catalog, Catalog, new MusicBuffer(), Audio); Queue = new(Sessions, Single, persistence, requireDocument: requireDocument, fm: Fm); }
    public FmFeedbackTarget Target() { var s = Queue.Snapshot; return new(s.FmSessionId, s.CurrentEntryId!.Value, s.Playback.Track!.Id, s.AccountEpoch); }
    public async ValueTask DisposeAsync() { await Queue.DisposeAsync(); Fm.Dispose(); await Single.DisposeAsync(); await Audio.DisposeAsync(); Sessions.Dispose(); }
}
internal static class DiscoveryTestWait
{
    public static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 2000 && !condition(); i++) await Task.Delay(1); Xunit.Assert.True(condition(), "受控任务未到达预期同步点"); }
}
