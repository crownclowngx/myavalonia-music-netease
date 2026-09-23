using System.Diagnostics;
using LibVLCSharp.Shared;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Audio;

/// <summary>
/// 原生引擎由插件容器唯一拥有，首次播放才初始化。加载尝试后冻结目录，不能靠再次 Core.Initialize 热换库。
/// 调用者必须在音频串行执行路径内访问 GetEngineAsync/Dispose；状态属性供 Tool 只读展示。
/// </summary>
internal sealed class LibVlcRuntime(LibVlcRuntimeResolver resolver) : IPlaybackRuntimeStatus, IDisposable
{
    private LibVLC? _engine;
    private int _attempted;
    private bool _disposed;
    public bool LoadAttempted => Volatile.Read(ref _attempted) != 0;
    public string? ActiveDirectory { get; private set; }
    public string? ActiveVersion { get; private set; }
    public RuntimeSource ActiveSource { get; private set; }
    public event EventHandler? Changed;
    public Task<RuntimeSelection> PreviewAsync(CancellationToken ct) => resolver.ResolveAsync(ct);

    public async Task<LibVLC> GetEngineAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_engine is not null) return _engine;
        if (LoadAttempted) throw new MusicException(MusicError.Runtime, "原生初始化曾失败，请修正目录后重启 Host。");
        var choice = await resolver.ResolveAsync(ct).ConfigureAwait(false);
        if (!choice.IsReady) throw new MusicException(MusicError.Runtime, choice.Summary);
        ct.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _attempted, 1);
        try
        {
            // 先初始化明确目录，再创建引擎；禁止默认解析到 PATH。仅关闭本实例的视频输出，不影响其他插件。
            if (OperatingSystem.IsWindows()) LibVlcNativeBinding.Initialize(choice.Directory!);
            else Core.Initialize(choice.Directory);
            _engine = new LibVLC("--no-video", "--no-video-title-show", "--no-spu");
            // Windows 的真实模块来源必须与所选目录一致；只检查 libvlc 主库，不把系统音频依赖误判为私有文件。
            if (OperatingSystem.IsWindows())
            {
                var expected = Path.GetFullPath(Path.Combine(choice.Directory!, "libvlc.dll"));
                using var process = Process.GetCurrentProcess();
                var loaded = process.Modules.Cast<ProcessModule>().Where(m => string.Equals(m.ModuleName, "libvlc.dll", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (!loaded.Any(m => string.Equals(Path.GetFullPath(m.FileName), expected, StringComparison.OrdinalIgnoreCase)))
                    throw new MusicException(MusicError.Runtime, "加载来源与配置目录不一致，请统一运行库版本和路径后重启 Host。");
            }
            ActiveDirectory = choice.Directory;
            ActiveVersion = _engine.Version;
            ActiveSource = choice.Source;
            return _engine;
        }
        catch (Exception)
        {
            _engine?.Dispose();
            _engine = null;
            throw new MusicException(MusicError.Runtime, "LibVLC 初始化失败或存在版本冲突，请检查目录并重启 Host。");
        }
        finally
        {
            // 界面订阅者异常不能改变引擎是否成功加载的事实。
            if (Changed is { } handlers)
                foreach (EventHandler handler in handlers.GetInvocationList())
                    try { handler(this, EventArgs.Empty); } catch (Exception) { }
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine?.Dispose();
        _engine = null;
    }
}
