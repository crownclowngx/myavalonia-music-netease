using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Settings;
using MusicNetEasePlugin.Infrastructure.Audio;
using MusicNetEasePlugin.Infrastructure.Persistence;
using Xunit;

namespace MusicNetEasePlugin.Tests;

internal sealed class MemoryVlcSettings : ILibVlcSettingsStore
{
    public LibVlcSettings Value { get; set; } = new();
    public int Loads { get; private set; }
    public Func<Task<LibVlcSettings>>? Loading { get; set; }
    public Task<LibVlcSettings> LoadAsync(CancellationToken ct) { Loads++; return Loading?.Invoke() ?? Task.FromResult(Value); }
    public Task SaveAsync(LibVlcSettings settings, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Value = settings; return Task.CompletedTask; }
}
internal sealed class DirectoryProbe(Func<string, RuntimeCheck> check) : ILibVlcDirectoryProbe { public RuntimeCheck Check(string directory) => check(directory); }
internal sealed class RuntimeStatus : IPlaybackRuntimeStatus
{
    public bool LoadAttempted { get; set; }
    public bool IsEngineActive { get; set; }
    public string? ActiveDirectory { get; set; }
    public string? ActiveVersion { get; set; } = "3.0.fixture";
    public RuntimeSource ActiveSource => RuntimeSource.Configured;
    public event EventHandler? Changed;
    public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    public Task<RuntimeSelection> PreviewAsync(CancellationToken ct) => Task.FromResult(new RuntimeSelection(RuntimeSource.BuiltIn, "builtin", new("builtin", []), null));
}

public sealed class LibVlcSettingsTests
{
    [Theory, InlineData(true, true, RuntimeSource.BuiltIn), InlineData(true, false, RuntimeSource.BuiltIn), InlineData(false, true, RuntimeSource.Configured), InlineData(false, false, RuntimeSource.None)]
    [Trait("M1", "T01,T02,E04,E06")]
    public async Task 内置优先且回退保留内置失败诊断(bool builtin, bool configured, RuntimeSource source)
    {
        var store = new MemoryVlcSettings { Value = new(CustomDirectory: "external") };
        var probe = new DirectoryProbe(p => new(p, (p == "builtin" ? builtin : configured) ? [] : [new("MissingFile", "缺少模块", p)]));
        var selection = await new LibVlcRuntimeResolver(store, probe, "builtin").ResolveAsync(default);
        Assert.Equal(source, selection.Source);
        Assert.Equal(builtin, selection.BuiltIn.IsValid);
        Assert.Equal("external", store.Value.CustomDirectory);
        Assert.Equal(builtin ? 0 : 1, store.Loads);
    }

