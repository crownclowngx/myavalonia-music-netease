using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.ComponentModel;
using MusicNetEasePlugin.Infrastructure.Ui;

namespace MusicNetEasePlugin.Features.Settings;

/// <summary>窗口选择器属于 View；Dock 换窗或重新绑定后旧选择结果必须失效，不销毁单例 Tool。</summary>
public partial class MusicSettingsView : UserControl
{
    private long _generation;
    private bool _choosing;
    private MusicSettingsTool? _loadedModel;
    private readonly Func<TopLevel, Task<string?>> _pickDirectory;
    private MusicSettingsTool? _observedModel;
    private bool? _wide;
    public ViewMotion Motion { get; }
    public MusicSettingsView() : this(PickDirectoryAsync) { }
    // 仅替换系统选择器边界，Headless 仍执行真实 View 的挂载、重绑和草稿保护逻辑。
    internal MusicSettingsView(Func<TopLevel, Task<string?>> pickDirectory)
    {
        _pickDirectory = pickDirectory;
        Motion = new(this);
        InitializeComponent();
        DataContextChanged += (_, _) => { _generation++; Bind(); LoadSettings(); };
        SizeChanged += (_, _) => ApplyWidth();
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); _generation++; Bind(); LoadSettings(); }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    { _generation++; Unbind(); base.OnDetachedFromVisualTree(e); }
    private void Bind()
    {
        Unbind();
        if (VisualRoot is null) return;
        _observedModel = DataContext as MusicSettingsTool;
        if (_observedModel is not null) _observedModel.PropertyChanged += ModelChanged;
        Motion.Bind(_observedModel?.Preferences);
    }
    private void Unbind()
    {
        if (_observedModel is not null) _observedModel.PropertyChanged -= ModelChanged;
        _observedModel = null;
        Motion.Bind(null);
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MusicSettingsTool.Message)) Motion.FadeIn(SettingsStatus);
    }
    private void ApplyWidth()
    {
        var wide = Bounds.Width >= 600;
        if (_wide == wide) return;
        _wide = wide;
        // 底部停靠可以并排利用宽度；侧栏仍使用自然滚动的紧凑分组，不强迫 Host 满足最小宽度。
        SettingsLayout.ColumnDefinitions = new(wide ? "2*,3*" : "*");
        Grid.SetColumn(RuntimeSection, wide ? 1 : 0);
        Grid.SetRow(RuntimeSection, wide ? 0 : 1);
        Grid.SetRowSpan(RuntimeSection, wide ? 3 : 1);
        Grid.SetRow(AppearanceSection, wide ? 1 : 2);
    }
    private void LoadSettings()
    {
        if (VisualRoot is null || DataContext is not MusicSettingsTool model || ReferenceEquals(model, _loadedModel)) return;
        _loadedModel = model;
        _ = model.EnsureLoadedAsync();
    }
    private async void ChooseDirectory(object? sender, RoutedEventArgs args) => await ChooseDirectoryAsync();
    internal async Task ChooseDirectoryAsync()
    {
        if (_choosing || DataContext is not MusicSettingsTool model || TopLevel.GetTopLevel(this) is not { } top) return;
        var generation = _generation;
        var draftVersion = model.DraftVersion;
        _choosing = true;
        try
        {
            var selected = await _pickDirectory(top);
            if (generation != _generation || draftVersion != model.DraftVersion || !ReferenceEquals(DataContext, model) || VisualRoot is null) return;
            if (selected is { } path) model.DirectoryPath = path;
        }
        catch (Exception) { /* 系统选择器取消或不可用不改变已保存配置，也不让 async void 异常逃逸至 Host。 */ }
        finally { _choosing = false; }
    }
    private static async Task<string?> PickDirectoryAsync(TopLevel top)
    {
        var selected = await top.StorageProvider.OpenFolderPickerAsync(new() { Title = "选择 LibVLC 运行库目录", AllowMultiple = false });
        return selected.FirstOrDefault()?.TryGetLocalPath();
    }
}
