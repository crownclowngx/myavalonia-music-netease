using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Infrastructure.Persistence;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class UiPreferencesTests
{
    [Fact]
    public async Task 偏好独立原子保存且取消不覆盖旧文件()
    {
        using var directory = new TestDirectory();
        var store = new UiPreferencesStore(directory.Path);
        Assert.False((await store.LoadAsync(default)).ReduceMotion);
        await store.SaveAsync(new(true), default);
        Assert.True((await new UiPreferencesStore(directory.Path).LoadAsync(default)).ReduceMotion);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(false), cancelled.Token));
        Assert.True((await store.LoadAsync(default)).ReduceMotion);
        Assert.Equal("ui-preferences.json", Path.GetFileName(Assert.Single(Directory.GetFiles(directory.Path))));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"reduceMotion\":true}")]
    [InlineData("broken")]
    public async Task 损坏或未知版本保留原文件并给出读取反馈(string contents)
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "ui-preferences.json");
        await File.WriteAllTextAsync(path, contents);
        using var preferences = new UiPreferences(new UiPreferencesStore(directory.Path), new ImmediateUi());
        await preferences.EnsureLoadedAsync();
        Assert.Contains("读取失败", preferences.Message);
        Assert.False(preferences.ReduceMotion);
        Assert.Equal(contents, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task 迟到读取不能覆盖用户选择且读取只执行一次()
    {
        var read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemoryUiPreferences { Read = () => read.Task };
        using var preferences = new UiPreferences(store, new ImmediateUi());
        var loading = preferences.EnsureLoadedAsync();
        Assert.Same(loading, preferences.EnsureLoadedAsync());
        preferences.ReduceMotion = true;
        await preferences.PendingSave;
        read.SetResult(false); await loading;
        Assert.True(preferences.ReduceMotion); Assert.True(store.Value); Assert.Equal(1, store.Reads);
    }

    [Fact]
    public async Task 快速修改串行保存且失败不撤销本次减少动效()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemoryUiPreferences { Write = async (_, _) => { entered.TrySetResult(); await release.Task; } };
        using var preferences = new UiPreferences(store, new ImmediateUi());
        preferences.ReduceMotion = true;
        await entered.Task;
        preferences.ReduceMotion = false;
        preferences.ReduceMotion = true;
        release.SetResult(); await preferences.PendingSave;
        Assert.True(store.Value); Assert.Equal(1, store.MaxConcurrentWrites);
        store.Write = (_, _) => throw new IOException("模拟磁盘失败");
        preferences.ReduceMotion = false; await preferences.PendingSave;
        Assert.False(preferences.ReduceMotion); Assert.True(store.Value); Assert.Contains("保存失败", preferences.Message);
        store.Write = null;
        preferences.RetrySaveCommand.Execute(null); await preferences.PendingSave;
        Assert.False(store.Value); Assert.Empty(preferences.Message);
    }
}

internal sealed class MemoryUiPreferences : IUiPreferencesStore
{
    public bool Value { get; private set; }
    public int Reads { get; private set; }
    public int MaxConcurrentWrites { get; private set; }
    private int _writing;
    public Func<Task<bool>>? Read { get; init; }
    public Func<bool, CancellationToken, Task>? Write { get; set; }
    public async Task<UiPreferencesData> LoadAsync(CancellationToken cancellationToken)
    { Reads++; return new(Read is null ? Value : await Read()); }
    public async Task SaveAsync(UiPreferencesData preferences, CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _writing);
        MaxConcurrentWrites = Math.Max(MaxConcurrentWrites, active);
        try { if (Write is not null) await Write(preferences.ReduceMotion, cancellationToken); Value = preferences.ReduceMotion; }
        finally { Interlocked.Decrement(ref _writing); }
    }
}
