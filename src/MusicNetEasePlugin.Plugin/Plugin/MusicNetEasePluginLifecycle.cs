using MusicNetEasePlugin.Application.Authentication;
using MyAvaloniaManagement.PluginSdk;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Application.Lyrics;

namespace MusicNetEasePlugin.Plugin;

/// <summary>
/// Host 停止先拒绝新的账号恢复，再收口共享队列并保存停止前位置，随后释放歌词、存储、单曲与登录。
/// 每个服务的重复释放等待自身同一个尾任务，因此 SDK 再同步销毁容器也不会重复播放或覆盖恢复位置。
/// 初始化不等待网络或创建原生引擎；页面关闭不走此进程级关闭链。
/// </summary>
internal sealed class MusicNetEasePluginLifecycle(LoginCoordinator login, PlaybackCoordinator playback, PlaybackQueueCoordinator queue, LyricsCoordinator lyrics,
    PlayerAccountCoordinator accounts, PlaybackPersistence persistence) : IPluginLifecycle
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await accounts.DisposeAsync().ConfigureAwait(false);
        await queue.DisposeAsync().ConfigureAwait(false);
        await lyrics.DisposeAsync().ConfigureAwait(false);
        await persistence.DisposeAsync().ConfigureAwait(false);
        await playback.DisposeAsync().ConfigureAwait(false);
        await login.DisposeAsync().ConfigureAwait(false);
    }
}
