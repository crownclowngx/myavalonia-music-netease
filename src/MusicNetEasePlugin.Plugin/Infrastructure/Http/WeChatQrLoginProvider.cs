using System.Text;
using Flurl;
using Flurl.Http;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Protocol;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>
/// 从网易发起微信 OAuth 网站登录，经微信长轮询取得 code，再由网易自己的回调完成账号兑换。
/// 应用不申请另一套 AppID、不保存 AppSecret，也不把微信 access_token 当成网易 Cookie。
/// </summary>
internal sealed class WeChatQrLoginProvider(NeteaseFlurlClients clients, TimeProvider time) : IQrLoginProvider
{
    public LoginMethod Method => LoginMethod.WeChat;

    public async Task<IQrLoginAttempt> CreateAsync(AuthContext context, CancellationToken cancellationToken)
    {
        var attempt = new Attempt(clients, time, context);
        try
        {
            await attempt.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return attempt;
        }
        catch { attempt.Dispose(); throw; }
    }

    private sealed record Reply(int Status, string? Location, byte[] Bytes);

    private sealed class Attempt(NeteaseFlurlClients clients, TimeProvider time, AuthContext context) : IQrLoginAttempt
    {
        private const string Agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36";
        // 临时 CookieJar 只属于本次尝试，并按域名/路径发送。网易会话绝不写到微信客户端的默认请求头。
        private readonly CookieJar _cookies = new();
        private AuthContext _context = context with { Cookies = context.Cookies.Clear() };
        private WeChatLoginProtocol.Authorization? _authorization;
        private WeChatLoginProtocol.QrPage? _qr;
        private AuthContext? _completed;
        private bool _callbackStarted;
        private bool _scanned;
        private bool _disposed;
        public byte[] Image { get; private set; } = [];

