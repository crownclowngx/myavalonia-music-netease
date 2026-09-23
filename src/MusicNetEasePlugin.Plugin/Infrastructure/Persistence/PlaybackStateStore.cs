using System.Globalization;
using System.Text.Json;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Persistence;

/// <summary>
/// 每账号独立 JSON；数字身份生成文件名，读写均限 4 MiB。同目录临时文件完全落盘后原子替换，失败保留旧文件。
/// 损坏/未来版本不自动修复为空文件；应用层必须等用户明确新建状态才解除保护。
/// </summary>
internal sealed class PlaybackStateStore(string root) : IPlaybackStateStore
{
    internal const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private string PathFor(long id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        return Path.Combine(root, "playback-state", id.ToString(CultureInfo.InvariantCulture) + ".json");
    }
    public async Task<PlaybackStateRead> LoadAsync(long accountId, CancellationToken ct)
    {
        var path = PathFor(accountId);
        try
        {
            if (!File.Exists(path)) return new(PlaybackStateData.Empty(accountId));
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            if (input.Length > MaximumBytes) return Invalid();
            // 限定流自身长度且拒绝尾部超限，不先按不可信大小分配内存。
            using var memory = new MemoryStream(); var buffer = new byte[65536];
            while (true)
            {
                var read = await input.ReadAsync(buffer, ct).ConfigureAwait(false); if (read == 0) break;
                if (memory.Length + read > MaximumBytes) return Invalid();
                memory.Write(buffer, 0, read);
            }
            var data = JsonSerializer.Deserialize<PlaybackStateData>(memory.ToArray(), Options);
            return data is not null && Valid(data, accountId) ? new(data) : Invalid();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return Invalid(); }
        static PlaybackStateRead Invalid() => new(null, "本地恢复文件无法读取或版本不支持，已保留原文件；新建或清空队列后才会替换。", true);
    }
    public async Task SaveAsync(PlaybackStateData state, CancellationToken ct)
    {
        if (!Valid(state, state.AccountId)) throw new MusicException(MusicError.Storage, "本地播放状态不完整，未写入。");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, Options);
        if (bytes.Length > MaximumBytes)
        {
            // 资料可重新读取，队列身份不能丢弃。先省略可选展示文字，再核验预算；绝不截断队列条目。
            state = state with { Entries = state.Entries.Select(entry => entry with { Name = null, Artists = null, Album = null }).ToArray() };
            bytes = JsonSerializer.SerializeToUtf8Bytes(state, Options);
            if (bytes.Length > MaximumBytes) throw new MusicException(MusicError.Storage, "本地播放状态超过 4 MiB，未替换旧文件。");
        }
        var path = PathFor(state.AccountId); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await output.WriteAsync(bytes, ct).ConfigureAwait(false); await output.FlushAsync(ct).ConfigureAwait(false); }
            ct.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new MusicException(MusicError.Storage, "本地恢复未保存，请检查磁盘空间和权限后重试。"); }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    internal static bool Valid(PlaybackStateData data, long accountId)
    {
        if (data.SchemaVersion != 1 || accountId <= 0 || data.AccountId != accountId || data.Revision is < 0 or > long.MaxValue - 1000000 ||
            !Enum.IsDefined(data.Mode) || data.Volume is < 0 or > 100 || data.PositionMs is < 0 or > 86400000 ||
            data.Entries is null || data.Recent is null || data.Entries.Count > 10000 || data.Recent.Count > 200) return false;
        var ids = new HashSet<Guid>();
        foreach (var entry in data.Entries)
            if (entry is null || entry.TrackId <= 0 || entry.EntryId == Guid.Empty || !ids.Add(entry.EntryId) || entry.SourceId is <= 0 ||
                entry.Source is null || entry.Source.Length > 32 || entry.Name?.Length > 256 || entry.Artists?.Length > 128 || entry.Album?.Length > 256 || entry.DurationMs is < 0 or > 86400000) return false;
        if (data.Entries.Count == 0 ? data.CurrentEntryId is not null || data.PositionMs != 0 : data.CurrentEntryId is null || !ids.Contains(data.CurrentEntryId.Value)) return false;
        var tracks = new HashSet<long>();
        return data.Recent.All(track => track is not null && track.TrackId > 0 && tracks.Add(track.TrackId) && track.Name is not null && track.Name.Length <= 256 &&
            track.Artists is not null && track.Artists.Length <= 128 && track.Album is not null && track.Album.Length <= 256 && track.DurationMs is >= 0 and <= 86400000);
    }
}