    [Fact, Trait("M1", "T05,T09")]
    public async Task 保存是原子替换且清空不触碰外部库文件()
    {
        using var directory = new TestDirectory(); using var external = new TestDirectory();
        var marker = Path.Combine(external.Path, "libvlc.dll"); File.WriteAllText(marker, "external-owned");
        var store = new LibVlcSettingsStore(directory.Path);
        Assert.Null((await store.LoadAsync(default)).CustomDirectory);
        await store.SaveAsync(new(CustomDirectory: external.Path), default);
        var path = Path.Combine(directory.Path, "playback-settings.json");
        Assert.Contains("schemaVersion", await File.ReadAllTextAsync(path));
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<MusicException>(() => store.SaveAsync(new(CustomDirectory: "changed"), default));
        Assert.Equal(external.Path, (await store.LoadAsync(default)).CustomDirectory);
        Assert.Single(Directory.GetFiles(directory.Path));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(), canceled.Token));
        Assert.Equal(external.Path, (await store.LoadAsync(default)).CustomDirectory);
        await store.SaveAsync(new(), default);
        Assert.Null((await store.LoadAsync(default)).CustomDirectory); Assert.Equal("external-owned", File.ReadAllText(marker));
        File.WriteAllText(path, "{broken");
        Assert.Equal(MusicError.Storage, (await Assert.ThrowsAsync<MusicException>(() => store.LoadAsync(default))).Kind);
        await store.SaveAsync(new(CustomDirectory: external.Path), default);
        Assert.Equal(external.Path, (await store.LoadAsync(default)).CustomDirectory);
    }

    [Fact, Trait("M1", "T03,E04,E06")]
    public void 静态目录检查汇总缺文件错误架构与损坏且不加载原生库()
    {
        using var directory = new TestDirectory();
        var probe = new LibVlcDirectoryProbe();
        Assert.Contains(probe.Check("relative").Issues, i => i.Code == "Path");
        Assert.Contains(probe.Check(Path.Combine(directory.Path, "missing")).Issues, i => i.Code == "Missing");
        var bytes = File.ReadAllBytes(typeof(LibVlcSettingsTests).Assembly.Location);
        // 将真实 PE 夹具的 COFF 架构改为 x64；仅用于元数据探针，绝不加载这个伪原生文件。
        var peOffset = BitConverter.ToInt32(bytes, 0x3c);
        bytes[peOffset + 4] = 0x64; bytes[peOffset + 5] = 0x86;
        foreach (var relative in new[] { "libvlc.dll", "libvlccore.dll" }.Concat(LibVlcDirectoryProbe.RequiredModules))
        {
            var file = Path.Combine(directory.Path, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, bytes);
        }
        Assert.True(probe.Check(directory.Path).IsValid);
        bytes[peOffset + 4] = 0x4c; bytes[peOffset + 5] = 1;
        File.WriteAllBytes(Path.Combine(directory.Path, "libvlc.dll"), bytes);
        File.WriteAllText(Path.Combine(directory.Path, "libvlccore.dll"), "invalid");
        File.Delete(Path.Combine(directory.Path, LibVlcDirectoryProbe.RequiredModules[0]));
        var issues = probe.Check(directory.Path).Issues;
        Assert.Contains(issues, i => i.Code == "Architecture"); Assert.Contains(issues, i => i.Code == "InvalidFile"); Assert.Contains(issues, i => i.Code == "MissingFile");
    }

    [Fact, Trait("M1", "T04,T08,U07,U08")]
    public async Task 模型初始化只一次并保留载入时用户草稿及过期检测隔离()
    {
        var stored = new TaskCompletionSource<LibVlcSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemoryVlcSettings { Loading = () => stored.Task };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var probe = new DirectoryProbe(p => { entered.SetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(3))); return new(p, [new("Path", "旧检测失败", p)]); });
        await using var login = TestLogin.Create(new(), new(), TimeProvider.System, LoginOptions.Default);
        var runtime = new RuntimeStatus();
        using var tool = new MusicSettingsTool(login, store, probe, runtime, new ImmediateUi());
        var load = tool.EnsureLoadedAsync(); tool.DirectoryPath = "draft-B"; stored.SetResult(new(CustomDirectory: "saved-A")); await load;
        await tool.EnsureLoadedAsync();
        Assert.Equal(1, store.Loads); Assert.Equal("draft-B", tool.DirectoryPath); Assert.Equal("saved-A", tool.SavedDirectory);
        var checking = tool.CheckCommand.ExecuteAsync(null); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        tool.DirectoryPath = "draft-C"; release.Set(); await checking;
        Assert.DoesNotContain("旧检测失败", tool.Message); Assert.False(runtime.LoadAttempted); Assert.Null(login.Snapshot.Account);
    }

    [Fact, Trait("M1", "T06,T07,T09")]
    public async Task 已加载时保存只更新下次目录且退出不清除播放设置()
    {
        var loginStore = new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()) };
        await using var login = TestLogin.Create(new(), loginStore, TimeProvider.System, LoginOptions.Default);
        await login.RestoreAsync(Guid.NewGuid(), default);
        var store = new MemoryVlcSettings(); var runtime = new RuntimeStatus { LoadAttempted = true, ActiveDirectory = "active" };
        using var tool = new MusicSettingsTool(login, store, new DirectoryProbe(p => new(p, [])), runtime, new ImmediateUi());
        tool.DirectoryPath = "next"; await tool.SaveCommand.ExecuteAsync(null);
        Assert.Equal("next", store.Value.CustomDirectory); Assert.Contains("active", tool.ActiveRuntime); Assert.Contains("重启", tool.Message);
        Assert.Contains("测试账号", tool.AccountText);
        await tool.LogoutCommand.ExecuteAsync(null);
        Assert.Equal("尚未登录", tool.AccountText); Assert.Equal("next", store.Value.CustomDirectory);
        await tool.ClearCommand.ExecuteAsync(null); Assert.Null(store.Value.CustomDirectory); Assert.Contains("active", tool.ActiveRuntime);
    }
}
