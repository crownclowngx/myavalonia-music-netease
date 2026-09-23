using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Application.Appearance;

/// <summary>仅保存本插件的界面偏好，文件边界与账号、运行库配置分开。</summary>
public sealed record UiPreferencesData(bool ReduceMotion = false, bool ShowArtwork = true, bool ShowTranslation = true,
    double DrawerWidth = 360, bool ComfortableDensity = false);

public interface IUiPreferencesStore
{
    Task<UiPreferencesData> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(UiPreferencesData preferences, CancellationToken cancellationToken);
}

/// <summary>
/// 插件容器共享的界面偏好。用户操作立即影响所有视图，磁盘写入串行执行；
/// 保存失败只表示下次启动不能恢复，不撤销用户本次减少动画的选择。
/// 不拥有 View、动画或播放器，也不解析 Host 的服务容器。
/// </summary>
public sealed class UiPreferences(IUiPreferencesStore store, ILoginUiDispatcher ui) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _closing = new();
    private readonly SemaphoreSlim _writes = new(1);
    private Task? _loading;
    private Task _saving = Task.CompletedTask;
    private long _revision;
    private bool _reduceMotion;
    private bool _showArtwork = true;
    private bool _showTranslation = true;
    private double _drawerWidth = 360;
    private bool _comfortableDensity;
    private bool _disposed;
    private string _message = "";

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (_disposed || !SetProperty(ref _reduceMotion, value)) return;
            Interlocked.Increment(ref _revision);
            Save();
        }
    }
    public bool ShowArtwork
    {
        get => _showArtwork;
        set { if (!_disposed && SetProperty(ref _showArtwork, value)) { Interlocked.Increment(ref _revision); Save(); } }
    }
    public bool ShowTranslation
    {
        get => _showTranslation;
        set { if (!_disposed && SetProperty(ref _showTranslation, value)) { Interlocked.Increment(ref _revision); Save(); } }
    }

    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>保存用户希望的逻辑宽度；窗口收窄只限制布局，不能覆盖这个偏好。</summary>
    public double DrawerWidth
    {
        get => _drawerWidth;
        set { if (!_disposed && double.IsFinite(value) && SetProperty(ref _drawerWidth, Math.Clamp(value, 320, 480))) { Interlocked.Increment(ref _revision); Save(); } }
    }
    public bool ComfortableDensity
    {
        get => _comfortableDensity;
        set { if (!_disposed && SetProperty(ref _comfortableDensity, value)) { Interlocked.Increment(ref _revision); Save(); } }
    }
    public IRelayCommand RetrySaveCommand => field ??= new RelayCommand(Save);
    internal Task PendingSave => _saving;

    /// <summary>每个容器只读取一次；迟到读取不能覆盖已经编辑的值。</summary>
    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        var revision = _revision;
        try
        {
            var value = await store.LoadAsync(_closing.Token).ConfigureAwait(false);
            ui.Post(() =>
            {
                if (_disposed || revision != _revision) return;
                SetProperty(ref _reduceMotion, value.ReduceMotion, nameof(ReduceMotion));
                SetProperty(ref _showArtwork, value.ShowArtwork, nameof(ShowArtwork));
                SetProperty(ref _showTranslation, value.ShowTranslation, nameof(ShowTranslation));
                SetProperty(ref _drawerWidth, value.DrawerWidth, nameof(DrawerWidth));
                SetProperty(ref _comfortableDensity, value.ComfortableDensity, nameof(ComfortableDensity));
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { PostMessage(revision, "界面偏好读取失败，暂用默认动效；更改选项可重新保存。"); }
    }

    private void Save()
    {
        if (_disposed) return;
        var revision = _revision;
        var value = new UiPreferencesData(ReduceMotion, ShowArtwork, ShowTranslation, DrawerWidth, ComfortableDensity);
        // 保留所有未完成任务供容器释放收口；同一时刻只有一个写操作触达文件。
        var next = SaveAsync(revision, value);
        _saving = _saving.IsCompleted ? next : Task.WhenAll(_saving, next);
    }

    private async Task SaveAsync(long revision, UiPreferencesData value)
    {
        try
        {
            await _writes.WaitAsync(_closing.Token).ConfigureAwait(false);
            try
            {
                if (revision != Volatile.Read(ref _revision)) return;
                await store.SaveAsync(value, _closing.Token).ConfigureAwait(false);
                PostMessage(revision, "");
            }
            finally { _writes.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { PostMessage(revision, "界面偏好保存失败；本次选择已生效，可重试保存。"); }
    }

    private void PostMessage(long revision, string message) => ui.Post(() =>
    {
        if (!_disposed && revision == _revision) Message = message;
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // 磁盘任务只 Post，不等待 UI；同步容器释放不会与 Dispatcher 形成互等。
        Task.WhenAll(_loading ?? Task.CompletedTask, _saving).GetAwaiter().GetResult();
        _closing.Dispose();
        _writes.Dispose();
    }
}
