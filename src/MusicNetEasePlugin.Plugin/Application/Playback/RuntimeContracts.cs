namespace MusicNetEasePlugin.Application.Playback;

/// <summary>运行库设置仅保存本机目录；不包含账号凭据，也不决定已加载原生库的寿命。</summary>
public sealed record LibVlcSettings(int SchemaVersion = 1, string? CustomDirectory = null);

public interface ILibVlcSettingsStore
{
    Task<LibVlcSettings> LoadAsync(CancellationToken ct);
    Task SaveAsync(LibVlcSettings settings, CancellationToken ct);
}

public sealed record RuntimeIssue(string Code, string Message, string Path);
public sealed record RuntimeCheck(string Directory, IReadOnlyList<RuntimeIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
    public string Summary => IsValid ? "目录检查通过，首次播放时加载。" : string.Join("；", Issues.Select(i => i.Message));
}
public enum RuntimeSource { None, BuiltIn, Configured }
public sealed record RuntimeSelection(RuntimeSource Source, string? Directory, RuntimeCheck BuiltIn, RuntimeCheck? Configured)
{
    public bool IsReady => Source != RuntimeSource.None;
    public string Summary => Source switch
    {
        RuntimeSource.BuiltIn => "使用内置 LibVLC；配置目录不会覆盖有效内置库。",
        RuntimeSource.Configured => "使用配置目录。" + (BuiltIn.Issues.Any(i => i.Code != "Missing") ? " 内置库检查未通过。" : ""),
        _ => "播放库不可用，请在账号与播放设置中配置 LibVLC 目录。"
    };
}

/// <summary>只读探针不加载 DLL，不会锁住用户选择的目录；便于测试文件与架构失败。</summary>
public interface ILibVlcDirectoryProbe { RuntimeCheck Check(string directory); }

/// <summary>向界面提供运行库事实；不暴露 LibVLC 对象，设置与播放器通过快照协作。</summary>
public interface IPlaybackRuntimeStatus
{
    event EventHandler? Changed;
    bool LoadAttempted { get; }
    string? ActiveDirectory { get; }
    string? ActiveVersion { get; }
    RuntimeSource ActiveSource { get; }
    Task<RuntimeSelection> PreviewAsync(CancellationToken ct);
}

public enum MusicError { Network, Timeout, Protocol, SignedOut, Restricted, Runtime, Decode, Storage, Device, AddressExpired }
/// <summary>面向用例的稳定失败；禁止附带含 Cookie/签名地址的底层异常。</summary>
public sealed class MusicException(MusicError kind, string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public MusicError Kind { get; } = kind;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
