using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Audio;

/// <summary>
/// 第一阶段支持 Windows x64 的目录加载；跨平台业务契约保持不变，其他平台在实际验证前明确不可用。
/// 检查文件元数据而不 LoadLibrary，避免“检测目录”污染当前进程的原生加载状态。
/// </summary>
internal sealed class LibVlcDirectoryProbe : ILibVlcDirectoryProbe
{
    internal static readonly string[] RequiredModules =
    [
        "plugins/codec/libavcodec_plugin.dll", "plugins/demux/libes_plugin.dll",
        "plugins/demux/libflacsys_plugin.dll", "plugins/demux/libwav_plugin.dll",
        "plugins/audio_output/libmmdevice_plugin.dll"
    ];
    public RuntimeCheck Check(string directory)
    {
        var issues = new List<RuntimeIssue>();
        void Add(string code, string text, string path) => issues.Add(new(code, text, path));
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            Add("Platform", "当前版本尚未验证此平台的 LibVLC 指定目录加载。", directory);
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
        {
            Add("Path", "请选择本机绝对目录，而不是相对路径或单个 DLL。", directory);
            return new(directory, issues);
        }
        try
        {
            directory = Path.GetFullPath(directory);
            if (!Directory.Exists(directory))
            {
                Add("Missing", "LibVLC 目录不存在。", directory);
                return new(directory, issues);
            }
            foreach (var relative in new[] { "libvlc.dll", "libvlccore.dll" }.Concat(RequiredModules))
            {
                var path = Path.Combine(directory, relative);
                if (!File.Exists(path)) { Add("MissingFile", "缺少运行库文件：" + relative, path); continue; }
                try
                {
                    using var file = File.OpenRead(path);
                    using var pe = new PEReader(file);
                    if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64)
                        Add("Architecture", "文件架构与 Windows x64 不匹配：" + relative, path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
                { Add("InvalidFile", "无法读取有效原生文件：" + relative, path); }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        { Add("Path", "目录无效或无法读取。", directory); }
        return new(directory, issues);
    }
}

/// <summary>固定内置优先规则，无策略注册表；指定路径可以复用视频插件文件，但不依赖其对象或容器。</summary>
internal sealed class LibVlcRuntimeResolver(ILibVlcSettingsStore store, ILibVlcDirectoryProbe probe, string builtInDirectory)
{
    public async Task<RuntimeSelection> ResolveAsync(CancellationToken ct)
    {
        // 路径设置损坏不能阻断有效内置库；Tool 独立报告配置读取错误，播放按固定优先级工作。
        ct.ThrowIfCancellationRequested();
        var builtIn = probe.Check(builtInDirectory);
        if (builtIn.IsValid) return new(RuntimeSource.BuiltIn, builtIn.Directory, builtIn, null);
        var settings = await store.LoadAsync(ct).ConfigureAwait(false);
        return Resolve(settings);
    }
    public RuntimeSelection Resolve(LibVlcSettings settings)
    {
        var builtIn = probe.Check(builtInDirectory);
        if (builtIn.IsValid) return new(RuntimeSource.BuiltIn, builtIn.Directory, builtIn, null);
        var configured = string.IsNullOrWhiteSpace(settings.CustomDirectory) ? null : probe.Check(settings.CustomDirectory);
        return configured?.IsValid == true
            ? new(RuntimeSource.Configured, configured.Directory, builtIn, configured)
            : new(RuntimeSource.None, null, builtIn, configured);
    }
}
