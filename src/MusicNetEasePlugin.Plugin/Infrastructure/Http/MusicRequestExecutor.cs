using System.Text.Json;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>歌单/歌词共享的只读请求保护层：只处理会话、错误和取消，解析与内容语义仍属于各端点适配器。</summary>
internal sealed class MusicRequestExecutor(NeteaseTransport transport, IMusicSessionAccessor sessions)
{
    internal async Task<T> ExecuteAsync<T>(DailyPlayerRequest request, MusicSession session, Func<JsonElement, T> parse, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Revoked);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!sessions.IsCurrent(session)) throw new OperationCanceledException(linked.Token);
            var reply = await transport.SendAsync(request.Path, request.Data, request.Protocol, session.Context, linked.Token).ConfigureAwait(false);
            var code = NeteaseTransport.Code(reply.Body);
            if (code != 200) throw NeteaseTransport.Error(200, code);
            var value = parse(reply.Body);
            linked.Token.ThrowIfCancellationRequested();
            if (!sessions.IsCurrent(session)) throw new OperationCanceledException(linked.Token);
            await sessions.CommitAsync(session, reply.Context, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (!sessions.IsCurrent(session)) throw new OperationCanceledException(linked.Token);
            return value;
        }
        catch (AuthException ex)
        {
            if (ex.Kind == AuthError.SessionExpired) await sessions.InvalidateAsync(session).ConfigureAwait(false);
            throw new MusicException(ex.Kind switch
            {
                AuthError.SessionExpired => MusicError.SignedOut, AuthError.Network => MusicError.Network,
                AuthError.Timeout => MusicError.Timeout, AuthError.RateLimited => MusicError.RateLimited,
                AuthError.VerificationRequired => MusicError.Restricted, _ => MusicError.Protocol
            }, ex.Message, ex.RetryAfter);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new MusicException(MusicError.Protocol, "网易音乐数据不完整或格式已经变化，请刷新重试。"); }
    }
}
