using System.Security.Cryptography;
using System.Text;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Persistence;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MusicNetEasePlugin.Tests", Guid.NewGuid().ToString("N"));
    private readonly XorProtector _protector = new();
    private ProtectedLoginSessionStore Store => new(_directory, _protector);

    private sealed class XorProtector : ISessionProtector
    {
        public byte[] Protect(byte[] plain) => plain.Select(b => (byte)(b ^ 0x5a)).ToArray();
        public byte[] Unprotect(byte[] cipher) => Protect(cipher);
    }

    [Fact]
    [Trait("Scenario", "S01,S02,S08,S10")]
    public async Task 重建服务后恢复会话且退出保留设备身份()
    {
        var store = Store;
        var initial = await store.LoadAsync(default);
        var authorized = FakeAuthApi.Authorized(initial);
        await store.SaveAsync(authorized, default);
        var raw = await File.ReadAllBytesAsync(store.SessionPath);
        Assert.DoesNotContain("test-cookie", Encoding.UTF8.GetString(raw));
        var restored = await Store.LoadAsync(default);
        Assert.Equal(initial.DeviceId, restored.DeviceId);
        Assert.Equal("test-cookie", restored.Cookie("MUSIC_U", DateTimeOffset.UtcNow));
        await Store.ClearAsync(default);
        Assert.False(File.Exists(store.SessionPath));
        var after = await Store.LoadAsync(default);
        Assert.Equal(initial.DeviceId, after.DeviceId);
        Assert.False(after.HasAccount(DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("{\"Version\":2,\"DeviceId\":\"x\",\"Cookies\":{}}")]
    [InlineData("{\"Version\":1,\"DeviceId\":null,\"Cookies\":null}")]
    [InlineData("not-json")]
    [Trait("Scenario", "S06")]
    public async Task 损坏或未来版本文件不能恢复且保留原文件(string payload)
    {
        var store = Store;
        await store.LoadAsync(default);
        var bytes = _protector.Protect(Encoding.UTF8.GetBytes(payload));
        await File.WriteAllBytesAsync(store.SessionPath, bytes);
        var ex = await Assert.ThrowsAsync<AuthException>(() => Store.LoadAsync(default));
        Assert.Equal(AuthError.Storage, ex.Kind);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(store.SessionPath));
        Assert.Null(ex.InnerException);
    }

    [Fact]
    [Trait("Scenario", "S06")]
    public async Task 超大会话拒绝读取()
    {
        var store = Store;
        await store.LoadAsync(default);
        await File.WriteAllBytesAsync(store.SessionPath, new byte[129 * 1024]);
        Assert.Equal(AuthError.Storage, (await Assert.ThrowsAsync<AuthException>(() => Store.LoadAsync(default))).Kind);
    }

    [Fact]
    [Trait("Scenario", "S07,S09")]
    public async Task 取消保存不破坏旧文件并清理本次临时文件()
    {
        var store = Store;
        var context = FakeAuthApi.Authorized(await store.LoadAsync(default));
        await store.SaveAsync(context, default);
        var before = await File.ReadAllBytesAsync(store.SessionPath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(context, cts.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(store.SessionPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    [Trait("Scenario", "S07,S08")]
    public async Task 保存或清除目标被目录占用时明确失败()
    {
        var store = Store;
        var context = FakeAuthApi.Authorized(await store.LoadAsync(default));
        Directory.CreateDirectory(store.SessionPath);
        Assert.Equal(AuthError.Storage, (await Assert.ThrowsAsync<AuthException>(() => store.SaveAsync(context, default))).Kind);
        Assert.Equal(AuthError.Storage, (await Assert.ThrowsAsync<AuthException>(() => store.ClearAsync(default))).Kind);
        Assert.True(Directory.Exists(store.SessionPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task 显式新登录能够修复损坏的设备元数据()
    {
        var store = Store;
        await store.LoadAsync(default);
        await File.WriteAllTextAsync(store.DevicePath, "broken");
        await Assert.ThrowsAsync<AuthException>(() => store.LoadAsync(default));
        var context = FakeAuthApi.Authorized(AuthContext.Create());
        await store.SaveAsync(context, default);
        Assert.Equal(context.DeviceId, (await Store.LoadAsync(default)).DeviceId);
    }

    [Fact]
    [Trait("Scenario", "S11")]
    public void 本机当前用户保护可以往返且损坏密文拒绝()
    {
        var protector = new CurrentUserSessionProtector();
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<AuthException>(() => protector.Protect([1, 2, 3]));
            return;
        }
        var bytes = Encoding.UTF8.GetBytes("test-private-session");
        var cipher = protector.Protect(bytes);
        Assert.NotEqual(bytes, cipher);
        Assert.Equal(bytes, protector.Unprotect(cipher));
        cipher[^1] ^= 0xff;
        Assert.Throws<CryptographicException>(() => protector.Unprotect(cipher));
    }

    [Fact]
    public async Task 不支持的凭据字段或非法设备不写入文件()
    {
        var store = Store;
        var context = FakeAuthApi.Authorized(AuthContext.Create());
        await Assert.ThrowsAsync<AuthException>(() => store.SaveAsync(context with { DeviceId = "bad" }, default));
        await Assert.ThrowsAsync<AuthException>(() => store.SaveAsync(context with { Cookies = context.Cookies.Add("password", new("secret")) }, default));
        Assert.False(File.Exists(store.SessionPath));
    }

    public void Dispose()
    {
        // 路径由当前夹具创建且含独占 GUID；只允许删除这个测试根下的精确子目录。
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MusicNetEasePlugin.Tests")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(_directory);
        if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("测试清理路径越界。");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
}
