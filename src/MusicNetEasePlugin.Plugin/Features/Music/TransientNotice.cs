using CommunityToolkit.Mvvm.ComponentModel;
using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>非阻断成功反馈只保留一项可取消延迟；后一次操作替换前一次，不堆积 DispatcherTimer。错误不走自动消失通道。</summary>
public sealed class TransientNotice(ILoginUiDispatcher ui, TimeProvider? time = null) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _delay;
    private string _text = "";
    private long _generation;
    private bool _closed;
    public string Text { get => _text; private set => SetProperty(ref _text, value); }
    internal Task Pending { get; private set; } = Task.CompletedTask;
    public void Show(string text)
    {
        if (_closed) return;
        Clear(); Text = text; var generation = _generation;
        var delay = _delay = new CancellationTokenSource();
        Pending = Complete();
        async Task Complete()
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(2500), time ?? TimeProvider.System, delay.Token).ConfigureAwait(false);
                ui.Post(() => { if (!_closed && generation == _generation) Text = ""; });
            }
            catch (OperationCanceledException) { }
            finally { delay.Dispose(); }
        }
    }
    public void Clear()
    {
        _generation++;
        try { _delay?.Cancel(); } catch (ObjectDisposedException) { /* 已到期的单次延迟由自身释放。 */ }
        _delay = null; Text = "";
    }
    public void Dispose() { _closed = true; Clear(); }
}
