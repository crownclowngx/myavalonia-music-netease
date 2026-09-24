using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Infrastructure.Ui;

namespace MusicNetEasePlugin.Features.Music;

/// <summary>音乐内容外壳只组织搜索、浏览和按需队列；播放条拥有自己的手势与图片寿命。</summary>
public partial class MusicView : UserControl
{
    private MusicWorkspace? _model;
    private IInputElement? _browseFocus;
    private MusicSidePanel _shownPanel;
    private readonly DrawerTransition _drawerTransition;
    private double _resizeDraft = 360;
    private bool _resizing;
    private IPointer? _resizePointer;
    private double _resizeStartX, _resizeStartWidth;
    public static readonly DirectProperty<MusicView, MusicWorkspace?> ModelProperty = AvaloniaProperty.RegisterDirect<MusicView, MusicWorkspace?>(nameof(Model), view => view.Model);
    public MusicWorkspace? Model => DataContext as MusicWorkspace;
    public ViewMotion Motion { get; }
    public MusicView()
    {
        Motion = new(this); InitializeComponent();
        _drawerTransition = new(Drawer);
        DataContextChanged += (_, _) => { RaisePropertyChanged(ModelProperty, _model, Model); if (VisualRoot is not null) Bind(); };
        ContentStage.SizeChanged += (_, _) => { Model?.Navigation.SetAvailableSize(ContentStage.Bounds.Width, ContentStage.Bounds.Height); Layout(); };
        Motion.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(ViewMotion.IsActive) or nameof(ViewMotion.IsEnabled)) Layout(); };
        // 使用 Document 坐标计算拖动差值，避免调整宽度后手柄自身坐标改变导致累加误差。
        // 手动管理捕获可明确区分正常松手与被其他控件夺走捕获，只有正常松手写入偏好。
        DrawerResize.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(DrawerResize).Properties.IsLeftButtonPressed) return;
            _resizing = true; _resizeStartX = e.GetPosition(this).X; _resizeDraft = _resizeStartWidth = Model?.Preferences?.DrawerWidth ?? 360;
            DrawerResize.Focus(); _resizePointer = e.Pointer; e.Pointer.Capture(DrawerResize); e.PreventGestureRecognition(); e.Handled = true;
        };
        DrawerResize.PointerMoved += (_, e) => { if (_resizing) { _resizeDraft = Math.Clamp(_resizeStartWidth - (e.GetPosition(this).X - _resizeStartX), 320, 480); Model?.Navigation.SetDesiredWidth(_resizeDraft); e.Handled = true; } };
        DrawerResize.PointerReleased += (_, e) => { if (_resizing) { _resizing = false; SaveDrawerWidth(_resizeDraft); _resizePointer = null; e.Pointer.Capture(null); e.Handled = true; } };
        DrawerResize.PointerCaptureLost += (_, _) => CancelResize();
        DrawerResize.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Left or Key.Right or Key.Home)) return;
            SaveDrawerWidth(e.Key == Key.Home ? 360 : (Model?.Preferences?.DrawerWidth ?? 360) + (e.Key == Key.Left ? 20 : -20)); e.Handled = true;
        };
        AddHandler(KeyDownEvent, PageKeyDown, RoutingStrategies.Bubble);
        GotFocus += (_, e) =>
        {
            if (e.Source is Control control && control.GetVisualAncestors().Contains(ContentLayout))
                _browseFocus = control.FindAncestorOfType<ListBoxItem>() ?? control;
        };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Bind(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Unbind(); base.OnDetachedFromVisualTree(e); }
    private void Bind()
    {
        Unbind(); _model = Model; Motion.Bind(_model?.Preferences);
        if (_model is not null) { if (_model.Preferences is not null) _model.Preferences.PropertyChanged += PreferencesChanged; _model.Navigation.PropertyChanged += NavigationChanged; _model.Navigation.SetDesiredWidth(_model.Preferences?.DrawerWidth ?? 360); _model.Navigation.SetAvailableSize(ContentStage.Bounds.Width, ContentStage.Bounds.Height); }
        _model?.Library?.SetVisible(Motion.IsActive);
        Layout();
    }
    private void Unbind() { CancelResize(); if (_model is not null) { _model.Library?.SetVisible(false); _model.SearchBusy.SetActive(false); _model.Playlists?.Busy.SetActive(false); _model.Navigation.PropertyChanged -= NavigationChanged; if (_model.Preferences is not null) _model.Preferences.PropertyChanged -= PreferencesChanged; } _model = null; _browseFocus = null; _shownPanel = MusicSidePanel.None; _drawerTransition.Reset(); Motion.Bind(null); }
    private void PreferencesChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == "DrawerWidth") _model?.Navigation.SetDesiredWidth(_model.Preferences?.DrawerWidth ?? 360); }
    private void NavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MusicNavigation.SidePanel)) return;
        Layout();
        var panel = Model?.Navigation.SidePanel ?? MusicSidePanel.None;
        if (panel == _shownPanel) return;
        var previous = _shownPanel; _shownPanel = panel;
        // 非模态抽屉不捕获整个页面的焦点；关闭只归还到仍有效的控件。
        if (panel != MusicSidePanel.None)
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (previous == MusicSidePanel.None && focused is Control control && !control.GetVisualAncestors().Contains(Drawer)) _browseFocus = focused;
            CloseDrawerButton.Focus();
        }
        else if (_browseFocus is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } control && control.GetVisualAncestors().Contains(this))
        { if (!control.Focus()) PlayerBar.FocusPanelButton(previous == MusicSidePanel.Queue); }
        else PlayerBar.FocusPanelButton(previous == MusicSidePanel.Queue);
    }
    private void Layout()
    {
        if (Model?.Navigation is not { } navigation) return;
        Model.SearchBusy.SetActive(Motion.IsActive && navigation.IsSearch);
        Model.Library?.SetVisible(Motion.IsActive);
        Model.Playlists?.Busy.SetActive(Motion.IsActive && navigation.IsLibrary);
        RestoreInline.IsVisible = Bounds.Width >= 1000;
        RestoreCompact.IsVisible = Bounds.Width < 1000;
        Drawer.Width = navigation.ActualDrawerWidth;
        ContentLayout.Margin = new(0, 0, navigation.IsBeside ? navigation.ActualDrawerWidth + 12 : 0, 0);
        _drawerTransition.SetOpen(navigation.IsOpen, navigation.ActualDrawerWidth, Motion.IsEnabled);
    }
    private void SaveDrawerWidth(double width)
    {
        if (Model?.Preferences is { } preferences) preferences.DrawerWidth = width;
        Model?.Navigation.SetDesiredWidth(width);
    }
    private void ResetDrawerWidth(object? sender, RoutedEventArgs e) => SaveDrawerWidth(360);
    private void CancelResize() { if (!_resizing) return; _resizing = false; Model?.Navigation.SetDesiredWidth(Model.Preferences?.DrawerWidth ?? 360); var pointer = _resizePointer; _resizePointer = null; if (pointer?.Captured == DrawerResize) pointer.Capture(null); }
    private void ClearSearch(object? sender, RoutedEventArgs e) { if (Model is { } model) model.Keyword = ""; SearchInput.Focus(); }
    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        // 候选组合阶段的 Enter 只交给输入法。读取 TextPresenter 的公开预编辑状态，不能假定所有 IME 都会拦截 KeyDown。
        if (SearchInput.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Any(p => !string.IsNullOrEmpty(p.PreeditText))) return;
        if (e.Key == Key.Enter && Model is { } m) { m.SearchCommand.Execute(null); e.Handled = true; }
    }
    internal static bool IsInteractiveChild(object? source) => source is Control control &&
        (control is Button or TextBox or RangeBase or ComboBox || control.GetVisualAncestors().Any(parent => parent is Button or TextBox or RangeBase or ComboBox));
    private void PageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Model is not { } m) return;
        if (m.Library?.IsOpen == true) { if (e.Key == Key.Escape) { m.Library.RequestClose(); e.Handled = true; } return; }
        if (e.Key == Key.Escape && _resizing) { CancelResize(); e.Handled = true; return; }
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control) { SearchInput.Focus(); SearchInput.SelectAll(); e.Handled = true; return; }
        if (e.Key == Key.Escape && m.Navigation.CanBack) { m.Navigation.Back(); e.Handled = true; }
        // Space 不覆盖文本编辑、滑块、按钮和列表自身的选择语义，也不注册全局快捷键。
        else if (e.Key == Key.Space && !IsInteractiveChild(e.Source) && e.Source is Control c && c.FindAncestorOfType<ListBox>() is null && m.Player.ToggleCommand.CanExecute(null))
        { m.Player.ToggleCommand.Execute(null); e.Handled = true; }
    }
}
