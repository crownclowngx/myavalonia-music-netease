using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Infrastructure.Persistence;

internal sealed record StoredSession(int Version, string DeviceId, Dictionary<string, SessionCookie> Cookies);

/// <summary>
/// 小型会话文件使用当前用户保护和同目录原子替换。设备身份独立保存，退出仅删除会话文件。
/// 一个插件容器持有一个实例；Standalone 使用独立数据根，避免预览程序和 Host 相互覆盖账号。
/// </summary>
internal sealed class ProtectedLoginSessionStore(string directory, ISessionProtector protector) : ILoginSessionStore
{
    private const int Limit = 128 * 1024;
    internal string SessionPath => Path.Combine(directory, "session.bin");
    internal string DevicePath => Path.Combine(directory, "device.id");

    public async Task<AuthContext> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            var device = File.Exists(DevicePath) ? await File.ReadAllTextAsync(DevicePath, cancellationToken).ConfigureAwait(false) : null;
            if (device is null)
            {
                device = AuthContext.Create().DeviceId;
                await WriteAtomicAsync(DevicePath, System.Text.Encoding.ASCII.GetBytes(device), cancellationToken).ConfigureAwait(false);
            }
            if (!File.Exists(SessionPath))
            {
                if (!ValidDevice(device)) throw new InvalidDataException();
                return new(device, ImmutableDictionary<string, SessionCookie>.Empty);
            }
            if (new FileInfo(SessionPath).Length > Limit) throw new InvalidDataException();
            var bytes = await File.ReadAllBytesAsync(SessionPath, cancellationToken).ConfigureAwait(false);
            if (bytes.Length > Limit) throw new InvalidDataException();
            var plain = protector.Unprotect(bytes);
            try
            {
                if (plain.Length > Limit) throw new InvalidDataException();
                var saved = JsonSerializer.Deserialize<StoredSession>(plain) ?? throw new InvalidDataException();
                if (saved.Version != 1 || !ValidDevice(saved.DeviceId) || saved.Cookies is null ||
                    saved.Cookies.Count > 4 || saved.Cookies.Any(p => !ValidCookie(p.Key, p.Value)))
                    throw new InvalidDataException();
                // 已提交会话中的设备身份是权威值：创建身份文件后的中断不应破坏仍有效的旧会话。
                return new(saved.DeviceId, saved.Cookies.ToImmutableDictionary(StringComparer.Ordinal));
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw new AuthException(AuthError.Storage, "无法恢复本地登录信息，请清除本地会话后重新扫码。");
        }
    }

    public async Task SaveAsync(AuthContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (!ValidDevice(context.DeviceId) || context.Cookies.Count > 4 || context.Cookies.Any(p => !ValidCookie(p.Key, p.Value)))
                throw new InvalidDataException();
            var plain = JsonSerializer.SerializeToUtf8Bytes(new StoredSession(1, context.DeviceId, context.Cookies.ToDictionary()));
            byte[] encrypted;
            try { encrypted = protector.Protect(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            if (encrypted.Length > Limit) throw new InvalidDataException();
            await WriteAtomicAsync(DevicePath, System.Text.Encoding.ASCII.GetBytes(context.DeviceId), cancellationToken).ConfigureAwait(false);
            await WriteAtomicAsync(SessionPath, encrypted, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw new AuthException(AuthError.Storage, "本次已登录，但保存登录信息失败；下次启动可能需要重新扫码。");
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(SessionPath);
            return Task.CompletedTask;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw new AuthException(AuthError.Storage, "本地登录信息未能清除，请重试清除后再关闭应用。");
        }
    }

    private static bool ValidDevice(string device) => device is { Length: 52 } && device.All(char.IsAsciiHexDigit);
    private static bool ValidCookie(string name, SessionCookie cookie) =>
        name is "MUSIC_U" or "MUSIC_A" or "__csrf" or "NMTID" &&
        cookie is not null && cookie.Value is { Length: > 0 and <= 16384 } &&
        !cookie.Value.Any(char.IsControl);

    private static bool IsStorageFailure(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException
        or CryptographicException or JsonException or ArgumentException or NotSupportedException;

    /// <summary>
    /// 取消只发生在原子提交之前。提交后返回成功，不因迟到取消把已落盘事实伪装成失败；
    /// 登录用例在提交后再次校验代次，若已取消则负责补偿清除。
    /// </summary>
    internal static async Task WriteAtomicAsync(string destination, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
