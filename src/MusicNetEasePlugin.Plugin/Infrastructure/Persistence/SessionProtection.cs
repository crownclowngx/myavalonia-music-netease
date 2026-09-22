using System.Security.Cryptography;
using System.Text;
using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Infrastructure.Persistence;

/// <summary>仅在操作系统凭据保护边界建立抽象，便于离线存储测试注入确定性保护器。</summary>
internal interface ISessionProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

internal sealed class CurrentUserSessionProtector : ISessionProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("myavalonia.plugin.music.netease/session/v1");

    public byte[] Protect(byte[] plaintext)
    {
        if (!OperatingSystem.IsWindows()) throw Unavailable();
        return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        if (!OperatingSystem.IsWindows()) throw Unavailable();
        return ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
    }

    private static AuthException Unavailable() =>
        new(AuthError.Storage, "当前平台尚未提供安全的登录凭据保存方式。");
}
