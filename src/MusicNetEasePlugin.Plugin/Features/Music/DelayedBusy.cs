using CommunityToolkit.Mvvm.ComponentModel;
using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>
/// 业务忙碌立即生效，辅助加载提示延迟 150ms，快速响应不会闪一下。
/// 每轮有独立取消和代次；完成/隐藏后迟到的调度不能复活加载提示。可注入时钟做确定性测试。
/// </summary>
public sealed class DelayedBusy(ILoginUiDispatcher ui, TimeProvider? time = null) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _visible;
    private bool _closed;
    private bool _active = true;
    private bool _busy;
    public bool IsVisible { get => _visible; private set => SetProperty(ref _visible, value); }
    internal Task Pending { get; private set; } = Task.CompletedTask;
    public void Set(bool busy)
    {
        _busy = busy;
        var generation = ++_generation;
        var previous = _pending; _pending = null;
        try { previous?.Cancel(); } catch (ObjectDisposedException) { /* 已完成延迟由自身释放。 */ }
        IsVisible = false;
        if (!busy || !_active || _closed) return;
        var source = _pending = new(); Pending = Show();
        async Task Show()
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150), time ?? TimeProvider.System, source.Token).ConfigureAwait(false);
                ui.Post(() => { if (!_closed && generation == _generation) IsVisible = true; });
            }
            catch (OperationCanceledException) { }
            finally { if (ReferenceEquals(_pending, source)) _pending = null; source.Dispose(); }
        }
    }
    public void SetActive(bool active) { if (_active == active) return; _active = active; Set(_busy); }
    public void Dispose() { _closed = true; Set(false); }
}
