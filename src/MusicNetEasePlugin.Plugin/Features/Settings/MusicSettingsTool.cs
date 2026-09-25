using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Application.Appearance;

namespace MusicNetEasePlugin.Features.Settings;

/// <summary>
/// Tool 模型由插件容器持有，隐藏/浮动只改变 View。账号摘要来自唯一登录协调器；
/// 路径草稿、保存值与实际加载值分开，避免保存按钮给出“已经热换 DLL”的错误承诺。
/// </summary>
public sealed class MusicSettingsTool : ObservableObject, IDisposable
{
    private readonly LoginCoordinator _login;
    private readonly ILibVlcSettingsStore _store;
    private readonly ILibVlcDirectoryProbe _probe;
    private readonly IPlaybackRuntimeStatus _runtime;
    private readonly ILoginUiDispatcher _ui;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly CancellationTokenSource _closing = new();
    private readonly SemaphoreSlim _writes = new(1);
    private long _editVersion;
    private long _loginRevision = -1;
    private bool _disposed;
    private string _directory = "";
    private string _saved = "";
    private string _message = "可使用内置 LibVLC，或在内置不可用时配置共享目录。";
    private string _account = "尚未登录";
    private string _session = "";
    private string _candidate = "尚未检查候选运行库。";
    private Task? _initialization;
    private readonly PlaybackPersistence? _playbackState;
    private string _playbackStorageMessage = "";
    private long _storageRevision = -1;
    public MusicSettingsTool(LoginCoordinator login, ILibVlcSettingsStore store, ILibVlcDirectoryProbe probe,
        IPlaybackRuntimeStatus runtime, ILoginUiDispatcher ui, UiPreferences? preferences = null, PlaybackPersistence? playbackState = null, PlayerAccountCoordinator? playerAccounts = null)
    {
        (_login, _store, _probe, _runtime, _ui) = (login, store, probe, runtime, ui);
        Preferences = preferences;
        _playbackState = playbackState;
        RetryPlaybackStorageCommand = new AsyncRelayCommand(() => playbackState?.RetryAsync() ?? Task.CompletedTask);
        if (playbackState is not null) { playbackState.Changed += StorageChanged; StorageChanged(null, playbackState.Snapshot); }
        CheckCommand = new AsyncRelayCommand(CheckAsync);
        SaveCommand = new AsyncRelayCommand(() => SaveAsync(false));
        ClearCommand = new AsyncRelayCommand(() => SaveAsync(true));
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        LogoutCommand = new AsyncRelayCommand(() => _login.LogoutAsync(_owner, _closing.Token));
        RestoreCommand = new AsyncRelayCommand(() => _login.RestoreAsync(_owner, _closing.Token, true));
        RetrySaveLoginCommand = new AsyncRelayCommand(() => _login.RetrySaveAsync(_owner, _closing.Token));
        _login.Changed += LoginChanged;
        _runtime.Changed += RuntimeChanged;
        ApplyLogin(login.Snapshot);
    }
    private bool _advancedExpanded;
    private string _runtimeSummary = "首次播放时检查运行库";
    public bool AdvancedExpanded { get => _advancedExpanded; set => SetProperty(ref _advancedExpanded, value); }
    public string RuntimeSummary { get => _runtimeSummary; private set => SetProperty(ref _runtimeSummary, value); }
    public string DirectoryPath { get => _directory; set { if (SetProperty(ref _directory, value ?? "")) Interlocked.Increment(ref _editVersion); } }
    public string PlaybackStorageMessage { get => _playbackStorageMessage; private set => SetProperty(ref _playbackStorageMessage, value); }
    public IAsyncRelayCommand RetryPlaybackStorageCommand { get; }
    private void StorageChanged(object? sender, PlaybackStorageSnapshot snapshot) => Post(() =>
    { if (snapshot.Revision > _storageRevision) { _storageRevision = snapshot.Revision; PlaybackStorageMessage = snapshot.Message; } });
    public UiPreferences? Preferences { get; }
    public bool NeedsLoginSaveRetry => _login.Snapshot is { Account: not null, Remembered: false };
    internal long DraftVersion => Volatile.Read(ref _editVersion);
    public string SavedDirectory { get => _saved; private set => SetProperty(ref _saved, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string AccountText { get => _account; private set => SetProperty(ref _account, value); }
    public string SessionText { get => _session; private set => SetProperty(ref _session, value); }
    public string CandidateRuntime { get => _candidate; private set => SetProperty(ref _candidate, value); }
    public string ActiveRuntime => _runtime.ActiveDirectory is { } path
        ? $"已绑定运行库（{(_runtime.ActiveSource == RuntimeSource.BuiltIn ? "内置" : "配置")}）：{path}（{_runtime.ActiveVersion}）\n{(_runtime.IsEngineActive ? "音频引擎正在运行。" : "音频引擎已释放，播放时重新创建。")}"
        : _runtime.LoadAttempted ? "原生加载已尝试；变更目录后需重启 Host。" : "尚未加载音频引擎；首次播放时内置目录优先。";
    public IAsyncRelayCommand CheckCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand ClearCommand { get; }
    public IAsyncRelayCommand ReloadCommand { get; }
    public IAsyncRelayCommand LogoutCommand { get; }
    public IAsyncRelayCommand RestoreCommand { get; }
    public IAsyncRelayCommand RetrySaveLoginCommand { get; }

    /// <summary>初始化属于单例模型，重建 View 只再次订阅同一任务，不覆盖尚未保存的草稿。</summary>
    public Task EnsureLoadedAsync() => _initialization ??= ReloadAsync();
    public async Task ReloadAsync()
    {
        if (_disposed) return;
        var revision = Volatile.Read(ref _editVersion);
        // 读取与保存也串行，确保 UI 的已保存值按真实磁盘操作顺序投递。
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            var saved = await _store.LoadAsync(_closing.Token).ConfigureAwait(false);
            Post(() =>
            {
                SavedDirectory = saved.CustomDirectory ?? "";
                if (revision == _editVersion) DirectoryPath = SavedDirectory;
                OnPropertyChanged(nameof(ActiveRuntime));
            });
        }
        catch (MusicException ex) { Post(() => Message = ex.Message); }
        catch (OperationCanceledException) { }
        finally { _writes.Release(); }
        await RefreshCandidateAsync().ConfigureAwait(false);
    }
    private async Task RefreshCandidateAsync()
    {
        try
        {
            var selection = await Task.Run(() => _runtime.PreviewAsync(_closing.Token)).ConfigureAwait(false);
            Post(() => { RuntimeSummary = selection.IsReady ? "播放库可用" : "播放库不可用，请配置目录"; if (!selection.IsReady) AdvancedExpanded = true; CandidateRuntime = $"下次加载：{selection.Summary}\n{selection.Directory}\n" +
                (selection.BuiltIn.IsValid ? "" : selection.BuiltIn.Summary) +
                (selection.Configured is { IsValid: false } configured ? "\n" + configured.Summary : ""); });
        }
        catch (MusicException ex) { Post(() => { CandidateRuntime = ex.Message; RuntimeSummary = "运行库检查失败"; AdvancedExpanded = true; }); }
        catch (OperationCanceledException) { }
    }
    private async Task CheckAsync()
    {
        var revision = Volatile.Read(ref _editVersion);
        var path = DirectoryPath.Trim();
        var result = await Task.Run(() => _probe.Check(path)).ConfigureAwait(false);
        Post(() => { if (revision == _editVersion) Message = result.Summary; OnPropertyChanged(nameof(ActiveRuntime)); });
    }
    private async Task SaveAsync(bool clear)
    {
        var revision = Volatile.Read(ref _editVersion);
        var path = clear ? null : DirectoryPath.Trim();
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            if (!clear)
            {
                var result = await Task.Run(() => _probe.Check(path!)).ConfigureAwait(false);
                if (revision != Volatile.Read(ref _editVersion)) return;
                if (!result.IsValid) { Post(() => Message = result.Summary); return; }
                path = result.Directory;
            }
            await _store.SaveAsync(new(CustomDirectory: path), _closing.Token).ConfigureAwait(false);
            Post(() =>
            {
                SavedDirectory = path ?? "";
                if (clear && revision == _editVersion) DirectoryPath = "";
                Message = _runtime.LoadAttempted ? "设置已保存；当前运行库保持不变，重启 Host 后按内置优先规则生效。"
                    : "设置已保存；首次播放时优先使用有效内置库，其次使用配置目录。";
                OnPropertyChanged(nameof(ActiveRuntime));
            });
            await RefreshCandidateAsync().ConfigureAwait(false);
        }
        catch (MusicException ex) { Post(() => Message = ex.Message); }
        catch (OperationCanceledException) { }
        finally { _writes.Release(); }
    }
    private void LoginChanged(object? sender, LoginSnapshot snapshot) => Post(() => ApplyLogin(snapshot));
    private void RuntimeChanged(object? sender, EventArgs args) => Post(() => { OnPropertyChanged(nameof(ActiveRuntime)); RuntimeSummary = _runtime.ActiveDirectory is not null ? _runtime.IsEngineActive ? "播放库已就绪" : "引擎已释放，播放时重新创建" : "播放库加载失败，请检查配置"; if (_runtime.ActiveDirectory is null) AdvancedExpanded = true; });
    private void ApplyLogin(LoginSnapshot snapshot)
    {
        if (snapshot.Revision <= _loginRevision) return;
        _loginRevision = snapshot.Revision;
        AccountText = snapshot.Account is { } account ? $"{account.Nickname} · {account.Id}" : "尚未登录";
        SessionText = snapshot.Message;
        OnPropertyChanged(nameof(NeedsLoginSaveRetry));
    }
    private void Post(Action action) => _ui.Post(() => { if (!_disposed) action(); });
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _login.Changed -= LoginChanged;
        _runtime.Changed -= RuntimeChanged;
        if (_playbackState is not null) _playbackState.Changed -= StorageChanged;
        _closing.Cancel();
        _login.Cancel(_owner);
        // 后台命令只向 UI 投递，不等待 UI；容器释放可安全等到取消与原子保存收口后销毁 CTS。
        Task.WhenAll(new[] { _initialization, CheckCommand.ExecutionTask, SaveCommand.ExecutionTask,
            ClearCommand.ExecutionTask, ReloadCommand.ExecutionTask, LogoutCommand.ExecutionTask,
            RestoreCommand.ExecutionTask, RetrySaveLoginCommand.ExecutionTask, RetryPlaybackStorageCommand.ExecutionTask }.OfType<Task>()).GetAwaiter().GetResult();
        _closing.Dispose();
    }
}
