using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Audio;
using MusicNetEasePlugin.Infrastructure.Persistence;
using MusicNetEasePlugin.Plugin;

/// <summary>
/// 显式手动探针：复用生产服务读取既有受保护会话，刷新仅留在探针内存，绝不覆盖正在运行的 Host 会话。
/// 真正下载并由生产 LibVLC 适配解码到 PCM；默认无声卡输出，不把结果描述为“已经听到声音”。
/// 报告只保留状态和格式，禁止 Cookie、账号详情、签名地址或远端正文。
/// </summary>
internal static class MusicProbe
{
    public static async Task<int> RunAsync(string sessionDirectory, string keyword, string output, string? runtimeDirectory)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "netease-music-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var stage = "restore";
        object report;
        var success = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        try
        {
            if (!Path.IsPathFullyQualified(sessionDirectory) || !File.Exists(Path.Combine(sessionDirectory, "session.bin")))
                throw new MusicException(MusicError.SignedOut, "指定目录没有已保存的会话。");
            var copy = Path.Combine(scratch, "session-copy"); Directory.CreateDirectory(copy);
            File.Copy(Path.Combine(sessionDirectory, "session.bin"), Path.Combine(copy, "session.bin"));
            if (File.Exists(Path.Combine(sessionDirectory, "device.id"))) File.Copy(Path.Combine(sessionDirectory, "device.id"), Path.Combine(copy, "device.id"));
            var stored = await new ProtectedLoginSessionStore(copy, new CurrentUserSessionProtector()).LoadAsync(timeout.Token);
            var memory = new ProbeSessionStore(stored);
            var decoded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            long frames = 0;
            var services = new ServiceCollection();
            services.AddSingleton<ILoginSessionStore>(memory);
            services.AddMusicNetEasePluginServices(scratch);
            if (runtimeDirectory is not null)
                services.AddSingleton(new LibVlcRuntimeResolver(new ProbeSettings(runtimeDirectory), new LibVlcDirectoryProbe(), Path.Combine(scratch, "absent-builtin")));
            services.AddSingleton<IAudioOutput>(provider => new LibVlcAudioOutput(provider.GetRequiredService<LibVlcRuntime>(), player =>
            {
                player.SetAudioFormat("S16N", 48000, 2);
                player.SetAudioCallbacks((_, _, count, _) =>
                { if (Interlocked.Add(ref frames, count) >= 48000) decoded.TrySetResult(); }, null, null, null, null);
            }));
            await using var container = services.BuildServiceProvider();
            var login = container.GetRequiredService<LoginCoordinator>();
            await login.RestoreAsync(Guid.NewGuid(), timeout.Token);
            var session = container.GetRequiredService<IMusicSessionAccessor>().Capture();
            stage = "search";
            var catalog = container.GetRequiredService<IMusicCatalogApi>();
            var page = await catalog.SearchAsync(keyword, 0, session, timeout.Token);
            if (page.Tracks.Count == 0) throw new MusicException(MusicError.Protocol, "没有匹配歌曲。");
            var track = page.Tracks[0];
            stage = "detail";
            var detail = await catalog.DetailAsync(track.Id, session, timeout.Token);
            stage = "resolve";
            var resource = await container.GetRequiredService<IPlaybackResourceResolver>().ResolveAsync(track.Id, session, timeout.Token);
            if (resource.Address is null) throw new MusicException(MusicError.Restricted, "首条结果没有可播地址，请换关键词。");
            stage = "playback";
            var playback = container.GetRequiredService<PlaybackCoordinator>();
            await playback.PlayAsync(Guid.NewGuid(), track, timeout.Token);
            if (playback.Snapshot.State == PlaybackState.Failed) throw new MusicException(MusicError.Decode, playback.Snapshot.Message);
            await decoded.Task.WaitAsync(TimeSpan.FromSeconds(20), timeout.Token);
            await playback.PauseAsync(true, timeout.Token);
            await playback.SetVolumeAsync(25, timeout.Token);
            await playback.PauseAsync(false, timeout.Token);
            await playback.StopAsync();
            var runtime = container.GetRequiredService<IPlaybackRuntimeStatus>();
            report = new { success = true, dateUtc = DateTimeOffset.UtcNow, searchCount = page.Tracks.Count,
                identityMatched = detail.Id == track.Id, resource.Format, resource.Quality, resource.IsTrial,
                decodedFrames = Interlocked.Read(ref frames), stopped = playback.Snapshot.State == PlaybackState.Stopped,
                runtime.ActiveDirectory, runtime.ActiveVersion, source = runtime.ActiveSource.ToString(),
                historicalSessionLoaded = true, hostSessionModified = false, actualDeviceOutput = false, hostVerified = false };
            success = true;
        }
        catch (MusicException ex) { report = new { success = false, stage, error = ex.Kind.ToString(), ex.Message, actualDeviceOutput = false }; }
        catch (AuthException ex) { report = new { success = false, stage, error = ex.Kind.ToString(), ex.HttpStatus, ex.BusinessCode, actualDeviceOutput = false }; }
        catch (OperationCanceledException) { report = new { success = false, stage, error = "CancelledOrTimedOut", actualDeviceOutput = false }; }
        catch (Exception) { report = new { success = false, stage, error = "ProbeFailed", actualDeviceOutput = false }; }
        finally
        {
            // 所有播放器/流先由容器释放；只清理本次生成且位于固定父目录下的 GUID 子目录。
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "netease-music-probe")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(scratch).StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, json);
        Console.WriteLine(json);
        return success ? 0 : 1;
    }
    private sealed class ProbeSessionStore(AuthContext context) : ILoginSessionStore
    {
        public Task<AuthContext> LoadAsync(CancellationToken ct) => Task.FromResult(context);
        public Task SaveAsync(AuthContext value, CancellationToken ct) { context = value; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken ct) { context = AuthContext.Create(); return Task.CompletedTask; }
    }
    private sealed class ProbeSettings(string path) : ILibVlcSettingsStore
    {
        public Task<LibVlcSettings> LoadAsync(CancellationToken ct) => Task.FromResult(new LibVlcSettings(CustomDirectory: path));
        public Task SaveAsync(LibVlcSettings settings, CancellationToken ct) => throw new NotSupportedException();
    }
}
