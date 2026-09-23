using LibVLCSharp.Shared;
using System.Globalization;
using VlcMedia = LibVLCSharp.Shared.Media;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Audio;

/// <summary>
/// 所有原生控制在后台单一异步门中执行，UI 不同步等待 Stop。一个容器复用引擎，
/// 每首歌独占 MediaPlayer/Media，旧播放器事件携带旧代次，不能冒充新歌曲事件。
/// </summary>
internal sealed class LibVlcAudioOutput(LibVlcRuntime runtime, Action<MediaPlayer>? configureOutput = null) : IAudioOutput, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private MediaPlayer? _player;
    private VlcMedia? _media;
    private int _volume = 70;
    private long _generation;
    private Action? _reportPosition;
    private bool _disposed;
    public event EventHandler<AudioProgress>? Changed;

    public Task OpenAsync(string path, long generation, CancellationToken ct, long startPositionMs = 0) => RunAsync(async () =>
    {
        if (startPositionMs < 0) throw new ArgumentOutOfRangeException(nameof(startPositionMs));
        StopCore();
        var engine = await runtime.GetEngineAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var media = new VlcMedia(engine, path, FromType.FromPath);
        // 在输入创建前指定起点。恢复只在用户点击继续时进入这里，不采用 Play 后 Pause/seek 的有声竞态。
        if (startPositionMs > 0)
        {
            media.AddOption(":start-paused");
            media.AddOption(":start-time=" + (startPositionMs / 1000d).ToString("0.000", CultureInfo.InvariantCulture));
        }
        _media = media;
        var player = new MediaPlayer(engine);
        _player = player;
        _generation = generation;
        var state = (int)PlaybackState.Loading;
        _reportedState = () => (PlaybackState)Volatile.Read(ref state);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long lastProgress = 0;
        long position = 0, duration = 0;
        var seekable = 0;
        void Notify(PlaybackState next, string? error = null)
        {
            Interlocked.Exchange(ref state, (int)next);
            if (!ReferenceEquals(_player, player) || _disposed) return;
            // 原生事件可能持有输入锁；这里仅读事件缓存，不能重入 Time/Length/Stop 等原生 API。
            try { Changed?.Invoke(this, new(generation, next, Math.Max(0, Interlocked.Read(ref position)), Math.Max(0, Interlocked.Read(ref duration)), error, Volatile.Read(ref seekable) != 0)); }
            catch (Exception) { /* 订阅者只接收事实，异常不能穿越原生回调边界。 */ }
        }
        player.Playing += (_, _) => { Notify(PlaybackState.Playing); if (startPositionMs == 0) started.TrySetResult(); };
        player.Paused += (_, _) => { Notify(PlaybackState.Paused); if (startPositionMs > 0) started.TrySetResult(); };
        player.EndReached += (_, _) => Notify(PlaybackState.Ended);
        player.EncounteredError += (_, _) =>
        {
            Notify(PlaybackState.Failed, "解码或音频输出失败，请检查运行库和设备。");
            started.TrySetException(new MusicException(MusicError.Decode, "音频解码或输出失败。"));
        };
        player.LengthChanged += (_, args) => Interlocked.Exchange(ref duration, args.Length);
        player.SeekableChanged += (_, args) => Volatile.Write(ref seekable, args.Seekable != 0 ? 1 : 0);
        // 此委托仅由控制门调用，绝不在原生事件线程读取 Time/Length；用于暂停时定位的事实回报。
        _reportPosition = () =>
        {
            Interlocked.Exchange(ref position, player.Time);
            Interlocked.Exchange(ref duration, player.Length);
            Volatile.Write(ref seekable, player.IsSeekable ? 1 : 0);
            Notify((PlaybackState)Volatile.Read(ref state));
        };
        player.TimeChanged += (_, args) =>
        {
            Interlocked.Exchange(ref position, args.Time);
            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref lastProgress) < 200) return;
            Interlocked.Exchange(ref lastProgress, now);
            Notify((PlaybackState)Volatile.Read(ref state));
        };
        player.Volume = _volume;
        // 生产默认使用系统音频输出；离线探针可接收真实 PCM，验证解码链路且无需声卡。
        configureOutput?.Invoke(player);
        if (!player.Play(media)) throw new MusicException(MusicError.Decode, "音频未能启动，请检查格式和输出设备。");
        try { await started.Task.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false); }
        catch (TimeoutException) { throw new MusicException(MusicError.Timeout, "音频启动超时，请检查运行库和设备。"); }
        if (startPositionMs > 0)
        {
            await SetPositionAsync(player, startPositionMs, ct).ConfigureAwait(false);
            player.SetPause(false);
        }
    }, ct);
    private Func<PlaybackState>? _reportedState;
    public Task PauseAsync(bool paused, CancellationToken ct, long? generation = null) => RunAsync(async () =>
    {
        if (_player is null || generation.HasValue && generation.Value != _generation) return;
        _player?.SetPause(paused); // 使用显式 SetPause，重复暂停不会像 toggle 一样反向播放。
        // SetPause 是异步命令。等待事件缓存确认后才交给下一次 seek，不能把 setter 返回误当成已暂停。
        var expected = paused ? PlaybackState.Paused : PlaybackState.Playing;
        var deadline = Environment.TickCount64 + 2000;
        while (_reportedState?.Invoke() != expected)
        {
            if (Environment.TickCount64 >= deadline) throw new MusicException(MusicError.Timeout, "播放状态切换未完成，请重试。");
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
    }, ct);
    public Task SeekAsync(long generation, long positionMs, CancellationToken ct) => RunAsync(async () =>
    {
        if (_player is null || generation != _generation) return;
        try { await SetPositionAsync(_player, positionMs, ct).ConfigureAwait(false); }
        finally { _reportPosition?.Invoke(); } // 定位超时也报告真实位置，界面不能保留未经确认的用户目标。
    }, ct);
    private static async Task SetPositionAsync(MediaPlayer player, long target, CancellationToken ct)
    {
        if (!player.IsSeekable || player.Length <= 0)
            throw new MusicException(MusicError.Decode, "当前媒体尚不可定位。");
        target = Math.Clamp(target, 0, Math.Max(0, player.Length - 1));
        player.Time = target;
        // LibVLC 3 的定位命令异步提交；暂停状态不保证继续发送 TimeChanged，因此在控制门内有界读取事实。
        // 不在回调中查询原生属性，也不把 setter 返回当作定位已经成功。
        var deadline = Environment.TickCount64 + 2000;
        while (Math.Abs(player.Time - target) > 100)
        {
            if (Environment.TickCount64 >= deadline) throw new MusicException(MusicError.Timeout, "媒体定位未完成，请重试。");
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
    }
    public Task SetVolumeAsync(int volume, CancellationToken ct)
    {
        if (volume is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(volume));
        return RunAsync(() => { _volume = volume; if (_player is not null) _player.Volume = volume; return Task.CompletedTask; }, ct);
    }
    public Task StopAsync() => RunAsync(() => { StopCore(); return Task.CompletedTask; }, CancellationToken.None);
    private async Task RunAsync(Func<Task> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await Task.Run(action, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (MusicException) { throw; }
        catch (Exception) { throw new MusicException(MusicError.Device, "音频操作失败，请检查设备和 LibVLC 运行库。"); }
        finally { _gate.Release(); }
    }
    private void StopCore()
    {
        // 先使回调失效，再 Stop/解绑；最后释放 Media，确保临时文件不再被原生代码访问。
        var player = _player;
        _player = null;
        _reportPosition = null;
        _reportedState = null;
        var media = _media;
        _media = null;
        try
        {
            if (player is not null)
            {
                try { player.Stop(); player.Media = null; }
                finally { player.Dispose(); }
            }
        }
        finally { media?.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) { await Task.Run(StopCore).ConfigureAwait(false); _disposed = true; Changed = null; } }
        finally { _gate.Release(); }
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
