using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Infrastructure.Audio;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Infrastructure.Persistence;
using MusicNetEasePlugin.Plugin;

/// <summary>显式只读 M2 探针。既有会话只复制后读取，所有服务使用独立临时根；报告仅含数量、协议与解码事实。</summary>
internal static class DailyPlayerProbe
{
    internal static async Task<int> RunAsync(string sessionDirectory, string output)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "netease-daily-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var results = new List<object>();
        var stage = "restore";
        object report;
        var success = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try
        {
            if (!Path.IsPathFullyQualified(sessionDirectory)) throw new ArgumentException("会话目录必须为绝对路径。");
            var copy = Path.Combine(scratch, "session-copy"); Directory.CreateDirectory(copy);
            File.Copy(Path.Combine(sessionDirectory, "session.bin"), Path.Combine(copy, "session.bin"));
            if (File.Exists(Path.Combine(sessionDirectory, "device.id"))) File.Copy(Path.Combine(sessionDirectory, "device.id"), Path.Combine(copy, "device.id"));
            var context = await new ProtectedLoginSessionStore(copy, new CurrentUserSessionProtector()).LoadAsync(timeout.Token);
            var services = new ServiceCollection();
            services.AddSingleton<ILoginSessionStore>(new MemoryStore(context));
            services.AddMusicNetEasePluginServices(scratch);
            long frames = 0;
            var decoded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            AudioProgress? latest = null;
            services.AddSingleton<IAudioOutput>(provider => new LibVlcAudioOutput(provider.GetRequiredService<LibVlcRuntime>(), player =>
            {
                player.SetAudioFormat("S16N", 48000, 2);
                player.SetAudioCallbacks((_, _, count, _) =>
                { if (Interlocked.Add(ref frames, count) > 4800) decoded.TrySetResult(); }, null, null, null, null);
            }));
            await using var container = services.BuildServiceProvider();
            var login = container.GetRequiredService<LoginCoordinator>();
            await login.RestoreAsync(Guid.NewGuid(), timeout.Token);
            var accessor = container.GetRequiredService<IMusicSessionAccessor>();
            var session = accessor.Capture();
            var transport = container.GetRequiredService<NeteaseTransport>();
            async Task<JsonElement> Read(string label, string path, Dictionary<string, object?> data, NeteaseProtocol protocol)
            {
                stage = label;
                var result = await transport.SendAsync(path, data, protocol, accessor.Capture().Context, timeout.Token);
                var code = NeteaseTransport.Code(result.Body);
                results.Add(new { endpoint = label, protocol = protocol.ToString(), code, bytes = Encoding.UTF8.GetByteCount(result.Body.GetRawText()) });
                if (code != 200) throw NeteaseTransport.Error(200, code);
                return result.Body;
            }
            var playlists = await Read("user_playlist", "/api/user/playlist", new()
            { ["uid"] = login.Snapshot.Account!.Id, ["limit"] = 30, ["offset"] = 0, ["includeVideo"] = true }, NeteaseProtocol.Weapi);
            var list = playlists.GetProperty("playlist").EnumerateArray().ToArray();
            var candidate = list.First(p => p.GetProperty("trackCount").GetInt32() > 0);
            JsonElement detail = default;
            foreach (var n in new[] { 0, 1 })
            {
                detail = await Read("playlist_detail_n" + n, "/api/v6/playlist/detail", new()
                { ["id"] = candidate.GetProperty("id").GetInt64(), ["n"] = n, ["s"] = 0 }, NeteaseProtocol.Eapi);
                var playlist = detail.GetProperty("playlist");
                results.Add(new { endpoint = "detail_shape", n, trackCount = playlist.GetProperty("trackCount").GetInt32(),
                    ids = playlist.GetProperty("trackIds").GetArrayLength(), tracks = playlist.GetProperty("tracks").GetArrayLength() });
            }
            var ids = detail.GetProperty("playlist").GetProperty("trackIds").EnumerateArray().Take(3).Select(p => p.GetProperty("id").GetInt64()).ToArray();
            var tracks = await Read("batch_detail", "/api/v3/song/detail", new()
            { ["c"] = JsonSerializer.Serialize(ids.Select(id => new { id })) }, NeteaseProtocol.Eapi);
            var id = ids[0];
            await Read("lyric", "/api/song/lyric", new() { ["id"] = id, ["tv"] = -1, ["lv"] = -1, ["rv"] = -1, ["kv"] = -1, ["_nmclfl"] = 1 }, NeteaseProtocol.Eapi);
            var lyrics = await Read("lyric_new", "/api/song/lyric/v1", new()
            { ["id"] = id, ["cp"] = false, ["tv"] = 0, ["lv"] = 0, ["rv"] = 0, ["kv"] = 0, ["yv"] = 0, ["ytv"] = 0, ["yrv"] = 0 }, NeteaseProtocol.Eapi);
            results.Add(new { endpoint = "lyric_tracks", tracks = new[] { "lrc", "tlyric", "yrc", "ytlrc" }.Where(name => lyrics.TryGetProperty(name, out _)).ToArray() });
            // 只保存轨道结构与解析数量，不输出歌词、歌曲 ID 或用户歌单正文。
            foreach (var sampleId in ids)
            {
                var raw = await container.GetRequiredService<ILyricsApi>().GetAsync(sampleId, accessor.Capture(), timeout.Token);
                var parsed = LyricParser.Parse(raw);
                var words = parsed.Lines.SelectMany(line => line.Words ?? []).ToArray();
                results.Add(new { endpoint = "lyric_parse", hasLrc = !string.IsNullOrWhiteSpace(raw.Lrc), hasYrc = !string.IsNullOrWhiteSpace(raw.Yrc),
                    hasTranslation = !string.IsNullOrWhiteSpace(raw.Translation) || !string.IsNullOrWhiteSpace(raw.YTranslation),
                    lines = parsed.Lines.Count, wordSegments = words.Length, translatedLines = parsed.Lines.Count(line => line.Translation is not null),
                    wordTimesValid = words.All(word => word.StartMs >= 0 && word.DurationMs >= 0), degraded = parsed.Status.Contains("降级", StringComparison.Ordinal) });
            }
            stage = "native_seek";
            var resource = await container.GetRequiredService<IPlaybackResourceResolver>().ResolveAsync(id, session, timeout.Token);
            using var media = await container.GetRequiredService<IMediaBuffer>().DownloadAsync(resource, timeout.Token);
            var audio = container.GetRequiredService<IAudioOutput>();
            audio.Changed += (_, p) => Volatile.Write(ref latest, p);
            try
            {
                await audio.OpenAsync(media.Path, 1, timeout.Token, 5000);
                await decoded.Task.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token);
                await audio.PauseAsync(true, timeout.Token, 1);
                await audio.SeekAsync(1, 10000, timeout.Token);
                var position = Volatile.Read(ref latest)!;
                if (position.State != PlaybackState.Paused || Math.Abs(position.PositionMs - 10000) > 100)
                    throw new MusicException(MusicError.Decode, "真实媒体定位未达到目标。");
                results.Add(new { endpoint = "native_seek", resource.Format, position.PositionMs, position.CanSeek, frames });
            }
            finally { await audio.StopAsync(); }
            success = true;
            report = new { success, dateUtc = DateTimeOffset.UtcNow, results, hostSessionModified = false, actualDeviceOutput = false, realHost = false };
        }
        catch (Exception ex)
        {
            report = new { success, stage, results, error = ex is AuthException auth ? auth.Kind.ToString() : ex is MusicException music ? music.Kind.ToString() : ex.GetType().Name,
                hostSessionModified = false, actualDeviceOutput = false, realHost = false };
        }
        finally
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "netease-daily-probe")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(scratch).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("探针清理路径越界。");
            try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, json); Console.WriteLine(json);
        return success ? 0 : 1;
    }

    private sealed class MemoryStore(AuthContext context) : ILoginSessionStore
    {
        public Task<AuthContext> LoadAsync(CancellationToken ct) => Task.FromResult(context);
        public Task SaveAsync(AuthContext value, CancellationToken ct) { context = value; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken ct) { context = AuthContext.Create(); return Task.CompletedTask; }
    }
}
