using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Protocol;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>
/// 一个容器的 xeapi 握手串行化；协议密钥随账号代次更换，不持久化，不照搬上游全局可变变量。
/// 首次公钥响应验证 nonce 签名后才解密；请求取消或账号撤销时不接受迟到协议会话头。
/// </summary>
internal sealed class XeapiTransport(NeteaseTransport transport, NeteaseFlurlClients clients, TimeProvider time, IMusicSessionAccessor sessions)
{
    private readonly SemaphoreSlim _gate = new(1);
    private long _epoch = -1;
    private XeapiPublicKey? _key;
    private string? _sessionId;
    private string? _sessionKey;
    public async Task<NeteaseResponse> SendAsync(string path, Dictionary<string, string> data, MusicSession session, CancellationToken ct)
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Revoked);
        await _gate.WaitAsync(cancelled.Token).ConfigureAwait(false);
        try
        {
            if (!sessions.IsCurrent(session)) throw new OperationCanceledException(cancelled.Token);
            if (_epoch != session.Epoch) { _epoch = session.Epoch; _key = null; _sessionKey = _sessionId = null; }
            _key ??= await GetKeyAsync(session, cancelled.Token).ConfigureAwait(false);
            var headers = new Dictionary<string, string>
            {
                ["User-Agent"] = "NeteaseMusic/9.1.65.240819161425(9001065);Dalvik/2.1.0 (Linux; U; Android 12)",
                ["X-Client-Enc-State"] = "ENCRYPTED", ["x-aeapi"] = "true", ["x-deviceid"] = session.Context.DeviceId,
                ["x-sdeviceid"] = session.Context.DeviceId, ["x-os"] = "android", ["x-osver"] = "16", ["x-appver"] = "9.1.65",
                ["x-buildver"] = time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
            };
            var cookies = session.Context.Cookies.Where(p => p.Value.Expires is null || p.Value.Expires > time.GetUtcNow()).ToDictionary(p => p.Key, p => p.Value.Value);
            cookies["os"] = "android"; cookies["osver"] = "16"; cookies["appver"] = "9.1.65";
            cookies["deviceId"] = cookies["sDeviceId"] = session.Context.DeviceId;
            cookies["buildver"] = headers["x-buildver"];
            headers["Cookie"] = string.Join("; ", cookies.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
            if (cookies.TryGetValue("MUSIC_U", out var account)) headers["x-music-u"] = account;
            var encoded = new EncodedRequest(path.Replace("/api/", "/xeapi/", StringComparison.Ordinal),
                XeapiCrypto.Encode(data, _key, _sessionId, _sessionKey), headers);
            var reply = await transport.SendEncodedAsync(clients.Xeapi, encoded, NeteaseProtocol.Eapi, session.Context, cancelled.Token).ConfigureAwait(false);
            cancelled.Token.ThrowIfCancellationRequested();
            if (!sessions.IsCurrent(session)) throw new OperationCanceledException(cancelled.Token);
            if (!string.IsNullOrEmpty(reply.SessionId) && reply.SessionKey is { } key && Encoding.UTF8.GetByteCount(key) is 16 or 24 or 32)
            { _sessionId = reply.SessionId; _sessionKey = key; }
            return reply;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or System.Text.Json.JsonException or KeyNotFoundException)
        { _key = null; _sessionKey = _sessionId = null; throw new MusicException(MusicError.Protocol, "播放协议握手数据无效，请重试。"); }
        finally { _gate.Release(); }
    }
    private async Task<XeapiPublicKey> GetKeyAsync(MusicSession session, CancellationToken ct)
    {
        var nonce = RandomNumberGenerator.GetString("0123456789", 16);
        var timestamp = time.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var data = new Dictionary<string, string>
        {
            ["appVersion"] = "9.5.61", ["currentKeyVersion"] = "", ["deviceId"] = session.Context.DeviceId,
            ["nonce"] = nonce, ["os"] = "android", ["requestType"] = "active", ["signature"] = XeapiCrypto.Sign(timestamp, nonce),
            ["t1"] = "", ["t2"] = "", ["timestamp"] = timestamp, ["uid"] = ""
        };
        var response = await transport.SendEncodedAsync(clients.Keys,
            new("/api/gorilla/anti/crawler/security/key/get", data, new()
            {
                ["User-Agent"] = "NeteaseMusic/9.5.61.260802021928(9005061);Dalvik/2.1.0 (Linux; U; Android 12; HBN-AL00 Build/cd737a2.0)",
                ["Cookie"] = "deviceId=" + Uri.EscapeDataString(session.Context.DeviceId)
            }), NeteaseProtocol.Weapi, session.Context, ct).ConfigureAwait(false);
        if (NeteaseTransport.Code(response.Body) != 200) throw NeteaseTransport.Error(200, NeteaseTransport.Code(response.Body));
        var payload = response.Body.GetProperty("data");
        var actual = Convert.FromBase64String(payload.GetProperty("signature").GetString()!);
        var expected = Convert.FromBase64String(XeapiCrypto.Sign(payload.GetProperty("timestamp").ToString(), nonce));
        if (!CryptographicOperations.FixedTimeEquals(actual, expected)) throw new CryptographicException();
        return XeapiCrypto.DecodePublicKey(payload.GetProperty("encryptedData").GetString()!);
    }
}
