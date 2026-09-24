using System.Text.Json;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>
/// M3 的薄请求保护层。读取与写回执分别处理：写成功之后的 Cookie 保存失败不能使调用方重发，
/// 非成功 HTTP 也可能已经产生副作用。编码、响应预算和连接寿命继续复用唯一传输实现。
/// </summary>
internal sealed class LibraryRequestExecutor(NeteaseTransport transport, IMusicSessionAccessor sessions, ILibraryCheckTokenProvider tokens)
{
    internal async Task<T> ReadAsync<T>(DailyPlayerRequest request, MusicSession session, Func<JsonElement, T> parse, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Revoked);
        try
        {
            Check(session, linked.Token);
            var reply = await transport.SendAsync(request.Path, request.Data, request.Protocol, session.Context, linked.Token).ConfigureAwait(false);
            Check(session, linked.Token);
            var code = NeteaseTransport.Code(reply.Body);
            if (code == 404) throw new LibraryReadException(LibraryReadError.MissingOrInaccessible, "歌单不存在或当前账号无法访问。");
            if (code != 200) throw NeteaseTransport.Error(200, code);
            var value = parse(reply.Body);
            await sessions.CommitAsync(session, reply.Context, linked.Token).ConfigureAwait(false);
            Check(session, linked.Token);
            return value;
        }
        catch (AuthException ex)
        {
            if (ex.Kind == AuthError.SessionExpired) await sessions.InvalidateAsync(session).ConfigureAwait(false);
            throw ReadError(ex);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new LibraryReadException(LibraryReadError.Protocol, "音乐库数据不完整或格式已变化，请重新读取。"); }
    }

    internal async Task<LibraryWriteReceipt> WriteAsync(DailyPlayerRequest request, MusicSession session,
        Func<JsonElement, LibraryWriteReceipt> parse, bool checkToken, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Revoked);
        LibraryCheckToken? token = null;
        try
        {
            Check(session, linked.Token);
            if (checkToken)
            {
                token = await tokens.GetAsync(linked.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token.Value) || token.Value.Length > 8192)
                    return new(LibraryReceiptState.NotSent, "无法取得有效收藏令牌，请稍后重新尝试。");
                Check(session, linked.Token);
            }
        }
        catch (OperationCanceledException) { return new(LibraryReceiptState.NotSent, "收藏准备已取消，尚未发送修改。"); }
        catch (LibraryReadException ex) { return new(LibraryReceiptState.NotSent, ex.Message, StopRecheck: true); }
        LibraryWriteReceipt? receipt = null;
        try
        {
            Check(session, linked.Token);
            var data = new Dictionary<string, object?>(request.Data);
            // 上游允许调用方传入 checkToken；这里同时使用本次新令牌，不携带固定样本令牌。
            if (checkToken && request.Path == "/api/playlist/subscribe") data["checkToken"] = token!.Value;
            var reply = await transport.SendAsync(request.Path, data, request.Protocol, session.Context, linked.Token, token?.Value).ConfigureAwait(false);
            Check(session, linked.Token);
            var code = NeteaseTransport.Code(reply.Body);
            if (code == 200) receipt = parse(reply.Body);
            else if (code is 301 or 401 or 403 or 405 or 429 or 460 or -460)
            {
                if (code is 301 or 401) await sessions.InvalidateAsync(session).ConfigureAwait(false);
                receipt = new(LibraryReceiptState.Rejected, code == 429 ? "请求过于频繁，请稍后操作。" : "账号权限不足或需要在官方客户端完成验证。", StopRecheck: true);
            }
            else receipt = new(LibraryReceiptState.Uncertain, "未取得明确的写回执，将重新读取确认。");
            try { await sessions.CommitAsync(session, reply.Context, linked.Token).ConfigureAwait(false); }
            catch (AuthException ex) when (ex.Kind == AuthError.Storage)
            { receipt = receipt with { CredentialSaveFailed = true, Message = "远端已有回执，但会话保存失败；请核实结果。" }; }
            Check(session, linked.Token);
            return receipt;
        }
        catch (AuthException ex)
        {
            if (ex.Kind == AuthError.SessionExpired) await sessions.InvalidateAsync(session).ConfigureAwait(false);
            if (receipt is not null) return receipt with { CredentialSaveFailed = ex.Kind == AuthError.Storage };
            return new(LibraryReceiptState.Uncertain, ReadError(ex).Message,
                StopRecheck: ex.Kind is AuthError.RateLimited or AuthError.VerificationRequired or AuthError.SessionExpired, RetryAfter: ex.RetryAfter);
        }
        catch (Exception ex) when (ex is OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return receipt ?? new(LibraryReceiptState.Uncertain, "操作可能已经生效，请重新读取结果；不会自动重发。"); }
    }
    private void Check(MusicSession session, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!sessions.IsCurrent(session)) throw new OperationCanceledException(ct);
    }
    private static LibraryReadException ReadError(AuthException ex) => new(ex.Kind switch
    {
        AuthError.SessionExpired => LibraryReadError.SignedOut,
        AuthError.RateLimited => LibraryReadError.RateLimited,
        AuthError.VerificationRequired => LibraryReadError.Restricted,
        AuthError.Timeout => LibraryReadError.Timeout,
        AuthError.Network => LibraryReadError.Network,
        _ => LibraryReadError.Protocol
    }, ex.Kind switch
    {
        AuthError.SessionExpired => "登录已失效，请重新登录。",
        AuthError.RateLimited => "请求过于频繁，请按提示稍后重新读取。",
        AuthError.VerificationRequired => "网易要求额外验证，请在官方客户端检查账号。",
        AuthError.Timeout => "网易请求超时。",
        AuthError.Network => "暂时无法读取网易音乐库。",
        _ => "网易音乐库响应未能确认。"
    }, ex.RetryAfter);
}
