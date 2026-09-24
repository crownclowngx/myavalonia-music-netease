using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Flurl.Http;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Protocol;

namespace MusicNetEasePlugin.Infrastructure.Http;

internal sealed record NeteaseResponse(JsonElement Body, AuthContext Context, string? SessionId = null, string? SessionKey = null)
{
    public override string ToString() => "[网易响应]";
}

/// <summary>
/// 唯一业务 HTTP 执行入口：固定端点、请求级凭据、完整取消、有限响应及安全错误。
/// 不自动重试 POST；轮询重试预算由用例决定，传输层不会重复创建登录凭据。
/// </summary>
internal sealed class NeteaseTransport(NeteaseFlurlClients clients, TimeProvider time)
{
    internal const int MaximumResponseBytes = 1024 * 1024;

    internal async Task<NeteaseResponse> SendAsync(string path, Dictionary<string, object?> data,
        NeteaseProtocol protocol, AuthContext context, CancellationToken cancellationToken, string? antiCheatToken = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var encoded = NeteaseRequestEncoder.Encode(path, data, protocol, context, time.GetUtcNow(), antiCheatToken);
        var client = protocol == NeteaseProtocol.Weapi ? clients.Web : clients.Eapi;
        return await SendEncodedAsync(client, encoded, protocol, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>复用相同预算、错误和 Cookie 规则；xeapi 编码器仅提供协议数据，不重新实现 HTTP 生命周期。</summary>
    internal async Task<NeteaseResponse> SendEncodedAsync(IFlurlClient client, EncodedRequest encoded,
        NeteaseProtocol protocol, AuthContext context, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var response = await client.Request(encoded.Route).WithHeaders(encoded.Headers)
                .AllowAnyHttpStatus().PostUrlEncodedAsync(encoded.Form,
                    completionOption: HttpCompletionOption.ResponseHeadersRead, cancellationToken: timeout.Token).ConfigureAwait(false);
            await using var stream = await response.GetStreamAsync().ConfigureAwait(false);
            var bytes = await ReadBoundedAsync(stream, MaximumResponseBytes, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != 200)
            {
                int? business = null;
                try { using var body = JsonDocument.Parse(bytes); business = Code(body.RootElement); }
                catch (JsonException) { }
                throw Error(response.StatusCode, business, RetryAfter(response, time.GetUtcNow()));
            }
            var json = Decode(bytes, protocol);
            var merged = NeteaseCookies.Merge(context, new Uri(client.BaseUrl + encoded.Route),
                response.Headers.GetAll("Set-Cookie"), time.GetUtcNow());
            return new(json, merged, response.Headers.FirstOrDefault("x-encr-ssid"), response.Headers.FirstOrDefault("x-encr-sskey"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (FlurlHttpException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new AuthException(AuthError.Timeout, "网易请求超时，请稍后重试。");
        }
        catch (FlurlHttpTimeoutException)
        {
            throw new AuthException(AuthError.Timeout, "网易请求超时，请稍后重试。");
        }
        catch (FlurlHttpException)
        {
            // 不保留 InnerException：Flurl 的异常文本可能包含请求 URL、参数或会话内容。
            throw new AuthException(AuthError.Network, "暂时无法连接网易云音乐。");
        }
        catch (IOException)
        {
            throw new AuthException(AuthError.Network, "读取网易响应时连接中断。");
        }
    }

    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > limit)
                throw new AuthException(AuthError.Protocol, "远端响应超过允许大小。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    internal static JsonElement Decode(byte[] bytes, NeteaseProtocol protocol)
    {
        try { using var json = JsonDocument.Parse(bytes); return json.RootElement.Clone(); }
        catch (JsonException) when (protocol == NeteaseProtocol.Eapi)
        {
            try
            {
                var plain = NeteaseCrypto.DecodeEapi(bytes);
                if (plain.Length >= 2 && plain[0] == 0x1f && plain[1] == 0x8b)
                {
                    using var input = new MemoryStream(plain);
                    using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    var buffer = new byte[8192];
                    int count;
                    while ((count = gzip.Read(buffer)) > 0)
                    {
                        if (output.Length + count > MaximumResponseBytes)
                            throw new AuthException(AuthError.Protocol, "解压后的响应超过允许大小。");
                        output.Write(buffer, 0, count);
                    }
                    plain = output.ToArray();
                }
                using var json = JsonDocument.Parse(plain);
                return json.RootElement.Clone();
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or ArgumentException)
            {
                throw new AuthException(AuthError.Protocol, "网易响应格式或协议已变化。");
            }
        }
        catch (JsonException) { throw new AuthException(AuthError.Protocol, "网易响应不是有效的账号数据。"); }
    }

    internal static int? Code(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("code", out var code)) return null;
        return code.ValueKind switch
        {
            JsonValueKind.Number when code.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(code.GetString(), out var number) => number,
            _ => null
        };
    }

    internal static AuthException Error(int http, int? code, TimeSpan? retry = null)
    {
        var kind = (http, code) switch
        {
            (429, _) or (_, 429) => AuthError.RateLimited,
            (401, _) or (_, 301) => AuthError.SessionExpired,
            (403, _) or (_, 460 or -460 or 405) => AuthError.VerificationRequired,
            (>= 500, _) => AuthError.Network,
            _ => AuthError.Protocol
        };
        var text = kind switch
        {
            AuthError.RateLimited => "请求过于频繁，请稍后重新尝试。",
            AuthError.SessionExpired => "登录已失效，请重新扫码。",
            AuthError.VerificationRequired => "网易要求额外验证，请在官方客户端检查账号后重试。",
            AuthError.Network => "网易服务暂时不可用。",
            _ => "网易返回了未支持的结果，请稍后重试。"
        };
        return new(kind, text, http, code, retry);
    }

    private static TimeSpan? RetryAfter(IFlurlResponse response, DateTimeOffset now)
    {
        var value = response.Headers.FirstOrDefault("Retry-After");
        if (int.TryParse(value, out var seconds)) return TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 180));
        if (DateTimeOffset.TryParse(value, out var date)) return TimeSpan.FromSeconds(Math.Clamp((date - now).TotalSeconds, 0, 180));
        return null;
    }
}
