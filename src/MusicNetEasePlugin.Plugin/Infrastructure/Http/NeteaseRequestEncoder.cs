using System.Globalization;
using System.Security.Cryptography;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Protocol;

namespace MusicNetEasePlugin.Infrastructure.Http;

internal enum NeteaseProtocol { Eapi, Weapi }
internal sealed record EncodedRequest(string Route, Dictionary<string, string> Form, Dictionary<string, string> Headers);

/// <summary>集中构造线上协议数据；端点适配器仅描述业务参数，避免每个端点重复维护设备头。</summary>
internal static class NeteaseRequestEncoder
{
    internal static EncodedRequest Encode(string path, Dictionary<string, object?> data,
        NeteaseProtocol protocol, AuthContext context, DateTimeOffset now)
    {
        if (!path.StartsWith("/api/", StringComparison.Ordinal) || path.Contains('?'))
            throw new ArgumentException("只接受固定的网易逻辑端点。", nameof(path));
        var cookies = context.Cookies.Where(p => p.Value.Expires is null || p.Value.Expires > now)
            .ToDictionary(p => p.Key, p => p.Value.Value, StringComparer.Ordinal);
        var payload = new Dictionary<string, object?>(data) { ["e_r"] = false };
        var headers = new Dictionary<string, string>();
        var csrf = context.Cookie("__csrf", now);
        if (protocol == NeteaseProtocol.Eapi)
        {
            var meta = new Dictionary<string, string>
            {
                ["osver"] = "Microsoft-Windows-10-Professional-build-19045-64bit",
                ["deviceId"] = context.DeviceId,
                ["os"] = "pc",
                ["appver"] = "3.1.17.204416",
                ["versioncode"] = "140",
                ["mobilename"] = "",
                ["buildver"] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["resolution"] = "1920x1080",
                ["__csrf"] = csrf,
                ["channel"] = "netease",
                ["requestId"] = $"{now.ToUnixTimeMilliseconds()}_{RandomNumberGenerator.GetInt32(1000):D4}"
            };
            foreach (var name in new[] { "MUSIC_U", "MUSIC_A", "NMTID" })
                if (cookies.TryGetValue(name, out var value)) meta[name] = value;
            payload["header"] = meta;
            headers["Cookie"] = CookieHeader(meta);
            // 固定上游默认组合：pc 元数据搭配其 api/iphone UA，不自行猜测更新版本。
            headers["User-Agent"] = "NeteaseMusic 9.0.90/5038 (iPhone; iOS 16.2; zh_CN)";
            return new(path.Replace("/api/", "/eapi/", StringComparison.Ordinal),
                NeteaseCrypto.Eapi(path, NeteaseCrypto.Json(payload)), headers);
        }

        cookies["__remember_me"] = "true";
        cookies["ntes_kaola_ad"] = "1";
        cookies["os"] = "pc";
        cookies["appver"] = "3.1.17.204416";
        cookies["deviceId"] = context.DeviceId;
        cookies["channel"] = "netease";
        cookies["osver"] = "Microsoft-Windows-10-Professional-build-19045-64bit";
        cookies["WEVNSM"] = "1.0.0";
        cookies.TryAdd("_ntes_nuid", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        cookies.TryAdd("_ntes_nnid", $"{cookies["_ntes_nuid"]},{now.ToUnixTimeMilliseconds()}");
        cookies.TryAdd("WNMCID", $"{RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz", 6)}.{now.ToUnixTimeMilliseconds()}.01.0");
        cookies.TryAdd("NMTID", "00O" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(19)));
        payload["csrf_token"] = csrf;
        headers["Cookie"] = CookieHeader(cookies);
        headers["Referer"] = "https://music.163.com";
        headers["User-Agent"] = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0";
        return new(path.Replace("/api/", "/weapi/", StringComparison.Ordinal),
            NeteaseCrypto.Weapi(NeteaseCrypto.Json(payload)), headers);
    }

    private static string CookieHeader(Dictionary<string, string> values) =>
        string.Join("; ", values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
}
