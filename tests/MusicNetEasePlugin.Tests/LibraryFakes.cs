using System.Collections.Immutable;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Http;

namespace MusicNetEasePlugin.Tests;

/// <summary>替身模拟远端事实而非协调器内部状态，支持回执丢失、部分成功及故意迟到的响应。</summary>
internal sealed class LibraryFake : ILikedSongsApi, ILibraryPlaylistQuery, IPlaylistMutationApi
{
    public ImmutableHashSet<long> Likes = ImmutableHashSet<long>.Empty;
    public Dictionary<long, LibraryPlaylist> Playlists { get; } = new()
    {
        [7] = new(7, "自有歌单", "原描述", true, 123, LibraryPlaylistKind.Normal, false, new long[] { 1, 2 }, true, 2, 1),
        [8] = new(8, "他人的歌单", "", true, 456, LibraryPlaylistKind.Normal, false, new long[] { 3 }, true, 1, 1)
    };
    public List<LibraryIntent> Writes { get; } = [];
    public List<long> ReadIds { get; } = [];
    public int LikeReads;
    public Func<CancellationToken, Task<ImmutableHashSet<long>>>? ReadLikes;
    public Func<long, CancellationToken, Task<LibraryPlaylist>>? ReadPlaylist { get; set; }
    public Func<LibraryIntent, CancellationToken, Task<LibraryWriteReceipt>>? Write;
    public Task<ImmutableHashSet<long>> ReadAsync(MusicSession session, CancellationToken ct)
    { LikeReads++; return ReadLikes?.Invoke(ct) ?? Task.FromResult(Likes); }
    public Task<LibraryPlaylist> ReadAsync(long id, MusicSession session, CancellationToken ct)
    {
        ReadIds.Add(id);
        return ReadPlaylist?.Invoke(id, ct) ?? (Playlists.TryGetValue(id, out var p) ? Task.FromResult(p)
            : Task.FromException<LibraryPlaylist>(new LibraryReadException(LibraryReadError.MissingOrInaccessible, "测试歌单不存在。")));
    }
    public Task<LibraryWriteReceipt> SetAsync(long trackId, bool desired, MusicSession session, CancellationToken ct) =>
        WriteAsync(new(LibraryOperationKind.Like, trackId, desired), session, ct);
    public Task<LibraryWriteReceipt> WriteAsync(LibraryIntent intent, MusicSession session, CancellationToken ct)
    {
        Writes.Add(intent); if (Write is not null) return Write(intent, ct);
        return Task.FromResult(Apply(intent));
    }
    public LibraryWriteReceipt Apply(LibraryIntent intent)
    {
        if (intent.Kind == LibraryOperationKind.Like) { Likes = intent.Desired ? Likes.Add(intent.TargetId) : Likes.Remove(intent.TargetId); return new(LibraryReceiptState.Accepted); }
        if (intent.Kind == LibraryOperationKind.Create)
        { Playlists[99] = new(99, intent.Text!, "", true, 123, LibraryPlaylistKind.Normal, false, [], true, 0); return new(LibraryReceiptState.Accepted, CreatedId: 99, ServerName: intent.Text); }
        if (intent.Kind == LibraryOperationKind.Delete) { Playlists.Remove(intent.TargetId); return new(LibraryReceiptState.Accepted); }
        var p = Playlists[intent.TargetId];
        p = intent.Kind switch
        {
            LibraryOperationKind.Subscribe => p with { Subscribed = intent.Desired },
            LibraryOperationKind.Rename => p with { Name = intent.Text! },
            LibraryOperationKind.Description => p with { Description = intent.Text! },
            LibraryOperationKind.AddTracks => p with { TrackIds = p.TrackIds.Concat(intent.TrackIds!).Distinct().ToArray() },
            LibraryOperationKind.RemoveTracks => p with { TrackIds = p.TrackIds.Where(id => !intent.TrackIds!.Contains(id)).ToArray() },
            _ => p
        };
        Playlists[p.Id] = p with { TrackCount = p.TrackIds.Count };
        return new(LibraryReceiptState.Accepted);
    }
}
internal sealed class LibraryFixture : IAsyncDisposable
{
    public MusicSessions Sessions { get; } = new();
    public LibraryFake Api { get; } = new();
    public MusicLibraryCoordinator Library { get; }
    public LibraryFixture(TimeProvider? time = null) => Library = new(Sessions, Api, Api, Api, time);
    public async ValueTask DisposeAsync() { await Library.DisposeAsync(); Sessions.Dispose(); }
}
internal sealed class LibraryTokenFake : ILibraryCheckTokenProvider
{
    public int Calls;
    public Func<CancellationToken, Task<LibraryCheckToken>> Get { get; set; } = _ => Task.FromResult(new LibraryCheckToken("fresh-test-token"));
    public Task<LibraryCheckToken> GetAsync(CancellationToken ct) { Calls++; return Get(ct); }
}
