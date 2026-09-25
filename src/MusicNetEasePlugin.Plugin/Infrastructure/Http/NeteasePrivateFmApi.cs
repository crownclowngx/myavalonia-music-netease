using System.Text.Json;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;
namespace MusicNetEasePlugin.Infrastructure.Http;
/// <summary>写入没有可信回查，保留未知且不自动重放；歌单令牌不套用到 FM。</summary>
internal sealed class NeteasePrivateFmApi(MusicRequestExecutor read, NeteaseTransport transport, IMusicSessionAccessor sessions) : IPrivateFmContentApi, IPrivateFmFeedbackApi
{
    public async Task<IReadOnlyList<MusicTrack>> ReadAsync(MusicSession session, CancellationToken ct) => (await read.ExecuteAsync(DiscoveryRequests.Fm(), session,
        json => DiscoveryJson.Page(DiscoveryJson.Required(json, "data", JsonValueKind.Array), DiscoveryJson.Song), ct).ConfigureAwait(false)).Items;
    public async Task<FmFeedbackResult> DislikeAsync(long trackId, MusicSession session, CancellationToken ct)
    {
        var request = DiscoveryRequests.Dislike(trackId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Revoked);
        if (linked.IsCancellationRequested || !sessions.IsCurrent(session)) return new(FmFeedbackState.NotSent, "反馈尚未发送。");
        FmFeedbackResult? receipt = null;
        try
        {
            var reply = await transport.SendAsync(request.Path, request.Data, request.Protocol, session.Context, linked.Token).ConfigureAwait(false);
            var code = NeteaseTransport.Code(reply.Body);
            receipt = code == 200 ? new(FmFeedbackState.Accepted, "已反馈不喜欢。")
                : code is 301 or 401 or 403 or 405 or 429 or 460 or -460 ? new(FmFeedbackState.Rejected, "反馈被拒绝，请稍后或在官方客户端核验账号。")
                : new(FmFeedbackState.Uncertain, "反馈结果未确认，未自动重发。");
            if (code is 301 or 401) await sessions.InvalidateAsync(session).ConfigureAwait(false);
            if (sessions.IsCurrent(session)) await sessions.CommitAsync(session, reply.Context, linked.Token).ConfigureAwait(false);
            return receipt;
        }
        catch (AuthException ex)
        {
            if (ex.Kind == AuthError.SessionExpired) await sessions.InvalidateAsync(session).ConfigureAwait(false);
            return receipt is not null ? receipt with { CredentialSaveFailed = ex.Kind == AuthError.Storage, Message = receipt.Message + " 会话保存未完成。" }
                : new(FmFeedbackState.Uncertain, "反馈结果未确认，未自动重发。");
        }
        catch (Exception ex) when (ex is OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return receipt ?? new(FmFeedbackState.Uncertain, "反馈结果未确认，未自动重发。"); }
    }
}
