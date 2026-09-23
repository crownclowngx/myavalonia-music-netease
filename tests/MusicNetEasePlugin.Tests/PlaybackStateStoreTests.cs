using System.Text.Json;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Persistence;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class PlaybackStateStoreTests
{
    [Fact, Trait("M2", "H02,H03")]
    public async Task 真实文件白名单保存按账号隔离且大队列省略资料不丢条目()
    {
        using var directory = new TestDirectory(); var store = new PlaybackStateStore(directory.Path);
        var entries = Enumerable.Range(1, 10000).Select(id => StoredEntry.From(QueueEntry.FromTrack(new(id, new string('歌', 1000), new string('人', 500), new string('专', 800), "https://secret/signed?token=hidden", 120000)))).ToArray();
        var state = PlaybackStateData.Empty(123) with { Entries = entries, CurrentEntryId = entries[678].EntryId, Mode = PlaybackMode.Shuffle, Volume = 29, PositionMs = 5000 };
        await store.SaveAsync(state, default);
        var file = Path.Combine(directory.Path, "playback-state", "123.json"); Assert.InRange(new FileInfo(file).Length, 1, PlaybackStateStore.MaximumBytes);
        var json = await File.ReadAllTextAsync(file); Assert.DoesNotContain("token", json); Assert.DoesNotContain("secret", json); Assert.DoesNotContain("cover", json); Assert.DoesNotContain("MUSIC_U", json);
        var read = (await store.LoadAsync(123, default)).Data!;
        Assert.Equal(10000, read.Entries.Count); Assert.Equal(entries.Select(entry => entry.EntryId), read.Entries.Select(entry => entry.EntryId));
        Assert.Equal(state.CurrentEntryId, read.CurrentEntryId); Assert.Equal(29, read.Volume); Assert.Equal(5000, read.PositionMs); Assert.All(read.Entries, entry => Assert.Null(entry.Name));
        Assert.Empty((await store.LoadAsync(456, default)).Data!.Entries);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.LoadAsync(-1, default));
    }
    [Theory, InlineData("json"), InlineData("schema"), InlineData("account"), InlineData("id"), InlineData("current"), InlineData("volume"), InlineData("position"), InlineData("mode"), InlineData("huge"), InlineData("null")]
    [Trait("M2", "H04")]
    public async Task 损坏未知版本身份及数值越界保留原文件(string kind)
    {
        using var directory = new TestDirectory(); var store = new PlaybackStateStore(directory.Path); var state = PlaybackPersistenceTests.State(123, 1, 500);
        state = kind switch
        {
            "schema" => state with { SchemaVersion = 99 }, "account" => state with { AccountId = 456 },
            "id" => state with { Entries = new[] { state.Entries[0] with { TrackId = 0 } } }, "current" => state with { CurrentEntryId = Guid.NewGuid() },
            "volume" => state with { Volume = 101 }, "position" => state with { PositionMs = -1 }, "mode" => state with { Mode = (PlaybackMode)99 }, _ => state
        };
        var path = Path.Combine(directory.Path, "playback-state", "123.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = kind == "json" ? "{broken" : kind == "null" ? "null" : kind == "huge" ? new string('x', PlaybackStateStore.MaximumBytes + 1) : JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await File.WriteAllTextAsync(path, text); var result = await store.LoadAsync(123, default);
        Assert.Null(result.Data); Assert.True(result.ProtectedFile); Assert.NotEmpty(result.Error); Assert.Equal(text, await File.ReadAllTextAsync(path));
    }
    [Fact, Trait("M2", "H05")]
    public async Task 保存取消及原子替换失败保持上次完整文件且清理临时文件()
    {
        using var directory = new TestDirectory(); var store = new PlaybackStateStore(directory.Path); var state = PlaybackPersistenceTests.State(123, 1, 500);
        await store.SaveAsync(state, default); var path = Path.Combine(directory.Path, "playback-state", "123.json"); var original = await File.ReadAllBytesAsync(path);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(state with { PositionMs = 800 }, cancelled.Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<MusicException>(() => store.SaveAsync(state with { PositionMs = 1200 }, default));
        Assert.Equal(original, await File.ReadAllBytesAsync(path)); Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        await store.SaveAsync(state with { PositionMs = 1600 }, default); Assert.Equal(1600, (await store.LoadAsync(123, default)).Data!.PositionMs);
    }
}
