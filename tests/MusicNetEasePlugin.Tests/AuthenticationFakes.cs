using System.Collections.Immutable;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Http;

namespace MusicNetEasePlugin.Tests;

/// <summary>
/// UI 测试只替换微信协议边界，仍运行真实协调器与网易 App 适配器。
/// 微信的实际 HTTP 往返另由 WeChatLoginTests 验证，避免测试依赖外网与手机操作。
/// </summary>
internal static class TestLogin
{
    public static LoginCoordinator Create(FakeAuthApi api, MemorySessionStore store, TimeProvider time,
        LoginOptions options) => new(api, store, time, options,
            [new NeteaseAppQrLoginProvider(api), new FakeWeChatProvider(api)]);

    private sealed class FakeWeChatProvider(FakeAuthApi api) : IQrLoginProvider
    {
        public LoginMethod Method => LoginMethod.WeChat;
        public Task<IQrLoginAttempt> CreateAsync(AuthContext context, CancellationToken cancellationToken) =>
            new NeteaseAppQrLoginProvider(api).CreateAsync(context, cancellationToken);
    }
}

internal sealed class MemorySessionStore : ILoginSessionStore
{
    public AuthContext Empty { get; } = AuthContext.Create();
    public AuthContext? Saved { get; set; }
    public bool FailSave { get; set; }
    public bool FailClear { get; set; }
    public bool FailLoad { get; set; }
    public int Saves { get; private set; }
    public int Clears { get; private set; }
    public Func<Task>? AfterCommit { get; set; }

    public Task<AuthContext> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (FailLoad) throw new AuthException(AuthError.Storage, "加载失败");
        return Task.FromResult(Saved ?? Empty);
    }

    public async Task SaveAsync(AuthContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (FailSave) throw new AuthException(AuthError.Storage, "保存失败");
        Saves++;
        Saved = context;
        // 模拟原子提交后返回前被取消：已经提交的文件事实不能假装被 CancellationToken 撤销。
        if (AfterCommit is { } after) await after();
    }

    public Task ClearAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (FailClear) throw new AuthException(AuthError.Storage, "清除失败");
        Clears++;
        Saved = null;
        return Task.CompletedTask;
    }
}

internal sealed class FakeAuthApi : INeteaseAuthApi
{
    public Queue<QrStatus> States { get; } = new();
    public int Keys { get; private set; }
    public int Checks { get; private set; }
    public int Accounts { get; private set; }
    public int Logouts { get; private set; }
    public Func<string, AuthContext, CancellationToken, Task<QrCheck>>? Check { get; set; }
    public Func<AuthContext, CancellationToken, Task<AccountCheck>>? Account { get; set; }
    public AuthException? LogoutFailure { get; set; }

    public static AuthContext Authorized(AuthContext context) => context with
    {
        Cookies = ImmutableDictionary<string, SessionCookie>.Empty.Add("MUSIC_U", new("test-cookie"))
    };

    public Task<QrKey> CreateKeyAsync(AuthContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new QrKey("key-" + ++Keys, context));
    }

    public Task<QrCheck> CheckQrAsync(string key, AuthContext context, CancellationToken ct)
    {
        Checks++;
        if (Check is { } action) return action(key, context, ct);
        ct.ThrowIfCancellationRequested();
        var state = States.Count == 0 ? QrStatus.Authorized : States.Dequeue();
        return Task.FromResult(new QrCheck(state, state == QrStatus.Authorized ? Authorized(context) : context));
    }

    public Task<AccountCheck> CheckAccountAsync(AuthContext context, CancellationToken ct)
    {
        Accounts++;
        ct.ThrowIfCancellationRequested();
        if (Account is { } action) return action(context, ct);
        return Task.FromResult(new AccountCheck(new(123, "测试账号", null), context));
    }

    public Task LogoutAsync(AuthContext context, CancellationToken ct)
    {
        Logouts++;
        ct.ThrowIfCancellationRequested();
        if (LogoutFailure is { } failure) throw failure;
        return Task.CompletedTask;
    }
}
