using System.Text.Json;
using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>将网易的原生正文转换为登录结果，不复制 Node HTTP 外壳额外包装的 body/data 层。</summary>
internal sealed class NeteaseAuthApi(NeteaseTransport transport) : INeteaseAuthApi
{
    public async Task<QrKey> CreateKeyAsync(AuthContext context, CancellationToken cancellationToken)
    {
        var reply = await transport.SendAsync("/api/login/qrcode/unikey",
            new() { ["type"] = 3 }, NeteaseProtocol.Eapi, context, cancellationToken).ConfigureAwait(false);
        EnsureCode(reply.Body, 200);
        if (!reply.Body.TryGetProperty("unikey", out var key) || key.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(key.GetString()))
            throw new AuthException(AuthError.Protocol, "网易没有返回有效的二维码凭据。");
        return new(key.GetString()!, reply.Context);
    }

    public async Task<QrCheck> CheckQrAsync(string key, AuthContext context, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var reply = await transport.SendAsync("/api/login/qrcode/client/login",
            new() { ["key"] = key, ["type"] = 3 }, NeteaseProtocol.Eapi, context, cancellationToken).ConfigureAwait(false);
        var code = NeteaseTransport.Code(reply.Body);
        if (code is not (800 or 801 or 802 or 803)) throw NeteaseTransport.Error(200, code);
        return new((QrStatus)code, reply.Context);
    }

    public async Task<AccountCheck> CheckAccountAsync(AuthContext context, CancellationToken cancellationToken)
    {
        var reply = await transport.SendAsync("/api/w/nuser/account/get", new(),
            NeteaseProtocol.Weapi, context, cancellationToken).ConfigureAwait(false);
        EnsureCode(reply.Body, 200);
        if (!reply.Body.TryGetProperty("account", out var account) || account.ValueKind == JsonValueKind.Null)
            throw new AuthException(AuthError.SessionExpired, "当前会话未登录，请重新扫码。", 200);
        if (account.ValueKind != JsonValueKind.Object ||
            !reply.Body.TryGetProperty("profile", out var profile) || profile.ValueKind != JsonValueKind.Object ||
            !account.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || number <= 0 ||
            !profile.TryGetProperty("userId", out var userId) || userId.ValueKind != JsonValueKind.Number || !userId.TryGetInt64(out var profileId) || profileId != number ||
            !profile.TryGetProperty("nickname", out var name) || name.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(name.GetString()))
            throw new AuthException(AuthError.Protocol, "网易账号资料不完整或身份不一致。");
        var avatar = profile.TryGetProperty("avatarUrl", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        return new(new(number, name.GetString()!, avatar), reply.Context);
    }

    public async Task LogoutAsync(AuthContext context, CancellationToken cancellationToken)
    {
        var reply = await transport.SendAsync("/api/logout", new(), NeteaseProtocol.Eapi,
            context, cancellationToken).ConfigureAwait(false);
        EnsureCode(reply.Body, 200);
    }

    private static void EnsureCode(JsonElement body, int expected)
    {
        var code = NeteaseTransport.Code(body);
        if (code != expected) throw NeteaseTransport.Error(200, code);
    }
}
