using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>
/// 曲目列表的公共输入边界。只有行本身的 Enter/双击才激活歌曲，子按钮和滑块遵守自身语义。
/// 命令仍由所属页面提供；列表不关心歌曲来源、队列替换或音频服务。
/// </summary>
public sealed class SongListBox : ListBox
{
    internal bool PointerGestureIsControl { get; set; }
    // 自定义输入规则仍沿用 ListBox 的主题模板，否则派生类型只占位而不生成虚拟化容器。
    protected override Type StyleKeyOverride => typeof(ListBox);
    public static readonly StyledProperty<ICommand?> ActivateCommandProperty = AvaloniaProperty.Register<SongListBox, ICommand?>(nameof(ActivateCommand));
    public ICommand? ActivateCommand { get => GetValue(ActivateCommandProperty); set => SetValue(ActivateCommandProperty, value); }
    public SongListBox()
    {
        // 拖动手柄把捕获交给列表后，DoubleTapped 的 Source 可能已经变成 ListBox。
        // 必须记住按下的原始目标，否则连续拖动会被误认为双击歌曲并启动播放。
        AddHandler(PointerPressedEvent, (_, e) => PointerGestureIsControl = MusicView.IsInteractiveChild(e.Source), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DoubleTapped += (_, e) => { if (!PointerGestureIsControl && !MusicView.IsInteractiveChild(e.Source)) { Activate(); e.Handled = true; } };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Handled) return;
            if (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && ContainerFromIndex(SelectedIndex) is { } row)
            { row.GetVisualDescendants().OfType<SongRowView>().FirstOrDefault()?.OpenMenu(); e.Handled = true; }
            else if (e.Key == Key.Enter && !MusicView.IsInteractiveChild(e.Source)) { Activate(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    private void Activate() { if (SelectedItem is not null && ActivateCommand?.CanExecute(null) == true) ActivateCommand.Execute(null); }
}
