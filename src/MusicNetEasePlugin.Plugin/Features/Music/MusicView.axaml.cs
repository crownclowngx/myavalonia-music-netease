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
    private bool _overlay;
    public static readonly DirectProperty<MusicView, MusicWorkspace?> ModelProperty = AvaloniaProperty.RegisterDirect<MusicView, MusicWorkspace?>(nameof(Model), view => view.Model);
    public MusicWorkspace? Model => DataContext as MusicWorkspace;
    public ViewMotion Motion { get; }
    public MusicView()
    {
        Motion = new(this); InitializeComponent();
        DataContextChanged += (_, _) => { RaisePropertyChanged(ModelProperty, _model, Model); if (VisualRoot is not null) Bind(); };
        SizeChanged += (_, _) => { Model?.Navigation.SetAvailableSize(Bounds.Width, Bounds.Height); Layout(); };
        AddHandler(KeyDownEvent, PageKeyDown, RoutingStrategies.Bubble);
        GotFocus += (_, e) =>
        {
            if (Model?.Navigation is { IsLyrics: false, IsQueuePage: false } && e.Source is Control control && control.GetVisualAncestors().Contains(ContentLayout))
                _browseFocus = control.FindAncestorOfType<ListBoxItem>() ?? control;
        };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Bind(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Unbind(); base.OnDetachedFromVisualTree(e); }
    private void Bind()
    {
        Unbind(); _model = Model; Motion.Bind(_model?.Preferences);
        if (_model is not null) { if (_model.Preferences is not null) _model.Preferences.PropertyChanged += PreferencesChanged; _model.Navigation.PropertyChanged += NavigationChanged; _model.Navigation.SetAvailableSize(Bounds.Width, Bounds.Height); }
        Layout();
    }
    private void Unbind() { if (_model is not null) { _model.Navigation.PropertyChanged -= NavigationChanged; if (_model.Preferences is not null) _model.Preferences.PropertyChanged -= PreferencesChanged; } _model = null; _browseFocus = null; Motion.Bind(null); }
    private void PreferencesChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == "ShowArtwork") Layout(); }
    private void NavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MusicNavigation.IsQueueBeside)) Layout();
        var overlay = Model?.Navigation is { IsLyrics: true } or { IsQueuePage: true };
        if (_overlay && !overlay)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_browseFocus is Control { IsEffectivelyVisible: true } control) control.Focus(); });
        _overlay = overlay;
    }
    private void Layout()
    {
        var large = Bounds.Width >= 1000 && Bounds.Height >= 550 && Model?.Preferences?.ShowArtwork != false;
        LargeCover.IsVisible = large; LyricsLayout.ColumnDefinitions = new(large ? "216,*" : "0,*"); LyricsLayout.ColumnSpacing = large ? 16 : 0;
        var beside = Model?.Navigation.IsQueueBeside == true;
        ContentLayout.ColumnDefinitions = new(beside ? "*,304" : "*,0");
        ContentLayout.ColumnSpacing = beside ? 12 : 0;
        Grid.SetColumn(QueuePanel, beside ? 1 : 0);
    }
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
        if (e.Key == Key.Escape && m.Navigation.CanBack) { m.Navigation.Back(); e.Handled = true; }
        // Space 不覆盖文本编辑、滑块、按钮和列表自身的选择语义，也不注册全局快捷键。
        else if (e.Key == Key.Space && !IsInteractiveChild(e.Source) && e.Source is Control c && c.FindAncestorOfType<ListBox>() is null && m.Player.ToggleCommand.CanExecute(null))
        { m.Player.ToggleCommand.Execute(null); e.Handled = true; }
    }
}
