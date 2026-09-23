using System.Text.Json;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Persistence;

/// <summary>
/// 原子保存运行库路径，与 DPAPI 会话文件分离。只替换自己的配置文件，绝不创建、删除或修改运行库目录。
/// 同一容器使用一把异步门序列化读写；取消或写入失败时保留旧配置。
/// </summary>
internal sealed class LibVlcSettingsStore(string dataDirectory) : ILibVlcSettingsStore
{
    private readonly string _path = Path.Combine(dataDirectory, "playback-settings.json");
    private readonly SemaphoreSlim _gate = new(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<LibVlcSettings> LoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return new();
            if (new FileInfo(_path).Length > 16 * 1024) throw new JsonException();
            var value = JsonSerializer.Deserialize<LibVlcSettings>(await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false), JsonOptions);
            if (value is null || value.SchemaVersion != 1 || value.CustomDirectory?.Length > 4096) throw new JsonException();
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { throw new MusicException(MusicError.Storage, "读取播放设置失败；请检查配置文件或重新保存目录。"); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(LibVlcSettings settings, CancellationToken ct)
    {
        if (settings.SchemaVersion != 1 || settings.CustomDirectory?.Length > 4096) throw new ArgumentException("设置格式无效。");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, JsonOptions), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new MusicException(MusicError.Storage, "保存播放设置失败，原配置保持不变。"); }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _gate.Release();
        }
    }
}
