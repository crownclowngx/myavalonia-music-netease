using System.Collections.Immutable;
using System.Net;
using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>
/// 每个响应使用独立 CookieContainer 解释标准属性，避免按逗号拆分 Expires。
/// 仅网易账号协议所需的 Cookie 被投影到上下文；应用自己生成设备元数据，远端不能覆盖它。
/// </summary>
internal static class NeteaseCookies
{
    private static readonly string[] Names = ["MUSIC_U", "MUSIC_A", "__csrf", "NMTID"];

    internal static AuthContext Merge(AuthContext context, Uri responseUri, IEnumerable<string> headers, DateTimeOffset now)
    {
        var container = new CookieContainer(100, 30, 16384);
        foreach (var name in Names)
        {
            var value = context.Cookie(name, now);
            if (value.Length > 0)
                container.Add(new Cookie(name, Uri.EscapeDataString(value), "/", ".music.163.com"));
        }
        foreach (var header in headers)
        {
            try { container.SetCookies(responseUri, header); }
            catch (CookieException) { throw new AuthException(AuthError.Protocol, "网易返回了无法识别的会话信息。"); }
        }
        var result = context.Cookies.ToBuilder();
        foreach (var name in Names)
        {
            var cookie = container.GetCookies(responseUri)[name];
            if (cookie is null || cookie.Expired || string.IsNullOrEmpty(cookie.Value)) result.Remove(name);
            else
            {
                var expires = cookie.Expires == DateTime.MinValue
                    ? context.Cookies.GetValueOrDefault(name)?.Expires
                    : new DateTimeOffset(cookie.Expires.ToUniversalTime());
                result[name] = new(Uri.UnescapeDataString(cookie.Value), expires);
            }
        }
        return context with { Cookies = result.ToImmutable() };
    }
}
