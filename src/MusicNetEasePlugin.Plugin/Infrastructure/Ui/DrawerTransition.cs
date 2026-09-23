using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace MusicNetEasePlugin.Infrastructure.Ui;

/// <summary>
/// 仅负责抽屉外壳的有限滑动，不参与导航。内容通过导航绑定立即隐藏，外壳完成退场后才折叠。
/// 每次反向操作取消旧收尾，避免旧的“关闭完成”把刚重新打开的抽屉隐藏。
/// 不维持循环时钟；隐藏窗口、减少动效和视图拆卸都立即收口。
/// </summary>
internal sealed class DrawerTransition(Control shell)
{
    private readonly TranslateTransform _translation = new();
    private CancellationTokenSource? _closing;
    private bool _open;
    public void SetOpen(bool open, double width, bool animate)
    {
        shell.RenderTransform = _translation;
        if (_open == open && animate) return;
        Cancel();
        var changed = _open != open;
        _open = open;
        shell.IsHitTestVisible = open;
        shell.IsEnabled = open;
        _translation.Transitions = null;
        if (open)
        {
            if (!shell.IsVisible) _translation.X = animate ? width : 0;
            shell.IsVisible = true;
        }
        if (animate && changed)
            _translation.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.XProperty,
                Duration = TimeSpan.FromMilliseconds(200), Easing = new QuadraticEaseOut() } };
        _translation.X = open ? 0 : width;
        if (open) return;
        if (!animate) { shell.IsVisible = false; return; }
        var source = _closing = new();
        _ = FinishClose(source);
    }
    private async Task FinishClose(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(200, source.Token);
            if (ReferenceEquals(_closing, source) && !_open) shell.IsVisible = false;
        }
        catch (OperationCanceledException) { /* 新意图或视图拆卸取消退场，不是业务错误。 */ }
        finally { if (ReferenceEquals(_closing, source)) _closing = null; source.Dispose(); }
    }
    private void Cancel() { var source = _closing; _closing = null; source?.Cancel(); }
    public void Reset() { Cancel(); _open = false; _translation.Transitions = null; shell.IsVisible = false; shell.IsHitTestVisible = false; shell.IsEnabled = false; }
}
