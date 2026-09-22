using System.Net;
using System.Text.RegularExpressions;
using Flurl;
using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Infrastructure.Protocol;

/// <summary>
/// 按微信官方授权页实际协议读取有限字段，不执行服务端返回的 JavaScript。
/// 微信 UUID 与网易 App codekey 是两类凭据，二者不可互换，也不能由手机网页登录结果推断桌面授权成功。
/// </summary>
internal static class WeChatLoginProtocol
{
    internal const string Entry = "https://music.163.com/api/sns/authorize?snsType=10&clientType=web2&callbackType=Login&forcelogin=true";
    internal const string Callback = "https://music.163.com/back/weichat";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    internal sealed record Authorization(Uri Page, string State)
    {
        public override string ToString() => "[微信授权上下文]";
    }
    internal sealed record QrPage(string Uuid, Uri Image, Uri Poll)
    {
        public override string ToString() => "[微信二维码上下文]";
    }
    internal sealed record PollResult(int Code, string? AuthorizationCode)
    {
        public override string ToString() => $"微信状态 {Code}";
    }

    internal static Authorization ParseAuthorization(string? location)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || !IsHttps(uri, "open.weixin.qq.com") ||
            uri.AbsolutePath != "/connect/qrconnect") throw Invalid();
        var query = new Url(uri.AbsoluteUri).QueryParams;
        string Required(string name)
        {
            var values = query.GetAll(name).ToArray();
            if (values.Length != 1 || values[0] is not string text || string.IsNullOrWhiteSpace(text) || text.Length > 4096)
                throw Invalid();
            return text;
        }
        if (Required("redirect_uri") != Callback || Required("scope") != "snsapi_login" || Required("response_type") != "code") throw Invalid();
        _ = Required("appid");
        return new(uri, Required("state"));
    }

    internal static QrPage ParsePage(string html)
    {
        foreach (Match image in Regex.Matches(html, @"<img\b[^>]*>", RegexOptions.IgnoreCase, MatchTimeout))
        {
            var src = Regex.Match(image.Value, "\\bsrc\\s*=\\s*[\"'](?<src>[^\"']+)[\"']", RegexOptions.IgnoreCase, MatchTimeout);
            if (!src.Success) continue;
            var address = WebUtility.HtmlDecode(src.Groups["src"].Value);
            if (!Uri.TryCreate(new Uri("https://open.weixin.qq.com"), address, out var uri) || !IsHttps(uri, "open.weixin.qq.com")) continue;
            var match = Regex.Match(uri.AbsolutePath, @"\A/connect/qrcode/(?<uuid>[A-Za-z0-9_-]{6,128})\z", RegexOptions.None, MatchTimeout);
            if (!match.Success || uri.Query.Length != 0 || uri.Fragment.Length != 0) continue;
            // 当前微信页面根据这个标记选择两处长轮询域；只接受这两个固定官方域名。
            var modern = Regex.IsMatch(html, @"window\.usenewdomain\s*=\s*(?:true|1)\b", RegexOptions.None, MatchTimeout);
            var poll = modern ? "https://lp.open.weixin.qq.com/connect/l/qrconnect" : "https://long.open.weixin.qq.com/connect/l/qrconnect";
            return new(match.Groups["uuid"].Value, uri, new Uri(poll));
        }
        throw Invalid();
    }

    internal static PollResult ParsePoll(string text)
    {
        // 只接受简单赋值语法；拒绝任意函数、重定向脚本或尾随代码，绝不使用 JS eval。
        var match = Regex.Match(text,
            "\\A\\s*window\\.wx_errcode\\s*=\\s*(?<status>[0-9]{3})\\s*;\\s*(?:window\\.wx_code\\s*=\\s*[\"'](?<code>[A-Za-z0-9_-]{0,512})[\"']\\s*;?)?\\s*\\z",
            RegexOptions.None, MatchTimeout);
        if (!match.Success) throw Invalid();
        var status = int.Parse(match.Groups["status"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var code = match.Groups["code"].Value;
        if (status == 405 && string.IsNullOrEmpty(code)) throw Invalid();
        return new(status, string.IsNullOrEmpty(code) ? null : code);
    }

    internal static bool IsHttps(Uri uri, string host) => uri.Scheme == "https" && uri.Host == host &&
        uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);

    internal static AuthException Invalid() => new(AuthError.Protocol, "微信登录页面或授权协议已变化，请重新尝试或使用网易云 App 扫码。");
}
