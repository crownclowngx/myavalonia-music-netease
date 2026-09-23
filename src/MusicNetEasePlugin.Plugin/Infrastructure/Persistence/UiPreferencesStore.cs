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
    private sealed record Settings(int SchemaVersion, bool ReduceMotion);

    public async Task<bool> LoadReduceMotionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return false;
        if (new FileInfo(_path).Length > 4096) throw new JsonException("界面偏好文件过大。");
        var value = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false), Options);
        if (value is null || value.SchemaVersion != 1) throw new JsonException("界面偏好版本无效。");
        return value.ReduceMotion;
    }

    public async Task SaveReduceMotionAsync(bool reduceMotion, CancellationToken cancellationToken)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Settings(1, reduceMotion), Options), cancellationToken).ConfigureAwait(false);
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
