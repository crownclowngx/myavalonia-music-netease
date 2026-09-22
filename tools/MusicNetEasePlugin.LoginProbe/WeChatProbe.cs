using System.Text.Json;
using Flurl.Http.Configuration;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Http;

/// <summary>显式微信联网验证：图片仅写入调用者指定路径，账号 Cookie 只在本进程内存中，不保存真实会话。</summary>
internal static class WeChatProbe
{
    internal static async Task<int> RunAsync(string output)
    {
        if (!Path.IsPathFullyQualified(output)) throw new ArgumentException("二维码输出必须使用绝对路径。");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var clients = new NeteaseFlurlClients(new FlurlClientCache());
        var api = new NeteaseAuthApi(new NeteaseTransport(clients, TimeProvider.System));
        await using var login = new LoginCoordinator(api, new VolatileStore(), TimeProvider.System, LoginOptions.Default,
            [new WeChatQrLoginProvider(clients, TimeProvider.System), new NeteaseAppQrLoginProvider(api)]);
        byte[]? previous = null;
        login.Changed += (_, state) =>
        {
            if (state.QrImage is { } image && !ReferenceEquals(previous, image))
            {
                File.WriteAllBytes(output, image);
                previous = image;
                Console.WriteLine("QR_READY");
            }
            // 协调器复用持久化契约，但探针刻意使用空保存实现；输出不能误称已经写入磁盘。
            var message = state.Stage == LoginStage.SignedIn ? "账号已核验，探针未保存登录会话。" : state.Message;
            Console.WriteLine(JsonSerializer.Serialize(new { stage = state.Stage.ToString(), message }));
        };
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await login.StartAsync(Guid.NewGuid(), cancellation.Token);
            var verified = login.Snapshot.Account is not null;
            Console.WriteLine(JsonSerializer.Serialize(new { accountVerified = verified, persistentSessionWritten = false }));
            return verified ? 0 : 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private sealed class VolatileStore : ILoginSessionStore
    {
        public Task<AuthContext> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(AuthContext.Create());
        public Task SaveAsync(AuthContext context, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
