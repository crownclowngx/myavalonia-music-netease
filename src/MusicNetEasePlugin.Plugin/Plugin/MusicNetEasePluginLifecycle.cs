using MusicNetEasePlugin.Application.Authentication;
using MyAvaloniaManagement.PluginSdk;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Application.Library;

namespace MusicNetEasePlugin.Plugin;

/// <summary>
/// Host 停止先结束 Document 播放资源会话，再关闭音乐库、账号恢复、队列、歌词、存储、单曲与登录。
/// 每个服务的重复释放等待自身同一个尾任务，因此 SDK 再同步销毁容器也不会重复播放或覆盖恢复位置。
/// 初始化不等待网络或创建原生引擎；最后一个音乐 Document 关闭会单独结束播放资源会话。
/// </summary>
internal sealed class MusicNetEasePluginLifecycle(LoginCoordinator login, PlaybackCoordinator playback, PlaybackQueueCoordinator queue, LyricsCoordinator lyrics,
    PlayerAccountCoordinator accounts, PlaybackPersistence persistence, MusicLibraryCoordinator library, MusicPlaybackLifetime lifetime) : IPluginLifecycle
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await lifetime.ShutdownAsync().ConfigureAwait(false);
        // 先撤销音乐库请求与写后回查，再释放它们依赖的会话；Document 关闭不经过此共享关闭链。
        await library.DisposeAsync().ConfigureAwait(false);
        await accounts.DisposeAsync().ConfigureAwait(false);
        await queue.DisposeAsync().ConfigureAwait(false);
        await lyrics.DisposeAsync().ConfigureAwait(false);
        await persistence.DisposeAsync().ConfigureAwait(false);
        await playback.DisposeAsync().ConfigureAwait(false);
        await login.DisposeAsync().ConfigureAwait(false);
    }
}
