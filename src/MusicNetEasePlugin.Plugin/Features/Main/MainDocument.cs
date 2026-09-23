using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MyAvaloniaManagement.PluginSdk;
using MusicNetEasePlugin.Application.Appearance;

namespace MusicNetEasePlugin.Features.Main;

/// <summary>
/// 登录页面仅投影共享账号状态，拥有自己的关闭令牌、订阅和头像任务。
/// 网络、Cookie、持久化与重试由登录服务处理；页面关闭只取消自己发起的操作，不退出共享账号。
/// </summary>
public sealed class MainDocument : ObservableObject, IPluginDocument, IDisposable
{
    private readonly LoginCoordinator _login;
    private readonly ILoginUiDispatcher _dispatcher;
    private readonly IAccountImageSource _images;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly CancellationTokenSource _closing = new();
    private readonly CancellationTokenRegistration _lifetimeRegistration;
    private readonly object _imageSync = new();
    private CancellationTokenSource? _avatarCancellation;
    private Task _avatarWork = Task.CompletedTask;
    private long _avatarGeneration;
    private string? _avatarAddress;
    private bool _visible = true;
    private int _closed;
    private int _disposed;
    private LoginSnapshot _snapshot = new(-1, LoginStage.SignedOut, "");
    public Music.MusicWorkspace? Music { get; }
    private byte[]? _qrImageBytes;
    private byte[]? _avatarImageBytes;
    private DocumentPresentationState _presentation = new("网易云音乐");