        internal async Task InitializeAsync(CancellationToken ct)
        {
            var redirect = await GetAsync(clients.Social, new Uri(WeChatLoginProtocol.Entry), ct).ConfigureAwait(false);
            if (redirect.Status is not (301 or 302 or 303 or 307 or 308)) throw WeChatLoginProtocol.Invalid();
            _authorization = WeChatLoginProtocol.ParseAuthorization(redirect.Location);
            var page = await GetAsync(clients.WeChat, _authorization.Page, ct).ConfigureAwait(false);
            EnsureSuccess(page);
            _qr = WeChatLoginProtocol.ParsePage(Encoding.UTF8.GetString(page.Bytes));
            var image = await GetAsync(clients.WeChat, _qr.Image, ct, limit: 2 * 1024 * 1024).ConfigureAwait(false);
            EnsureSuccess(image);
            if (image.Bytes.Length < 8 || !(image.Bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                image.Bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }))) throw WeChatLoginProtocol.Invalid();
            Image = image.Bytes;
        }

        public async Task<QrCheck> CheckAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_completed is { } completed) return new(QrStatus.Authorized, completed);
            // 回调 code 是一次性票据。失败后停止当前尝试，不能通过外层轮询重试重放它。
            if (_callbackStarted) throw CallbackFailed();
            var url = new Url(_qr!.Poll).SetQueryParam("uuid", _qr.Uuid);
            if (_scanned) url.SetQueryParam("last", 404);
            var reply = await GetAsync(clients.WeChatPoll, new Uri(url), cancellationToken, seconds: 35, limit: 16 * 1024).ConfigureAwait(false);
            EnsureSuccess(reply);
            var result = WeChatLoginProtocol.ParsePoll(Encoding.UTF8.GetString(reply.Bytes));
            switch (result.Code)
            {
                case 408: return new(_scanned ? QrStatus.WaitingForConfirmation : QrStatus.WaitingForScan, _context);
                case 404:
                    _scanned = true;
                    return new(QrStatus.WaitingForConfirmation, _context);
                case 403: return new(QrStatus.Denied, _context);
                case 402: return new(QrStatus.Expired, _context);
                case 405:
                    _callbackStarted = true;
                    try
                    {
                        _completed = await CompleteAsync(result.AuthorizationCode!, cancellationToken).ConfigureAwait(false);
                        return new(QrStatus.Authorized, _completed);
                    }
                    catch (AuthException ex) when (ex.Kind is AuthError.Network or AuthError.Timeout or AuthError.RateLimited)
                    {
                        throw CallbackFailed();
                    }
                default: throw WeChatLoginProtocol.Invalid();
            }
        }

        private async Task<AuthContext> CompleteAsync(string code, CancellationToken ct)
        {
            var callback = new Url(WeChatLoginProtocol.Callback).SetQueryParam("code", code).SetQueryParam("state", _authorization!.State);
            var uri = new Uri(callback);
            for (var hop = 0; hop < 5; hop++)
            {
                ct.ThrowIfCancellationRequested();
                // 仅手工跟随网易同源 HTTPS 回调，不全局打开重定向，更不能把票据送到 Location 指定的任意域。
                if (!WeChatLoginProtocol.IsHttps(uri, "music.163.com")) throw WeChatLoginProtocol.Invalid();
                var reply = await GetAsync(clients.Social, uri, ct).ConfigureAwait(false);
                if (reply.Status != 200 && reply.Status is not (301 or 302 or 303 or 307 or 308)) EnsureSuccess(reply);
                if (_context.HasAccount(time.GetUtcNow())) return _context;
                if (reply.Status == 200) break;
                if (string.IsNullOrWhiteSpace(reply.Location) || !Uri.TryCreate(uri, reply.Location, out uri)) throw WeChatLoginProtocol.Invalid();
            }
            throw new AuthException(AuthError.VerificationRequired,
                "微信已确认，但网易尚未建立登录会话；请在官方客户端完成账号绑定或额外验证后重新扫码。");
        }

        private async Task<Reply> GetAsync(IFlurlClient client, Uri uri, CancellationToken ct, int seconds = 15, int limit = 1024 * 1024)
        {
            ct.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            try
            {
                using var response = await client.Request(uri).WithHeader("User-Agent", Agent).WithCookies(_cookies)
                    .WithTimeout(TimeSpan.FromSeconds(seconds)).AllowAnyHttpStatus()
                    .GetAsync(completionOption: HttpCompletionOption.ResponseHeadersRead, cancellationToken: timeout.Token).ConfigureAwait(false);
                // 即使后续正文读取失败，Cookie 仍局限在本次尝试；不会污染已登录账号或其他 Document。
                if (uri.Host == "music.163.com")
                    _context = NeteaseCookies.Merge(_context, uri, response.Headers.GetAll("Set-Cookie"), time.GetUtcNow());
                await using var stream = await response.GetStreamAsync().ConfigureAwait(false);
                var bytes = await NeteaseTransport.ReadBoundedAsync(stream, limit, timeout.Token).ConfigureAwait(false);
                return new(response.StatusCode, response.Headers.FirstOrDefault("Location"), bytes);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
            catch (FlurlHttpException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
            catch (OperationCanceledException) { throw new AuthException(AuthError.Timeout, "微信登录请求超时，请稍后重试。"); }
            catch (FlurlHttpTimeoutException) { throw new AuthException(AuthError.Timeout, "微信登录请求超时，请稍后重试。"); }
            catch (FlurlHttpException) { throw new AuthException(AuthError.Network, "暂时无法连接微信或网易登录服务。"); }
            catch (IOException) { throw new AuthException(AuthError.Network, "读取微信登录响应时连接中断。"); }
        }

        private static void EnsureSuccess(Reply reply)
        {
            if (reply.Status != 200) throw NeteaseTransport.Error(reply.Status, null);
        }
        private static AuthException CallbackFailed() => new(AuthError.Protocol, "微信授权回调未完成，请重新生成二维码再试。");
        public void Dispose()
        {
            _disposed = true;
            _cookies.Clear();
            _context = _context with { Cookies = _context.Cookies.Clear() };
            _completed = null;
            _authorization = null;
            _qr = null;
            Image = [];
        }
        public override string ToString() => "[微信扫码尝试]";
    }
}
