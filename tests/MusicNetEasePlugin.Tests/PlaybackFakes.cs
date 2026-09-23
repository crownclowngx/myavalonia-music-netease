using System.Collections.Concurrent;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MyAvaloniaManagement.PluginSdk;

namespace MusicNetEasePlugin.Tests;

// 用独立完成源控制迟到响应；替身刻意允许忽略取消，验证协调器自己的代次屏障。
internal sealed class MusicSessions : IMusicSessionAccessor, IDisposable
{
    private CancellationTokenSource _revoked = new();
    public long Epoch { get; private set; } = 1;
    public bool SignedIn { get; set; } = true;
    public long AccountId { get; set; } = 123;
    public int Commits { get; private set; }
    public MusicSession Capture() => SignedIn ? new(Epoch, 1, FakeAuthApi.Authorized(AuthContext.Create()), _revoked.Token, AccountId)
        : throw new MusicException(MusicError.SignedOut, "请先登录。");
    public bool IsCurrent(MusicSession session) => SignedIn && session.Epoch == Epoch && !session.Revoked.IsCancellationRequested;
    public Task CommitAsync(MusicSession session, AuthContext context, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); if (IsCurrent(session)) Commits++; return Task.CompletedTask; }
    public Task InvalidateAsync(MusicSession session) { if (IsCurrent(session)) Revoke(); return Task.CompletedTask; }
    public void Revoke() { SignedIn = false; _revoked.Cancel(); }
    public void Relogin() { Revoke(); _revoked.Dispose(); _revoked = new(); Epoch++; SignedIn = true; }
    public void Dispose() { _revoked.Cancel(); _revoked.Dispose(); }
}
internal sealed class MusicCatalog : IMusicCatalogApi, IPlaybackResourceResolver
{
    public static MusicTrack Track(long id) => new(id, "歌曲" + id, "歌手", "专辑", null, 120000);
    public Func<long, CancellationToken, Task<MusicTrack>> Detail { get; set; } = (id, _) => Task.FromResult(Track(id));
    public Func<string, int, CancellationToken, Task<MusicSearchPage>> Search { get; set; } = (_, offset, _) => Task.FromResult(new MusicSearchPage([Track(1)], offset, false));
    public Func<long, CancellationToken, Task<PlaybackResource>> Resource { get; set; } = (id, _) => Task.FromResult(new PlaybackResource(id, new Uri("https://m1.music.126.net/fixture"), "mp3", "standard", false, null));
    public int Resolves { get; private set; }
    public Task<MusicTrack> DetailAsync(long id, MusicSession s, CancellationToken ct) => Detail(id, ct);
    public Task<MusicSearchPage> SearchAsync(string keyword, int offset, MusicSession s, CancellationToken ct) => Search(keyword, offset, ct);
    public Task<PlaybackResource> ResolveAsync(long id, MusicSession s, CancellationToken ct) { Resolves++; return Resource(id, ct); }
}
internal sealed class MusicBuffer : IMediaBuffer
{
    public ConcurrentQueue<string> Deleted { get; } = new();
    public Func<PlaybackResource, CancellationToken, Task<BufferedMedia>>? Download { get; set; }
    public Task<BufferedMedia> DownloadAsync(PlaybackResource resource, CancellationToken ct) => Download?.Invoke(resource, ct)
        ?? Task.FromResult(new BufferedMedia(resource.Id + ".media", Deleted.Enqueue));
}
internal sealed class MusicAudio : IAudioOutput
{
    public event EventHandler<AudioProgress>? Changed;
    public ConcurrentQueue<(string Path, long Generation)> Opened { get; } = new();
    public string? Current { get; private set; }
    public int Volume { get; private set; }
    public int Stops { get; private set; }
    public bool AutoStart { get; set; } = true;
    public Func<CancellationToken, Task>? Opening { get; set; }
    public Func<CancellationToken, Task>? Pausing { get; set; }
    public Func<Task>? Stopping { get; set; }
    public async Task OpenAsync(string path, long generation, CancellationToken ct, long startPositionMs = 0)
    {
        if (Opening is not null) await Opening(ct);
        ct.ThrowIfCancellationRequested();
        if (Current is not null) throw new InvalidOperationException("出现第二个同时持有的音源");
        Current = path; Opened.Enqueue((path, generation));
        if (AutoStart) Emit(generation, PlaybackState.Playing, startPositionMs);
    }
    public async Task PauseAsync(bool paused, CancellationToken ct, long? generation = null) { if (Pausing is not null) await Pausing(ct); ct.ThrowIfCancellationRequested(); }
    public Task SeekAsync(long generation, long positionMs, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Emit(generation, PlaybackState.Playing, positionMs); return Task.CompletedTask; }
    public async Task StopAsync() { if (Stopping is not null) await Stopping(); Current = null; Stops++; }
    public Task SetVolumeAsync(int volume, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Volume = volume; return Task.CompletedTask; }
    public void Emit(long generation, PlaybackState state, long position = 0) => Changed?.Invoke(this, new(generation, state, position, 120000, state == PlaybackState.Failed ? "设备失败" : null));
    public ValueTask DisposeAsync() { Current = null; Changed = null; return ValueTask.CompletedTask; }
}
internal sealed class PlaybackFixture : IAsyncDisposable
{
    public MusicSessions Sessions { get; } = new();
    public MusicCatalog Catalog { get; } = new();
    public MusicBuffer Buffer { get; } = new();
    public MusicAudio Audio { get; } = new();
    public PlaybackCoordinator Player { get; }
    public PlaybackQueueCoordinator Queue { get; }
    public Guid Owner { get; } = Guid.NewGuid();
    public PlaybackFixture() { Player = new(Sessions, Catalog, Catalog, Buffer, Audio); Queue = new(Sessions, Player); }
    public Task Play(long id = 1) => Player.PlayAsync(Owner, MusicCatalog.Track(id), default);
    public async ValueTask DisposeAsync() { await Queue.DisposeAsync(); await Player.DisposeAsync(); await Audio.DisposeAsync(); Sessions.Dispose(); }
}
internal sealed class ImmediateUi : ILoginUiDispatcher { public void Post(Action action) => action(); }
internal sealed class MusicLifetime : IDocumentLifetime, IDisposable
{
    private readonly CancellationTokenSource _source = new();
    public CancellationToken ClosingToken => _source.Token;
    public bool IsClosing => _source.IsCancellationRequested;
    public void Close() => _source.Cancel();
    public void Dispose() { Close(); _source.Dispose(); }
}
internal sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netease-m1-tests", Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
}