    public MainDocument(LoginCoordinator login, ILoginUiDispatcher dispatcher, IAccountImageSource images, IDocumentLifetime lifetime,
        Music.MusicWorkspace? music = null, UiPreferences? preferences = null, Settings.MusicSettingsTool? settings = null, MusicNetEasePlugin.Infrastructure.Ui.ArtworkContext? artwork = null)
    {
        Music = music; Settings = settings; Artwork = artwork;
        (_login, _dispatcher, _images) = (login, dispatcher, images);
        LoginCommand = new AsyncRelayCommand(() => _login.StartAsync(_owner, _closing.Token, LoginMethodIndex == 0 ? LoginMethod.WeChat : LoginMethod.NeteaseApp), CanStart, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        Preferences = preferences;
        if (preferences is not null) preferences.PropertyChanged += PreferencesChanged;
        (_login, _dispatcher, _images) = (login, dispatcher, images);
        StartLoginCommand = new AsyncRelayCommand(() => _login.StartAsync(_owner, _closing.Token),
            CanStart,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        StartNeteaseLoginCommand = new AsyncRelayCommand(() => { LoginMethodIndex = 1; return _login.StartAsync(_owner, _closing.Token, LoginMethod.NeteaseApp); },
            CanStart,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        CancelLoginCommand = new RelayCommand(() => _login.Cancel(_owner),
            () => !Closed && _snapshot.IsBusy && _login.IsOwnedBy(_owner));
        RestoreCommand = new AsyncRelayCommand(() => _login.RestoreAsync(_owner, _closing.Token, true),
            () => !Closed && !_snapshot.IsBusy && !IsSignedIn);
        RetrySaveCommand = new AsyncRelayCommand(() => _login.RetrySaveAsync(_owner, _closing.Token),
            () => !Closed && !_snapshot.IsBusy && IsSignedIn && !_snapshot.Remembered);
        LogoutCommand = new AsyncRelayCommand(() => _login.LogoutAsync(_owner, _closing.Token),
            () => !Closed && _snapshot.Stage != LoginStage.SigningOut);
        _login.Changed += OnLoginChanged;
        Apply(_login.Snapshot);
        _lifetimeRegistration = lifetime.ClosingToken.Register(Close);
    }

    public Settings.MusicSettingsTool? Settings { get; }
    public MusicNetEasePlugin.Infrastructure.Ui.ArtworkContext? Artwork { get; }
    public string[] LoginMethods { get; } = ["微信扫码", "网易云音乐 App"];
    private int _loginMethodIndex;
    public int LoginMethodIndex { get => _loginMethodIndex; set { if (SetProperty(ref _loginMethodIndex, value)) { _login.Cancel(_owner); OnPropertyChanged(nameof(QrInstruction)); } } }
    public IAsyncRelayCommand LoginCommand { get; }
    public string LoginAction => _snapshot.Stage switch { LoginStage.CreatingQr => "正在生成…", LoginStage.Expired => "刷新二维码", LoginStage.WaitingForScan or LoginStage.WaitingForConfirmation => "刷新二维码", _ => "生成二维码" };
    public DocumentPresentationState Presentation => _presentation;
    public UiPreferences? Preferences { get; }
    public event EventHandler? PresentationChanged;
    public IAsyncRelayCommand StartLoginCommand { get; }
    public IAsyncRelayCommand StartNeteaseLoginCommand { get; }
    public IRelayCommand CancelLoginCommand { get; }
    public IAsyncRelayCommand RestoreCommand { get; }
    public IAsyncRelayCommand RetrySaveCommand { get; }
    public IAsyncRelayCommand LogoutCommand { get; }
    public bool IsSignedIn => _snapshot.Account is not null;
    public string AccountName => _snapshot.Account?.Nickname ?? "还未登录";
    public string AccountId => _snapshot.Account is { } account ? $"网易账号 · {account.Id}" : "使用手机扫码连接你的音乐账号";
    public string StatusMessage => _snapshot.Message;
    public string QrInstruction => LoginMethodIndex == 0
        ? "使用微信扫一扫，并确认授权登录网易云音乐。" : "使用网易云音乐 App 扫码，并在手机上确认登录。";
    public bool IsBusy => _snapshot.IsBusy;
    public bool NeedsSaveRetry => IsSignedIn && !_snapshot.Remembered;
    public bool NeedsCleanup => _snapshot.CleanupRequired;
    public string SessionHint => _snapshot.Remembered ? "登录信息已安全保存，下次打开将检查并恢复。" : "登录信息尚未保存。";
    public byte[]? QrImageBytes { get => _qrImageBytes; private set => SetProperty(ref _qrImageBytes, value); }
    public byte[]? AvatarImageBytes { get => _avatarImageBytes; private set => SetProperty(ref _avatarImageBytes, value); }
    private bool Closed => Volatile.Read(ref _closed) != 0;
    private bool CanStart() => !Closed && !IsSignedIn && _snapshot.Stage is not
        (LoginStage.CreatingQr or LoginStage.Restoring or LoginStage.Verifying or LoginStage.SigningOut);

    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(activation.Title))
        {
            _presentation = new(activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        // 恢复操作由共享服务跟踪，不能阻塞 Host 创建 Document。关闭通过 IDocumentLifetime 单独传递。
        if (!Closed) _ = _login.RestoreAsync(_owner, _closing.Token);
        return ValueTask.CompletedTask;
    }

    private void OnLoginChanged(object? sender, LoginSnapshot snapshot) => _dispatcher.Post(() => Apply(snapshot));

    private void Apply(LoginSnapshot snapshot)
    {
        if (Closed || snapshot.Revision <= _snapshot.Revision) return;
        _snapshot = snapshot;
        QrImageBytes = snapshot.QrImage;
        if (_avatarAddress != snapshot.Account?.AvatarUrl)
        {
            _avatarAddress = snapshot.Account?.AvatarUrl;
            StartAvatar(_avatarAddress);
        }
        foreach (var property in new[] { nameof(IsSignedIn), nameof(AccountName), nameof(AccountId), nameof(StatusMessage),
            nameof(IsBusy), nameof(NeedsSaveRetry), nameof(NeedsCleanup), nameof(SessionHint), nameof(QrInstruction) })
            OnPropertyChanged(property);
        LoginCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(LoginAction));
        StartLoginCommand.NotifyCanExecuteChanged();
        StartNeteaseLoginCommand.NotifyCanExecuteChanged();
        CancelLoginCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        RetrySaveCommand.NotifyCanExecuteChanged();
        LogoutCommand.NotifyCanExecuteChanged();
    }

    public void SetVisible(bool visible) { if (_visible == visible) return; _visible = visible; StartAvatar(_avatarAddress); }
    private void PreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(UiPreferences.ShowArtwork)) StartAvatar(_avatarAddress); }
    private void StartAvatar(string? address)
    {
        lock (_imageSync)
        {
            if (Closed) return;
            _avatarCancellation?.Cancel();
            _avatarCancellation?.Dispose();
            _avatarCancellation = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
            var generation = ++_avatarGeneration;
            AvatarImageBytes = null;
            _avatarWork = LoadAvatarAsync(_avatarWork, _visible && Preferences?.ShowArtwork != false ? address : null, generation, _avatarCancellation.Token);
        }
    }

    private async Task LoadAvatarAsync(Task previous, string? address, long generation, CancellationToken token)
    {
        try
        {
            await previous.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (address is null) return;
            var bytes = await _images.LoadAsync(address, token).ConfigureAwait(false);
            _dispatcher.Post(() =>
            {
                if (!Closed && generation == _avatarGeneration && !token.IsCancellationRequested) AvatarImageBytes = bytes;
            });
        }
        catch (Exception) { /* 头像是可选展示；取消、下载或解码失败保留占位图，不修改账号结果。 */ }
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _login.Changed -= OnLoginChanged;
        if (Preferences is not null) Preferences.PropertyChanged -= PreferencesChanged;
        AvatarImageBytes = null;
        _closing.Cancel();
        _login.Cancel(_owner);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Close();
        Task avatar;
        lock (_imageSync) { _avatarCancellation?.Cancel(); avatar = _avatarWork; }
        // 头像 continuation 只 Post 而不等待 UI，因此同步 Scope 释放不会形成 UI 死锁。
        avatar.GetAwaiter().GetResult();
        _avatarCancellation?.Dispose();
        _lifetimeRegistration.Dispose();
        _closing.Dispose();
    }
}
