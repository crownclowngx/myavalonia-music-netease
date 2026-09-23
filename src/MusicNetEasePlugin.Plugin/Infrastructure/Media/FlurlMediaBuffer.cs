using System.Net.Http;
using Flurl.Http;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Http;

namespace MusicNetEasePlugin.Infrastructure.Media;

internal sealed record MediaLimits(long MaximumBytes, TimeSpan HeaderTimeout, TimeSpan IdleTimeout, TimeSpan TotalTimeout)
{
    public static MediaLimits Default { get; } = new(64 * 1024 * 1024, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(120));
}
/// <summary>
/// 独立无 Cookie 的媒体 Client；逐跳检查地址、逐段计数和空闲预算。下载完成后才发布临时文件。
/// 每个实例占用独立目录和锁文件，残留回收只处理能取得独占锁的本插件目录。
/// </summary>
internal sealed class FlurlMediaBuffer(NeteaseFlurlClients clients, string root, MediaLimits limits) : IMediaBuffer, IDisposable
{
    private readonly object _sync = new();
    private string? _directory;
    private FileStream? _owner;
    private bool _disposed;
    public async Task<BufferedMedia> DownloadAsync(PlaybackResource resource, CancellationToken ct, Action<BufferProgress>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        var address = ValidateAddress(resource.Address ?? throw new MusicException(MusicError.Restricted, "当前歌曲暂不可播放。"));
        using var total = CancellationTokenSource.CreateLinkedTokenSource(ct);
        total.CancelAfter(limits.TotalTimeout);
        string? file = null;
        try
        {
            var directory = GetDirectory();
            file = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".partial");
            for (var redirects = 0; ; redirects++)
            {
                using var response = await clients.Media.Request(address.AbsoluteUri).WithTimeout(limits.HeaderTimeout)
                    .AllowAnyHttpStatus().GetAsync(HttpCompletionOption.ResponseHeadersRead, total.Token).ConfigureAwait(false);
                if (response.StatusCode is >= 300 and < 400)
                {
                    if (redirects >= 3 || !Uri.TryCreate(address, response.Headers.FirstOrDefault("Location"), out var next))
                        throw new MusicException(MusicError.Protocol, "媒体重定向次数过多或地址无效。");
                    address = ValidateAddress(next);
                    continue;
                }
                if (response.StatusCode == 410) throw new MusicException(MusicError.AddressExpired, "播放地址已过期。");
                if (response.StatusCode == 429) throw new MusicException(MusicError.RateLimited, "媒体访问频繁，请稍后重试。");
                if (response.StatusCode is >= 400 and < 500) throw new MusicException(MusicError.Restricted, "媒体访问被拒绝，请主动重试或选择其他歌曲。");
                if (response.StatusCode != 200) throw new MusicException(MusicError.Network, "媒体服务器暂未提供可播放内容，请重试。");
                if (response.ResponseMessage.Content.Headers.ContentLength > limits.MaximumBytes)
                    throw new MusicException(MusicError.Storage, "音频超过本阶段 64 MiB 缓冲上限。");
                await using var input = await response.GetStreamAsync().ConfigureAwait(false);
                await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    var block = new byte[65536];
                    var prefix = new byte[16];
                    var prefixLength = 0;
                    long length = 0;
                    var totalBytes = response.ResponseMessage.Content.Headers.ContentLength;
                    progress?.Invoke(new(0, totalBytes));
                    long lastReport = Environment.TickCount64;
                    while (true)
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                        idle.CancelAfter(limits.IdleTimeout);
                        int read;
                        try { read = await input.ReadAsync(block, idle.Token).ConfigureAwait(false); }
                        catch (IOException) { throw new MusicException(MusicError.Network, "音频传输中断，请重试。"); }
                        if (read == 0) break;
                        length += read;
                        if (length > limits.MaximumBytes) throw new MusicException(MusicError.Storage, "音频超过允许的缓冲大小。");
                        var take = Math.Min(read, prefix.Length - prefixLength);
                        block.AsSpan(0, take).CopyTo(prefix.AsSpan(prefixLength));
                        prefixLength += take;
                        await output.WriteAsync(block.AsMemory(0, read), total.Token).ConfigureAwait(false);
                        if (Environment.TickCount64 - lastReport >= 200)
                        { progress?.Invoke(new(length, totalBytes)); lastReport = Environment.TickCount64; }
                    }
                    if (response.ResponseMessage.Content.Headers.ContentLength is { } promised && length != promised)
                        throw new MusicException(MusicError.Network, "音频内容被截断，请重试。");
                    if (!LooksLikeAudio(prefix.AsSpan(0, prefixLength)))
                        throw new MusicException(MusicError.Decode, "返回内容不是受支持的音频文件。");
                    await output.FlushAsync(total.Token).ConfigureAwait(false);
                    progress?.Invoke(new(length, totalBytes));
                }
                total.Token.ThrowIfCancellationRequested();
                var complete = Path.ChangeExtension(file, ".media");
                File.Move(file, complete);
                return new(complete, DeleteFile);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new MusicException(MusicError.Timeout, "音频下载超时，请重试。"); }
        catch (FlurlHttpException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (FlurlHttpTimeoutException) { throw new MusicException(MusicError.Timeout, "音频连接超时，请重试。"); }
        catch (FlurlHttpException) { throw new MusicException(MusicError.Network, "音频下载失败，请重试。"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new MusicException(MusicError.Storage, "无法完成音频缓冲，请检查磁盘空间和权限。"); }
        finally { if (file is not null) DeleteFile(file); }
    }
    internal static Uri ValidateAddress(Uri address)
    {
        var host = address.IdnHost;
        if (address.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(address.UserInfo) ||
            !address.IsDefaultPort || !(host.EndsWith(".music.126.net", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".music.163.com", StringComparison.OrdinalIgnoreCase)))
            throw new MusicException(MusicError.Protocol, "媒体来源不在允许范围内。");
        // 网易历史地址可能为 HTTP；仅对已核对的 CDN 主机升级 HTTPS，不把凭据发送给媒体服务。
        return address.Scheme == "http" ? new UriBuilder(address) { Scheme = "https", Port = -1 }.Uri : address;
    }
    internal static bool LooksLikeAudio(ReadOnlySpan<byte> bytes) => bytes.Length >= 12 &&
        (bytes.StartsWith("ID3"u8) || bytes.StartsWith("fLaC"u8) || bytes.StartsWith("OggS"u8) ||
        bytes.StartsWith("RIFF"u8) && bytes[8..].StartsWith("WAVE"u8) ||
        bytes[4..].StartsWith("ftyp"u8) || bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0);
    private string GetDirectory()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_directory is not null) return _directory;
            Directory.CreateDirectory(root);
            foreach (var old in Directory.EnumerateDirectories(root, "instance-*"))
            {
                try
                {
                    // 只回收本组件生成的 GUID 目录；拒绝链接。其他实例恰好清理掉目录也是正常竞争。
                    if (!Guid.TryParseExact(Path.GetFileName(old)[9..], "N", out _) ||
                        (File.GetAttributes(old) & FileAttributes.ReparsePoint) != 0) continue;
                    using (new FileStream(Path.Combine(old, ".owner"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        foreach (var item in Directory.EnumerateFiles(old).Where(f => Path.GetFileName(f) != ".owner")) File.Delete(item);
                    File.Delete(Path.Combine(old, ".owner"));
                    Directory.Delete(old);
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            _directory = Path.Combine(root, "instance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _owner = new FileStream(Path.Combine(_directory, ".owner"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            return _directory;
        }
    }
    private static void DeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* 文件句柄未释放时保留残留，由下次持有独占目录锁的实例回收。 */ }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _owner?.Dispose();
            if (_directory is null) return;
            try { foreach (var file in Directory.EnumerateFiles(_directory)) File.Delete(file); Directory.Delete(_directory); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
