using Avalonia.Controls;
namespace MusicNetEasePlugin.Features.Music;
/// <summary>队列视图只绑定页面投影，不参与音频控制或共享队列寿命。</summary>
public sealed partial class QueueView : UserControl
{
    public QueueView() => InitializeComponent();
}
