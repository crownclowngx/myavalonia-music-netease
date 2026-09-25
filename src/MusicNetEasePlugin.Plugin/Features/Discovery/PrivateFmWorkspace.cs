using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Discovery;
using MusicNetEasePlugin.Application.Playback;
namespace MusicNetEasePlugin.Features.Discovery;

/// <summary>页面只投影共享 FM，不拥有会话；关闭一页解订阅，最后页的停止由播放租约处理。</summary>
public sealed class PrivateFmWorkspace : ObservableObject, IDisposable
{
    private readonly PrivateFmCoordinator _fm;
    private readonly IPlayerSession _player;
    private readonly IPrivateFmPlayer _control;
    private readonly ILoginUiDispatcher _ui;
    private bool _closed;
    private string _message = "";
    public PrivateFmWorkspace(PrivateFmCoordinator fm, IPlayerSession player, IPrivateFmPlayer control, ILoginUiDispatcher ui)
    {
        (_fm, _player, _control, _ui) = (fm, player, control, ui);
        StartCommand = new AsyncRelayCommand(() => Run(() => control.StartFmAsync(default)));
        EndCommand = new RelayCommand(control.EndFm);
        NextCommand = new AsyncRelayCommand(() => Run(() => player.NextAsync(false, default)), () => IsActive);
        DislikeCommand = new AsyncRelayCommand(() => Run(() =>
        {
            var state = player.Snapshot;
            return state.CurrentEntryId is { } entry && state.Playback.Track is { } track
                ? control.DislikeFmAsync(new(state.FmSessionId, entry, track.Id, state.AccountEpoch), default) : Task.CompletedTask;
        }), () => IsActive && !fm.Snapshot.FeedbackBusy);
        fm.Changed += FmChanged; player.Changed += PlayerChanged;
    }
    public string Message => _message.Length > 0 ? _message : _fm.Snapshot.Feedback?.Message ?? _fm.Snapshot.Message;
    public string Track => _player.Snapshot.Playback.Track?.Display ?? "尚未选择歌曲";
    public bool IsActive => _player.Snapshot.FmSessionId != Guid.Empty;
    public IAsyncRelayCommand StartCommand { get; }
    public IRelayCommand EndCommand { get; }
    public IAsyncRelayCommand NextCommand { get; }
    public IAsyncRelayCommand DislikeCommand { get; }
    private async Task Run(Func<Task> action)
    {
        _message = "";
        try { await action().ConfigureAwait(false); } catch (OperationCanceledException) { }
        catch (MusicException ex) { _ui.Post(() => { if (!_closed) { _message = ex.Message; Refresh(); } }); }
    }
    private void FmChanged(object? sender, FmSnapshot state) => _ui.Post(() => { if (!_closed && ReferenceEquals(state, _fm.Snapshot)) { _message = ""; Refresh(); } });
    private void PlayerChanged(object? sender, PlayerSessionSnapshot state) => _ui.Post(() => { if (!_closed && state.Revision == _player.Snapshot.Revision) Refresh(); });
    private void Refresh() { OnPropertyChanged(nameof(Message)); OnPropertyChanged(nameof(Track)); OnPropertyChanged(nameof(IsActive)); NextCommand.NotifyCanExecuteChanged(); DislikeCommand.NotifyCanExecuteChanged(); }
    public void Dispose() { if (_closed) return; _closed = true; _fm.Changed -= FmChanged; _player.Changed -= PlayerChanged; }
}
