using System.Collections.Immutable;
using System.Security.Cryptography;

namespace MusicNetEasePlugin.Application.Authentication;

/// <summary>账号资料只用于显示；凭据由独立的会话上下文拥有，不放进 ViewModel。</summary>
public sealed record NeteaseAccount(long Id, string Nickname, string? AvatarUrl);

/// <summary>保存 Cookie 值和服务端有效期。禁止默认 ToString 输出敏感值。</summary>
public sealed record SessionCookie(string Value, DateTimeOffset? Expires = null)
{
    public override string ToString() => "[受保护的 Cookie]";
}

/// <summary>
/// 不可变请求快照。每次请求返回新的上下文，避免迟到响应经共享 CookieJar 污染已退出的账号。
/// 设备身份在一个会话中保持不变，持久化后跨启动复用；不依赖 Flurl 或 UI 类型。
/// </summary>
public sealed record AuthContext(string DeviceId, ImmutableDictionary<string, SessionCookie> Cookies)
{
    public static AuthContext Create() => new(Convert.ToHexString(RandomNumberGenerator.GetBytes(26)),
        ImmutableDictionary<string, SessionCookie>.Empty);

    public string Cookie(string name, DateTimeOffset now) =>
        Cookies.TryGetValue(name, out var cookie) && (cookie.Expires is null || cookie.Expires > now)
            ? cookie.Value : "";

    public bool HasAccount(DateTimeOffset now) => !string.IsNullOrEmpty(Cookie("MUSIC_U", now));
    public override string ToString() => "[网易会话上下文]";
}

public sealed record QrKey(string Key, AuthContext Context)
{
    public override string ToString() => "[二维码登录凭据]";
}

// 前四项沿用网易码；Denied 是统一后的本地状态，不会作为业务码发送给任何远端。
public enum QrStatus { Expired = 800, WaitingForScan = 801, WaitingForConfirmation = 802, Authorized = 803, Denied = 804 }
public sealed record QrCheck(QrStatus Status, AuthContext Context);
public sealed record AccountCheck(NeteaseAccount Account, AuthContext Context);

/// <summary>登录用例所需的最小网络端口；所有方法观察取消，不承诺二维码 Cookie 可刷新。</summary>
public interface INeteaseAuthApi
{
    Task<QrKey> CreateKeyAsync(AuthContext context, CancellationToken cancellationToken);
    Task<QrCheck> CheckQrAsync(string key, AuthContext context, CancellationToken cancellationToken);
    Task<AccountCheck> CheckAccountAsync(AuthContext context, CancellationToken cancellationToken);
    Task LogoutAsync(AuthContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 本地存储只负责受保护会话和稳定设备身份。清除只移除账号凭据，保留设备身份。
/// 网络与状态转换属于登录服务；存储失败必须抛出安全的 AuthException，不能降级明文。
/// </summary>
public interface ILoginSessionStore
{
    Task<AuthContext> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AuthContext context, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

public enum AuthError { Network, Timeout, RateLimited, SessionExpired, VerificationRequired, Protocol, Storage }

/// <summary>只保留稳定分类与数值码，不携带原始响应、完整 URL 或底层异常链。</summary>
public sealed class AuthException(
    AuthError kind, string message, int? httpStatus = null, int? businessCode = null,
    TimeSpan? retryAfter = null) : Exception(message)
{
    public AuthError Kind { get; } = kind;
    public int? HttpStatus { get; } = httpStatus;
    public int? BusinessCode { get; } = businessCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
