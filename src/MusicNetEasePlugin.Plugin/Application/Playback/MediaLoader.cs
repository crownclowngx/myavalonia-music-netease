namespace MusicNetEasePlugin.Application.Playback;

/// <summary>
/// 单次加载的有限恢复规则。网络最多重试一次、410 最多重新解析一次，共享最多三次媒体请求预算。
/// 只重试未完成的下载，不重试解码或账号问题；TimeProvider 使等待可取消且可确定性验证。
/// </summary>
internal sealed class MediaLoader(IPlaybackResourceResolver resolver, IMediaBuffer buffer, TimeProvider time)
{
    public async Task<(BufferedMedia Media, PlaybackResource Resource)> LoadAsync(long id, MusicSession session,
        CancellationToken ct, Action<BufferProgress> progress, Action<string> feedback)
    {
        var resource = await resolver.ResolveAsync(id, session, ct).ConfigureAwait(false);
        var refreshed = false; var retried = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (resource.Address is null) throw new MusicException(MusicError.Unavailable, "当前账号暂不能播放这首歌曲。");
            try { return (await buffer.DownloadAsync(resource, ct, progress).ConfigureAwait(false), resource); }
            catch (MusicException ex) when (attempt < 2 && ex.Kind == MusicError.AddressExpired && !refreshed)
            {
                refreshed = true; feedback("地址已过期，正在重新获取…");
                resource = await resolver.ResolveAsync(id, session, ct).ConfigureAwait(false);
            }
            catch (MusicException ex) when (attempt < 2 && ex.Kind is MusicError.Network or MusicError.Timeout && !retried)
            {
                retried = true; feedback("网络暂时中断，稍后重试一次…");
                await Task.Delay(TimeSpan.FromMilliseconds(500), time, ct).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException("媒体加载预算已耗尽。");
    }
}
