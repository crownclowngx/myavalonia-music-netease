using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Audio;
using Xunit;

namespace MusicNetEasePlugin.Tests;

/// <summary>
/// 使用前后两段不同符号的自生成 PCM 验证真实适配，恢复起点不仅检查 Time 属性，
/// 还检查第一段实际输出内容，防止先播放开头再定位仍被误判成功。不使用声卡和网易账号。
/// </summary>
public sealed class NativeSeekTests
{
    [Fact, Trait("M2", "Q05,C02,A03")]
    public async Task 原生三曲自然结束连续交接且最后释放所有文件()
    {
        using var directory = new TestDirectory(); using var sessions = new MusicSessions(); var catalog = new MusicCatalog();
        using var runtime = new LibVlcRuntime(new(new MemoryVlcSettings(), new LibVlcDirectoryProbe(), Path.Combine(AppContext.BaseDirectory, "native", "win-x64", "libvlc")));
        long samples = 0;
        await using var audio = new LibVlcAudioOutput(runtime, player => { player.SetAudioFormat("S16N", 48000, 1); player.SetAudioCallbacks((_, _, count, _) => Interlocked.Add(ref samples, count), null, null, null, null); });
        var buffer = new MusicBuffer { Download = async (resource, ct) =>
        {
            var file = Path.Combine(directory.Path, resource.Id + ".wav"); await File.WriteAllBytesAsync(file, SegmentedWave(1), ct);
            return new(file, File.Delete);
        } };
        await using var single = new PlaybackCoordinator(sessions, catalog, catalog, buffer, audio);
        await using var queue = new PlaybackQueueCoordinator(sessions, single);
        var played = new ConcurrentQueue<long>(); var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Changed += (_, snapshot) => { if (snapshot.Playback.State == PlaybackState.Playing) played.Enqueue(snapshot.Playback.Track!.Id); if (snapshot.Playback.State == PlaybackState.Ended && snapshot.Playback.Track?.Id == 3) ended.TrySetResult(); };
        await queue.ReplaceAsync(Enumerable.Range(1, 3).Select(id => QueueEntry.FromTrack(MusicCatalog.Track(id))).ToArray(), 0, default);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(20)); await queue.DisposeAsync();
        Assert.Equal(new long[] { 1, 2, 3 }, played.Distinct()); Assert.True(samples > 48000); Assert.Empty(Directory.GetFiles(directory.Path));
        var artifacts = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS");
        if (!string.IsNullOrEmpty(artifacts)) await File.WriteAllTextAsync(Path.Combine(artifacts, "m2-native-queue.json"), JsonSerializer.Serialize(new { schemaVersion = 1, tracks = 3, decodedFrames = samples, filesReleased = true, actualDeviceOutput = false, realHost = false }));
    }
    [Fact, Trait("M2", "B02,B03,B08")]
    public async Task 原生定位保留暂停且续播第一帧来自指定区段()
    {
        using var directory = new TestDirectory();
        var builtIn = Path.Combine(AppContext.BaseDirectory, "native", "win-x64", "libvlc");
        using var runtime = new LibVlcRuntime(new(new MemoryVlcSettings(), new LibVlcDirectoryProbe(), builtIn));
        var firstSamples = new TaskCompletionSource<short[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new ConcurrentQueue<AudioProgress>();
        await using var audio = new LibVlcAudioOutput(runtime, player =>
        {
            player.SetAudioFormat("S16N", 48000, 1);
            player.SetAudioCallbacks((_, data, count, _) =>
            {
                if (firstSamples.Task.IsCompleted || count == 0) return;
                var values = new short[(int)count];
                Marshal.Copy(data, values, 0, values.Length);
                firstSamples.TrySetResult(values);
            }, null, null, null, null);
        });
        audio.Changed += (_, p) => progress.Enqueue(p);
        var sample = SegmentedWave();
        var path = Path.Combine(directory.Path, "segments.wav");
        await File.WriteAllBytesAsync(path, sample);
        await audio.OpenAsync(path, 41, default, 4000).WaitAsync(TimeSpan.FromSeconds(20));
        var first = await firstSamples.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(first.Count(v => v < -1000) > first.Length / 2, $"恢复后第一段 PCM 应来自后半段：min={first.Min()}, max={first.Max()}, mean={first.Average(v => (double)v)}, count={first.Length}。");
        await audio.PauseAsync(true, default, 41);
        await audio.SeekAsync(41, 1000, default);
        var backward = progress.Last();
        Assert.Equal(PlaybackState.Paused, backward.State);
        Assert.True(backward.CanSeek);
        Assert.InRange(backward.PositionMs, 950, 1050);
        await audio.SeekAsync(41, 4500, default);
        var forward = progress.Last();
        Assert.Equal(PlaybackState.Paused, forward.State);
        Assert.InRange(forward.PositionMs, 4450, 4550);
        await audio.SeekAsync(40, 0, default);
        Assert.Equal(forward, progress.Last());
        await audio.StopAsync();
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        var artifacts = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS");
        if (!string.IsNullOrEmpty(artifacts))
            await File.WriteAllTextAsync(Path.Combine(artifacts, "m2-native-seek.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, runtime.ActiveVersion, sampleSha256 = Convert.ToHexString(SHA256.HashData(sample)),
                startPositionMs = 4000, firstOutputMatchesStart = true, backward.PositionMs,
                forwardPositionMs = forward.PositionMs, pausedAfterSeek = true, fileReleased = true,
                actualDeviceOutput = false, realHost = false
            }));
    }

    private static byte[] SegmentedWave(int seconds = 8)
    {
        const int rate = 48000; var count = rate * seconds;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(count * 2);
        for (var i = 0; i < count; i++) writer.Write((short)(i < rate * 3 ? 4000 : -6000));
        return stream.ToArray();
    }
}
