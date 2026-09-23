using System.Text.Json;
using Flurl.Http.Configuration;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Http;

if (args.Length > 0)
{
    if (args.Length is 7 or 9 && args[0] == "--music" && args[1] == "--session-directory" && args[3] == "--keyword" && args[5] == "--output" && (args.Length == 7 || args[7] == "--runtime-directory"))
        return await MusicProbe.RunAsync(args[2], args[4], args[6], args.Length == 9 ? args[8] : null);
    if (args.Length == 3 && args[0] == "--wechat" && args[1] == "--output")
        return await WeChatProbe.RunAsync(args[2]);
    Console.Error.WriteLine("用法：无参数为未登录探针；--wechat --output <二维码路径>；--music --session-directory <既有会话目录> --keyword <歌曲关键词> --output <报告路径> [--runtime-directory <LibVLC目录>] 为显式无声解码验证。");
    return 2;
}

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
