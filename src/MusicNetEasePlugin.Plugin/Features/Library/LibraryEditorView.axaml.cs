using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MusicNetEasePlugin.Features.Library;

/// <summary>编辑外壳只负责焦点和键盘，不访问协议或决定权限。所有输入必须经明确按钮提交，Enter 不隐式执行删除。</summary>
public partial class LibraryEditorView : UserControl
{
    private PlaylistEditor? _model;
    private IInputElement? _returnFocus;
    public LibraryEditorView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => Bind();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (this.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Any(p => !string.IsNullOrEmpty(p.PreeditText))) { e.Handled = true; return; }
                _model?.RequestClose(); e.Handled = true;
            }
            // 文本框中的 Enter 只用于输入，不启动表单；按钮自己的 Enter/Space 仍保留原生行为。
        }, RoutingStrategies.Bubble);
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Bind(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Unbind(); base.OnDetachedFromVisualTree(e); }
    private void Bind() { Unbind(); _model = DataContext as PlaylistEditor; if (_model is not null) _model.PropertyChanged += ModelChanged; }
    private void Unbind() { if (_model is not null) _model.PropertyChanged -= ModelChanged; _model = null; }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlaylistEditor.Page) || _model is null) return;
        if (_model.IsOpen)
        {
            var current = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (current is Control control && !control.GetVisualAncestors().Contains(this)) _returnFocus = current;
            Dispatcher.UIThread.Post(() =>
            {
                if (_model?.IsOpen != true || !IsEffectivelyVisible) return;
                EditorScroll.Offset = default;
                // 危险确认和放弃草稿默认聚焦取消；普通表单聚焦首字段。
                if (_model.IsConfirmation || _model.IsDiscardPage) CloseEditor.Focus();
                else if (_model.IsIdPage) PlaylistIdInput.Focus();
                else if (_model.IsNamePage) PlaylistNameInput.Focus();
                else if (_model.IsAddPage) TargetPlaylistList.Focus();
                else CloseEditor.Focus();
            });
        }
        else if (_returnFocus is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } target) { target.Focus(); _returnFocus = null; }
    }
}
