using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicNetEasePlugin.Application.Appearance;

namespace MusicNetEasePlugin.Infrastructure.Ui;

/// <summary>
/// 一个 View 的有限动效寿命。只关注有效可见性、窗口状态和用户偏好，
/// 不拥有业务模型；没有计时器，空闲时没有自建渲染循环。
/// 统一关闭许可会撤销 XAML 的短过渡，同时取消最多一个局部淡入。
/// </summary>
public sealed class ViewMotion : ObservableObject
{
    private readonly Control _owner;
    private Window? _window;
    private UiPreferences? _preferences;
    private bool _attached;
    private bool _enabled;
    private bool _active;
    private long _themeRevision;
    private CancellationTokenSource? _fade;
    private readonly List<Visual> _ancestors = [];

    public ViewMotion(Control owner)
    {
        _owner = owner;
        owner.AttachedToVisualTree += (_, _) => Attach();
        owner.DetachedFromVisualTree += (_, _) => Detach();
        owner.ActualThemeVariantChanged += (_, _) => ThemeChanged();
    }

    public bool IsEnabled { get => _enabled; private set => SetProperty(ref _enabled, value); }
    /// <summary>可见工作许可与动画偏好分开；关闭动画不应关闭封面，隐藏窗口则两者都停止。</summary>
    public bool IsActive { get => _active; private set => SetProperty(ref _active, value); }
    internal bool HasActiveFade => _fade is not null;

    public void Bind(UiPreferences? preferences)
    {
        if (_preferences is not null) _preferences.PropertyChanged -= PreferencesChanged;
        _preferences = preferences;
        if (_attached && preferences is not null)
        {
            preferences.PropertyChanged += PreferencesChanged;
            _ = preferences.EnsureLoadedAsync();
        }
        Refresh();
    }

    private void Attach()
    {
        _attached = true;
        // Avalonia 的有效可见性变更事件不是此版本的公开端口。订阅当前视觉祖先的公开
        // IsVisible 属性即可处理 Dock 隐藏但未 Detach 的情况；换父级时 Attach 会重建这组订阅。
        _ancestors.Add(_owner);
        _ancestors.AddRange(_owner.GetVisualAncestors());
        foreach (var ancestor in _ancestors) ancestor.PropertyChanged += AncestorChanged;
        _window = TopLevel.GetTopLevel(_owner) as Window;
        if (_window is not null) _window.PropertyChanged += WindowChanged;
        Bind(_preferences);
    }

    private void Detach()
    {
        _attached = false;
        foreach (var ancestor in _ancestors) ancestor.PropertyChanged -= AncestorChanged;
        _ancestors.Clear();
        if (_window is not null) _window.PropertyChanged -= WindowChanged;
        _window = null;
        if (_preferences is not null) _preferences.PropertyChanged -= PreferencesChanged;
        Refresh();
    }

    private void PreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UiPreferences.ReduceMotion)) Refresh();
    }

    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Visual.IsVisibleProperty) Refresh();
    }

    private void AncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty) Refresh();
    }

    private void Refresh()
    {
        IsActive = _attached && _ancestors.All(visual => visual.IsVisible) && _window?.WindowState != WindowState.Minimized;
        IsEnabled = IsActive && _preferences?.ReduceMotion != true;
        if (!IsEnabled) CancelFade();
    }

    private void ThemeChanged()
    {
        // 主题切换要直接使用新色，不能从旧主题画刷继续插值；下一次 Dispatcher 周期才恢复交互过渡。
        var revision = ++_themeRevision;
        IsEnabled = false;
        CancelFade();
        Dispatcher.UIThread.Post(() => { if (revision == _themeRevision) Refresh(); }, DispatcherPriority.Background);
    }

    public void FadeIn(Control target)
    {
        CancelFade();
        if (!IsEnabled || !target.IsEffectivelyVisible) return;
        var source = _fade = new CancellationTokenSource();
        _ = RunFadeAsync(target, source);
    }

    private async Task RunFadeAsync(Control target, CancellationTokenSource source)
    {
        try
        {
            var animation = new Animation { Duration = TimeSpan.FromMilliseconds(140), Easing = new QuadraticEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.65d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } }
                } };
            await animation.RunAsync(target, source.Token);
        }
        catch (OperationCanceledException) { /* 隐藏、偏好变化或下一次交互覆盖旧淡入是正常结束。 */ }
        finally
        {
            if (ReferenceEquals(_fade, source)) _fade = null;
            source.Dispose();
        }
    }

    private void CancelFade()
    {
        var source = _fade;
        _fade = null;
        source?.Cancel();
    }
}
