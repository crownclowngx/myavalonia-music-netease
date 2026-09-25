using Microsoft.Extensions.DependencyInjection;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Main;
using MusicNetEasePlugin.Features.Music;
using MusicNetEasePlugin.Features.Settings;
using MusicNetEasePlugin.Infrastructure.Audio;
using MusicNetEasePlugin.Plugin;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace MusicNetEasePlugin.Tests;

[Collection("LibVlcNative")]
public sealed class NativePlaybackLifetimeTests
{
    [Fact]
    public async Task 生产Document在UI线程关闭释放真实引擎且保留设置并可反复重建()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            using var directory = new TestDirectory(); using var sessions = new MusicSessions();
            var file = Path.Combine(directory.Path, "tone.wav");
            await File.WriteAllBytesAsync(file, LongTone());
            var catalog = new MusicCatalog();
            var services = new ServiceCollection();
            services.AddSingleton<IMusicSessionAccessor>(sessions);
            services.AddSingleton<IMusicCatalogApi>(catalog);
            services.AddSingleton<IPlaybackResourceResolver>(catalog);
            services.AddSingleton<IMediaBuffer>(new MusicBuffer { Download = (_, _) => Task.FromResult(new BufferedMedia(file, _ => { })) });
            services.AddSingleton<ILoginSessionStore>(new MemorySessionStore());
            services.AddSingleton<INeteaseAuthApi>(new FakeAuthApi());
            services.AddSingleton<ILyricsApi>(new LyricsTests.LyricsFake());
            services.AddSingleton<IPlaybackStateStore>(new PlaybackPersistenceTests.MemoryStore());
            services.AddSingleton<ILoginUiDispatcher>(new ImmediateUi());
            services.AddMusicNetEasePluginServices(directory.Path);
            var pcm = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            long frames = 0;
            services.AddSingleton<IAudioOutput>(p => new LibVlcAudioOutput(p.GetRequiredService<LibVlcRuntime>(), player =>
            {
                player.SetAudioFormat("S16N", 48000, 1);
                player.SetAudioCallbacks((_, _, count, _) => { Interlocked.Add(ref frames, count); pcm.TrySetResult(); }, null, null, null, null);
            }));
            services.AddScoped<MainDocument>(); services.AddScoped<IDocumentLifetime, MusicLifetime>();
            services.AddSingleton<MusicSettingsTool>();
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            var settings = provider.GetRequiredService<MusicSettingsTool>();
            var runtime = provider.GetRequiredService<LibVlcRuntime>();
            Assert.False(runtime.LoadAttempted); // 设置面板不获得音乐租约。
            var queue = provider.GetRequiredService<PlaybackQueueCoordinator>();
            var persistence = provider.GetRequiredService<PlaybackPersistence>();
            var saved = await persistence.ActivateAsync(sessions.Capture(), default);
            Assert.NotNull(saved);
            await queue.RestoreAsync(sessions.Capture(), saved, queue.Snapshot.Revision);
            LibVLCSharp.Shared.LibVLC? previous = null;
            for (var cycle = 0; cycle < 4; cycle++)
            {
                pcm = new(TaskCreationOptions.RunContinuationsAsynchronously);
                using var scope = provider.CreateScope();
                var page = scope.ServiceProvider.GetRequiredService<MainDocument>();
                Assert.False(runtime.IsEngineActive);
                page.Music!.SelectedTrack = MusicCatalog.Track(1);
                await page.Music.PlayCommand.ExecuteAsync(null);
                await pcm.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(runtime.IsEngineActive);
                var engine = await runtime.GetEngineAsync(default);
                Assert.NotSame(previous, engine); previous = engine;
                var saving = persistence.FlushAsync(); // 保存尾任务尚未完成时，同步关闭也不能等待 UI continuation。
                var lifetime = (MusicLifetime)scope.ServiceProvider.GetRequiredService<IDocumentLifetime>();
                lifetime.Close(); scope.Dispose(); // 与 Host 相同：先 ClosingToken，再同步释放 Document Scope。
                await saving;
                Assert.False(runtime.IsEngineActive);
                Assert.Contains("已释放", settings.ActiveRuntime);
                Assert.Equal(PlaybackState.Stopped, provider.GetRequiredService<PlaybackQueueCoordinator>().Snapshot.Playback.State);
                using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                var stoppedFrames = Interlocked.Read(ref frames);
                await Task.Delay(100);
                Assert.Equal(stoppedFrames, Interlocked.Read(ref frames));
            }
            Assert.True(frames > 0);
            return true;
        }, default).WaitAsync(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public async Task 释放一个插件引擎不影响另一个引擎且重建继续使用已绑定目录()
    {
        using var directory = new TestDirectory();
        var file = Path.Combine(directory.Path, "tone.wav"); await File.WriteAllBytesAsync(file, LongTone());
        var builtIn = Path.Combine(AppContext.BaseDirectory, "native", "win-x64", "libvlc");
        var settings = new MemoryVlcSettings();
        using var first = new LibVlcRuntime(new(settings, new LibVlcDirectoryProbe(), builtIn));
        using var second = new LibVlcRuntime(new(new MemoryVlcSettings(), new LibVlcDirectoryProbe(), builtIn));
        var continuing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observeSecond = 0;
        await using var audioA = new LibVlcAudioOutput(first, p =>
        { p.SetAudioFormat("S16N", 48000, 1); p.SetAudioCallbacks((_, _, _, _) => { }, null, null, null, null); });
        await using var audioB = new LibVlcAudioOutput(second, p =>
        {
            p.SetAudioFormat("S16N", 48000, 1);
            p.SetAudioCallbacks((_, _, _, _) => { if (Volatile.Read(ref observeSecond) != 0) continuing.TrySetResult(); }, null, null, null, null);
        });
        await audioA.OpenAsync(file, 1, default); await audioB.OpenAsync(file, 1, default);
        var old = await first.GetEngineAsync(default);
        var other = await second.GetEngineAsync(default);
        await audioA.ReleaseSessionAsync();
        Assert.False(first.IsEngineActive); Assert.True(second.IsEngineActive);
        Volatile.Write(ref observeSecond, 1); await continuing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        settings.Value = new(CustomDirectory: "changed-after-first-load");
        await audioA.OpenAsync(file, 2, default);
        Assert.NotSame(old, await first.GetEngineAsync(default));
        Assert.Same(other, await second.GetEngineAsync(default));
        Assert.Equal(Path.GetFullPath(builtIn), first.ActiveDirectory);
        await audioA.ReleaseSessionAsync(); await audioB.ReleaseSessionAsync();
    }

    private static byte[] LongTone()
    {
        const int rate = 48000, samples = rate * 8;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)(4000 * Math.Sin(2 * Math.PI * 440 * i / rate)));
        return stream.ToArray();
    }
}
