using System.Text.Json;
using Flurl.Http.Configuration;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Http;

// 手动联网探针与默认离线测试分离。只输出状态，不记录二维码 key、Cookie 或账号完整正文。
// 此时只创建一次二维码并检查一次，无需用户授权，也不访问已有账号文件。
var cache = new FlurlClientCache();
using var clients = new NeteaseFlurlClients(cache);
var api = new NeteaseAuthApi(new NeteaseTransport(clients, TimeProvider.System));
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
try
{
    var key = await api.CreateKeyAsync(AuthContext.Create(), cancellation.Token);
    var status = await api.CheckQrAsync(key.Key, key.Context, cancellation.Token);
    var unauthenticated = false;
    try { await api.CheckAccountAsync(status.Context, cancellation.Token); }
    catch (AuthException ex) when (ex.Kind == AuthError.SessionExpired) { unauthenticated = true; }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        mode = "fresh-anonymous-free-qr-probe", keyCreated = true, qrStatus = (int)status.Status,
        unauthenticatedAccountRecognized = unauthenticated, historicalCookiesLoaded = false
    }));
    return status.Status == QrStatus.WaitingForScan && unauthenticated ? 0 : 1;
}
catch (AuthException ex)
{
    Console.WriteLine(JsonSerializer.Serialize(new { error = ex.Kind.ToString(), ex.HttpStatus, ex.BusinessCode }));
    return 1;
}
catch (OperationCanceledException)
{
    Console.WriteLine("{\"error\":\"CancelledOrTimedOut\"}");
    return 1;
}
