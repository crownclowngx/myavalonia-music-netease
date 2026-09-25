using MusicNetEasePlugin.Application.Playback;
namespace MusicNetEasePlugin.Application.Discovery;
public interface IPrivateFmContentApi
{
    Task<IReadOnlyList<MusicTrack>> ReadAsync(MusicSession session, CancellationToken ct);
}
public enum FmFeedbackState { NotSent, Accepted, Rejected, Uncertain }
public sealed record FmFeedbackResult(FmFeedbackState State, string Message, bool CredentialSaveFailed = false);
/// <summary>反馈独立于取内容；取消发送后的等待不表示远端撤销，未知结果不得自动重试。</summary>
public interface IPrivateFmFeedbackApi
{
    Task<FmFeedbackResult> DislikeAsync(long trackId, MusicSession session, CancellationToken ct);
}
public enum FmContentState { Idle, Loading, Active, WaitingForContent, Exhausted, Failed }
public sealed record FmSnapshot(Guid SessionId, FmContentState State, string Message = "", bool FeedbackBusy = false, FmFeedbackResult? Feedback = null);
public sealed record FmFeedbackTarget(Guid SessionId, Guid EntryId, long TrackId, long AccountEpoch);
/// <summary>页面提交意图，唯一队列写入者负责切换与推进，内容服务不控制音频。</summary>
public interface IPrivateFmPlayer
{
    Task StartFmAsync(CancellationToken ct);
    void EndFm();
    Task DislikeFmAsync(FmFeedbackTarget target, CancellationToken ct);
}
