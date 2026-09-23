using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace MusicNetEasePlugin.Features.Settings;

/// <summary>窗口选择器属于 View；Dock 换窗或重新绑定后旧选择结果必须失效，不销毁单例 Tool。</summary>
public partial class MusicSettingsView : UserControl
{
    private long _generation;
    private bool _choosing;
    private MusicSettingsTool? _loadedModel;
    private readonly Func<TopLevel, Task<string?>> _pickDirectory;
    public MusicSettingsView() : this(PickDirectoryAsync) { }
    // 仅替换系统选择器边界，Headless 仍执行真实 View 的挂载、重绑和草稿保护逻辑。
    internal MusicSettingsView(Func<TopLevel, Task<string?>> pickDirectory)
    {
        _pickDirectory = pickDirectory;
        InitializeComponent();
        DataContextChanged += (_, _) => { _generation++; LoadSettings(); };
    }
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); _generation++; LoadSettings(); }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    { _generation++; base.OnDetachedFromVisualTree(e); }
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
