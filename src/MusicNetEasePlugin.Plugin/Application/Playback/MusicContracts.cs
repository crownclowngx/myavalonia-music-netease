using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Application.Playback;

/// <summary>音乐请求获得的内部快照；不可放进 ViewModel。账号代次区别于登录界面的刷新序号。</summary>
public sealed record MusicSession(long Epoch, long CredentialVersion, AuthContext Context, CancellationToken Revoked, long AccountId = 0)
{
    public override string ToString() => "[音乐会话快照]";
}
public interface IMusicSessionAccessor
{
    MusicSession Capture();
    bool IsCurrent(MusicSession session);
    Task CommitAsync(MusicSession session, AuthContext context, CancellationToken ct);
    Task InvalidateAsync(MusicSession session);
}
public sealed record MusicTrack(long Id, string Name, string Artists, string Album, string? Cover, long DurationMs)
{
    public string Display => $"{Name} — {Artists}";
}
public sealed record MusicSearchPage(IReadOnlyList<MusicTrack> Tracks, int Offset, bool HasMore);
public interface IMusicCatalogApi
{
    Task<MusicSearchPage> SearchAsync(string keyword, int offset, MusicSession session, CancellationToken ct);
    Task<MusicTrack> DetailAsync(long id, MusicSession session, CancellationToken ct);
}
/// <summary>带签名的资源仅在播放链内短期存在，默认字符串不输出 URL。</summary>
public sealed record PlaybackResource(long Id, Uri? Address, string Format, string Quality, bool IsTrial, long? TrialDurationMs, long? TrialStartMs = null)
{
    public override string ToString() => "[临时播放资源]";
}
public interface IPlaybackResourceResolver
{
    Task<PlaybackResource> ResolveAsync(long id, MusicSession session, CancellationToken ct);
}

/// <summary>媒体句柄由播放协调器拥有；必须先让输出解绑，再 Dispose 清除临时文件。</summary>
public sealed class BufferedMedia(string path, Action<string> cleanup) : IDisposable
{
    private int _disposed;
    public string Path { get; } = path;
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) cleanup(Path); }
}
/// <summary>仅描述本次完整文件下载；未知 Content-Length 保持 null，不虚构完成百分比。</summary>
public sealed record BufferProgress(long BytesRead, long? TotalBytes);
public interface IMediaBuffer { Task<BufferedMedia> DownloadAsync(PlaybackResource resource, CancellationToken ct, Action<BufferProgress>? progress = null); }

public enum PlaybackState { Idle, Loading, Playing, Paused, Stopped, Ended, Failed }
public sealed record AudioProgress(long Generation, PlaybackState State, long PositionMs, long DurationMs, string? Error = null, bool CanSeek = false, bool StartPositionReset = false);
/// <summary>
/// 本地音频边界，调用者按顺序 await 控制命令，事件可从任意线程到达且必须携带原播放代次。
/// StopAsync 返回后不再持有本次媒体文件；Dispose 仅负责本适配持有的播放器，不销毁外部共享库。
/// </summary>
public interface IAudioOutput : IAsyncDisposable
{
    event EventHandler<AudioProgress>? Changed;
    /// <summary>从指定本地媒体时间启动；起点在创建输入时设置，禁止先播放开头再跳转。恢复界面本身不得调用此方法。</summary>
    Task OpenAsync(string path, long generation, CancellationToken ct, long startPositionMs = 0);
    Task PauseAsync(bool paused, CancellationToken ct, long? generation = null);
    /// <summary>只控制指定代次的当前媒体；在原生控制门内重新检查代次，保留暂停状态，位置以事实回报为准。</summary>
    Task SeekAsync(long generation, long positionMs, CancellationToken ct);
    Task StopAsync();
    Task SetVolumeAsync(int volume, CancellationToken ct);
}
public sealed record PlaybackSnapshot(long Revision, PlaybackState State, MusicTrack? Track = null,
    long PositionMs = 0, long DurationMs = 0, int Volume = 70, bool IsTrial = false, string Message = "",
    long Generation = 0, Guid AttemptId = default, MusicError? Error = null, bool CanSeek = false,
    bool IsSeeking = false, long SeekRevision = 0, BufferProgress? Buffer = null, long? TrialDurationMs = null);
