using MusicNetEasePlugin.Application.Authentication;
using MyAvaloniaManagement.PluginSdk;

namespace MusicNetEasePlugin.Plugin;

/// <summary>Host 停止前先收口登录任务；初始化不等待网络，避免账号故障阻塞插件加载。</summary>
internal sealed class MusicNetEasePluginLifecycle(LoginCoordinator login) : IPluginLifecycle
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken) =>
        login.DisposeAsync().AsTask().WaitAsync(cancellationToken);
}
