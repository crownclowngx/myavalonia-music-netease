using MusicNetEasePlugin.Application.Authentication;
using MyAvaloniaManagement.PluginSdk;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Plugin;

/// <summary>Host 停止先收口播放与媒体，再撤销登录请求；初始化不等待网络或创建原生引擎。</summary>
internal sealed class MusicNetEasePluginLifecycle(LoginCoordinator login, PlaybackCoordinator playback) : IPluginLifecycle
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await playback.DisposeAsync().ConfigureAwait(false);
        await login.DisposeAsync().ConfigureAwait(false);
    }
}
