using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Infrastructure.Ui;

/// <summary>容器注入的图片呈现依赖包；只负责组合下载、位图缓存和偏好，不持有任何页面或全局静态实例。</summary>
public sealed class ArtworkContext : IDisposable
{
    private readonly LoginCoordinator _login;
    private readonly ILoginUiDispatcher _ui;
    private long? _account;
    private long _revision = -1;
    private bool _disposed;
    public IAccountImageSource Source { get; }
    public ArtworkCache Cache { get; } = new();
    public UiPreferences Preferences { get; }
    public ArtworkContext(IAccountImageSource source, UiPreferences preferences, LoginCoordinator login, ILoginUiDispatcher ui)
    {
        (Source, Preferences, _login, _ui) = (source, preferences, login, ui);
        _account = login.Snapshot.Account?.Id;
        login.Changed += Changed;
    }
    private void Changed(object? sender, LoginSnapshot snapshot)
    {
        _ui.Post(() =>
        {
            if (_disposed || snapshot.Revision <= _revision) return;
            _revision = snapshot.Revision;
            if (_account == snapshot.Account?.Id) return;
            _account = snapshot.Account?.Id; Cache.Clear();
        });
    }
    public void Dispose() { _disposed = true; _login.Changed -= Changed; Cache.Dispose(); }
}
