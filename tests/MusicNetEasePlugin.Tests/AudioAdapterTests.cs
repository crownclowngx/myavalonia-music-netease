using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Audio;
using Xunit;

namespace MusicNetEasePlugin.Tests;

/// <summary>测试运行库固定为构建产物，真实生产适配解码到 PCM；不访问账号、网络或系统输出设备。</summary>
public sealed class AudioAdapterTests
{
    [Fact, Trait("M1", "E01,E02,E03,E07")]
    public async Task 生产适配复用引擎真实解码并释放每次媒体句柄()
    {
        using var directory = new TestDirectory();
        var builtIn = Path.Combine(AppContext.BaseDirectory, "native", "win-x64", "libvlc");
        var probe = new LibVlcDirectoryProbe();
        Assert.True(probe.Check(builtIn).IsValid, probe.Check(builtIn).Summary);
        var settings = new MemoryVlcSettings();
        using var runtime = new LibVlcRuntime(new(settings, probe, builtIn));
        long frames = 0, nonzero = 0;
        await using var audio = new LibVlcAudioOutput(runtime, player =>
        {
            player.SetAudioFormat("S16N", 48000, 1);
            player.SetAudioCallbacks((_, samples, count, _) =>
            {
                // 回调内只复制/计数；不 Stop，不执行断言，避免异常越过原生边界。
                var values = new short[(int)count]; Marshal.Copy(samples, values, 0, values.Length);
                Interlocked.Add(ref frames, count); Interlocked.Add(ref nonzero, values.Count(v => v != 0));
            }, null, null, null, null);
        });
        var measurements = new List<object>();
        var sample = ToneWave();
        for (var cycle = 0; cycle < 12; cycle++)
        {
            var path = Path.Combine(directory.Path, cycle + ".wav"); await File.WriteAllBytesAsync(path, sample);
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var generation = cycle + 1;
            void Changed(object? sender, AudioProgress p)
            {
                if (p.Generation != generation) return;
                if (p.State == PlaybackState.Ended) ended.TrySetResult();
                if (p.State == PlaybackState.Failed) ended.TrySetException(new InvalidOperationException(p.Error));
            }
            audio.Changed += Changed;
            var before = Interlocked.Read(ref frames);
            await audio.OpenAsync(path, generation, default).WaitAsync(TimeSpan.FromSeconds(20));
            await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await audio.StopAsync(); audio.Changed -= Changed;
            var decoded = Interlocked.Read(ref frames) - before;
            Assert.InRange(decoded, 19000, 19400); // 48000 Hz × 0.4 秒，允许末帧对齐差异。
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.Delete(path); Assert.False(File.Exists(path));
            using var process = Process.GetCurrentProcess();
            measurements.Add(new { cycle, decodedFrames = decoded, handles = process.HandleCount, threads = process.Threads.Count });
        }
        Assert.True(nonzero > 0); Assert.True(runtime.LoadAttempted);
        Assert.Equal(Path.GetFullPath(builtIn), runtime.ActiveDirectory);
        Assert.Equal(RuntimeSource.BuiltIn, runtime.ActiveSource);
        // 保存新目录只改变候选设置，原生引擎仍然复用首次实例。
        var first = await runtime.GetEngineAsync(default); settings.Value = new(CustomDirectory: "invalid-new-path");
        Assert.Same(first, await runtime.GetEngineAsync(default));
        var invalid = Path.Combine(directory.Path, "invalid.mp3"); await File.WriteAllBytesAsync(invalid, []);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        audio.Changed += (_, p) => { if (p.Generation == 100 && p.State == PlaybackState.Failed) failed.TrySetResult(); };
        var error = await Record.ExceptionAsync(() => audio.OpenAsync(invalid, 100, default).WaitAsync(TimeSpan.FromSeconds(20)));
        if (error is null) await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        else Assert.IsType<MusicException>(error);
        await audio.StopAsync(); File.Delete(invalid);
        var artifactRoot = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS");
        if (!string.IsNullOrEmpty(artifactRoot))
            await File.WriteAllTextAsync(Path.Combine(artifactRoot, "libvlc-offline.json"), JsonSerializer.Serialize(new
            {
                sample = new { kind = "generated-440Hz-PCM16-WAV", sampleRate = 48000, channels = 1, seconds = .4, sha256 = Convert.ToHexString(SHA256.HashData(sample)) },
                runtime.ActiveDirectory, runtime.ActiveVersion, measurements, actualDeviceOutput = false
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static byte[] ToneWave()
    {
        const int rate = 48000, count = 19200;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(count * 2);
        for (var i = 0; i < count; i++) writer.Write((short)(8000 * Math.Sin(2 * Math.PI * 440 * i / rate)));
        return stream.ToArray();
    }
}
