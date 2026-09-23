using System.Text.Json;
using MusicNetEasePlugin.Application.Appearance;

namespace MusicNetEasePlugin.Infrastructure.Persistence;

/// <summary>
/// 界面配置独立保存到 ui-preferences.json。同目录临时文件写完后原子替换，
/// 写入失败保留旧文件；这里不会访问登录凭据或 LibVLC 路径。
/// </summary>
internal sealed class UiPreferencesStore(string directory) : IUiPreferencesStore
{
    private readonly string _path = Path.Combine(directory, "ui-preferences.json");
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private sealed record Settings(int SchemaVersion, bool ReduceMotion, bool? ShowArtwork = null, bool? ShowTranslation = null);

    public async Task<UiPreferencesData> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new();
        if (new FileInfo(_path).Length > 4096) throw new JsonException("界面偏好文件过大。");
        var value = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false), Options);
        // 旧文件只包含减少动画，迁移时新选项使用产品默认值；读取不主动重写文件。
        // 新版本缺字段视为损坏，不把 JSON 默认值误当成用户明确关闭封面的选择。
        return value switch
        {
            { SchemaVersion: 1 } => new(value.ReduceMotion),
            { SchemaVersion: 2, ShowArtwork: { } artwork, ShowTranslation: { } translation } => new(value.ReduceMotion, artwork, translation),
            _ => throw new JsonException("界面偏好版本或字段无效。")
        };
    }

    public async Task SaveAsync(UiPreferencesData preferences, CancellationToken cancellationToken)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Settings(2, preferences.ReduceMotion, preferences.ShowArtwork, preferences.ShowTranslation), Options), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
