using System.Reflection;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Infrastructure.Audio;

/// <summary>
/// Windows 同一进程可能已加载其他插件的同名 DLL。仅 LoadLibrary(绝对路径) 不足以保证后续
/// DllImport("libvlc") 使用这份库，因此把本插件私有 LibVLCSharp 程序集的导入固定到选中的句柄。
/// 此处静态状态只描述不可热换的原生绑定，不保存引擎、账号或播放状态；每个容器仍独立拥有引擎。
/// 句柄与程序集绑定同寿命，不能在某个播放器关闭时 Free，否则其他容器的导入将悬空。
/// </summary>
internal static class LibVlcNativeBinding
{
    private static readonly object Sync = new();
    private static string? _directory;
    private static nint _vlc;
    private static nint _core;
    private static bool _ready;

    public static void Initialize(string directory)
    {
        lock (Sync)
        {
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (_directory is not null)
            {
                if (!_ready) throw new MusicException(MusicError.Runtime, "原生绑定初始化曾失败，请重启 Host。");
                if (!string.Equals(_directory, directory, StringComparison.OrdinalIgnoreCase))
                    throw new MusicException(MusicError.Runtime, "本音频程序集已经绑定其他目录，请重启 Host 后使用新设置。");
                return;
            }
            // 加载失败不退回默认目录；解析器只影响当前私有程序集，不修改 PATH 或全局 DLL 搜索目录。
            var core = NativeLibrary.Load(Path.Combine(directory, "libvlccore.dll"));
            nint vlc = 0;
            try
            {
                vlc = NativeLibrary.Load(Path.Combine(directory, "libvlc.dll"));
                _core = core; _vlc = vlc;
                NativeLibrary.SetDllImportResolver(typeof(LibVLC).Assembly, Resolve);
                _directory = directory;
                Core.Initialize(directory);
                _ready = true;
            }
            catch
            {
                // 解析器一旦登记就不能撤销，绑定目录必须冻结；只有登记前失败才可释放临时句柄。
                if (_directory is null) { if (vlc != 0) NativeLibrary.Free(vlc); NativeLibrary.Free(core); _vlc = _core = 0; }
                throw;
            }
        }
    }
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath) => name switch
    {
        "libvlc" or "libvlc.dll" => _vlc,
        "libvlccore" or "libvlccore.dll" => _core,
        _ => 0
    };
}
