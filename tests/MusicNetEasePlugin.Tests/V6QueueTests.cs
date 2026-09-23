using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Music;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class V6QueueTests
{
    private static QueueEntry[] Entries(int count) => Enumerable.Range(1, count).Select(_ => QueueEntry.FromTrack(MusicCatalog.Track(1))).ToArray();
    [Fact, Trait("V6", "Q01,Q02")]
    public async Task 重复歌曲按条目排序撤销保留当前媒体进度且旧版本不能提交()
    {
        await using var f = new PlaybackFixture(); var entries = Entries(4); await f.Queue.ReplaceAsync(entries, 0, default);
        f.Audio.Emit(f.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 12345);
        var before = f.Queue.Snapshot;
        Assert.True(f.Queue.MoveTo(new(entries[0].EntryId, 3, before.QueueRevision, before.AccountEpoch)));
        var moved = f.Queue.Snapshot; Assert.Equal(entries[0].EntryId, moved.Entries[3].EntryId);
        Assert.Equal(before.Playback, moved.Playback); Assert.Equal(before.CurrentEntryId, moved.CurrentEntryId);
        Assert.False(f.Queue.MoveTo(new(entries[1].EntryId, 0, before.QueueRevision, before.AccountEpoch)));
        Assert.True(f.Queue.UndoQueueChange(moved.Undo!.Id, moved.AccountEpoch));
        Assert.Equal(entries, f.Queue.Snapshot.Entries); Assert.Equal(before.Playback, f.Queue.Snapshot.Playback); Assert.Single(f.Audio.Opened);
        Assert.False(f.Queue.UndoQueueChange(moved.Undo.Id, moved.AccountEpoch));
    }
    [Fact, Trait("V6", "Q02")]
    public async Task 删除当前项后撤销仅补回条目不回退下一首且最后一首恢复保持停止()
    {
        await using var f = new PlaybackFixture(); var entries = Entries(2); await f.Queue.ReplaceAsync(entries, 0, default);
        await f.Queue.RemoveAsync(entries[0].EntryId, default);
        var removed = f.Queue.Snapshot; Assert.Equal(entries[1].EntryId, removed.CurrentEntryId);
        var opens = f.Audio.Opened.Count; f.Audio.Emit(removed.Playback.Generation, PlaybackState.Playing, 2000);
        Assert.True(f.Queue.UndoQueueChange(removed.Undo!.Id, removed.AccountEpoch));
        Assert.Equal(entries[1].EntryId, f.Queue.Snapshot.CurrentEntryId); Assert.Equal(2000, f.Queue.Snapshot.Playback.PositionMs); Assert.Equal(opens, f.Audio.Opened.Count);
        await f.Queue.ReplaceAsync([entries[0]], 0, default); await f.Queue.RemoveAsync(entries[0].EntryId, default);
        removed = f.Queue.Snapshot; opens = f.Audio.Opened.Count;
        Assert.True(f.Queue.UndoQueueChange(removed.Undo!.Id, removed.AccountEpoch)); Assert.Null(f.Queue.Snapshot.CurrentEntryId);
        Assert.Equal(PlaybackState.Stopped, f.Queue.Snapshot.Playback.State); Assert.Equal(opens, f.Audio.Opened.Count);
        await f.Queue.SelectAsync(entries[0].EntryId, default); Assert.Equal(PlaybackState.Playing, f.Queue.Snapshot.Playback.State);
    }
    [Fact, Trait("V6", "Q01,Q02")]
    public async Task 多页面只保留最近逆操作而追加换歌换模式和账号撤销旧资格()
    {
        await using var f = new PlaybackFixture(); var entries = Entries(4); await f.Queue.ReplaceAsync(entries, 0, default);
        using var first = new QueueWorkspace(f.Queue, new ImmediateUi()); using var second = new QueueWorkspace(f.Queue, new ImmediateUi());
        first.Selected = first.Rows[3]; first.UpCommand.Execute(null); var old = f.Queue.Snapshot;
        second.Selected = second.Rows[1]; await second.RemoveCommand.ExecuteAsync(null); Assert.True(first.CanUndo); Assert.True(second.CanUndo);
        Assert.False(f.Queue.UndoQueueChange(old.Undo!.Id, old.AccountEpoch)); first.UndoCommand.Execute(null); Assert.False(second.CanUndo); Assert.Equal(4, second.Rows.Count);
        foreach (var invalidate in new Func<Task>[] {
            () => f.Queue.EnqueueAsync([QueueEntry.FromTrack(MusicCatalog.Track(2))], false, default),
            () => f.Queue.SelectAsync(entries[1].EntryId, default),
            () => { f.Queue.SetMode(PlaybackMode.Shuffle); return Task.CompletedTask; } })
        {
            var snapshot = f.Queue.Snapshot; Assert.True(f.Queue.MoveTo(new(entries[0].EntryId, f.Queue.Snapshot.Entries.Count - 1, snapshot.QueueRevision, snapshot.AccountEpoch)));
            var undo = f.Queue.Snapshot; await invalidate(); Assert.False(f.Queue.UndoQueueChange(undo.Undo!.Id, undo.AccountEpoch));
            f.Queue.Move(entries[0].EntryId, -1);
        }
        var stale = f.Queue.Snapshot; f.Sessions.Relogin(); await f.Queue.EnqueueAsync(Entries(2), false, default);
        Assert.False(f.Queue.UndoQueueChange(stale.Undo!.Id, stale.AccountEpoch));
        Assert.False(f.Queue.MoveTo(new(entries[0].EntryId, 0, stale.QueueRevision, stale.AccountEpoch)));
    }
    [Fact, Trait("V6", "Q02")]
    public async Task 撤销移除保留下一首优先段而容量已满后的旧撤销不能插入()
    {
        await using var f = new PlaybackFixture(); var entries = Entries(3); await f.Queue.ReplaceAsync([entries[0]], 0, default);
        f.Queue.SetMode(PlaybackMode.Shuffle); await f.Queue.EnqueueAsync([entries[1], entries[2]], true, default);
        await f.Queue.RemoveAsync(entries[1].EntryId, default); var removed = f.Queue.Snapshot;
        Assert.True(f.Queue.UndoQueueChange(removed.Undo!.Id, removed.AccountEpoch));
        await f.Queue.NextAsync(false, default); Assert.Equal(entries[1].EntryId, f.Queue.Snapshot.CurrentEntryId);
        await f.Queue.NextAsync(false, default); Assert.Equal(entries[2].EntryId, f.Queue.Snapshot.CurrentEntryId);
        var full = Entries(10000); await f.Queue.ReplaceAsync(full, 0, default); await f.Queue.RemoveAsync(full[9999].EntryId, default);
        removed = f.Queue.Snapshot; Assert.True(f.Queue.UndoQueueChange(removed.Undo!.Id, removed.AccountEpoch)); Assert.Equal(10000, f.Queue.Snapshot.Entries.Count);
        await f.Queue.RemoveAsync(full[9999].EntryId, default); removed = f.Queue.Snapshot;
        await f.Queue.EnqueueAsync([QueueEntry.FromTrack(MusicCatalog.Track(2))], false, default);
        Assert.False(f.Queue.UndoQueueChange(removed.Undo!.Id, removed.AccountEpoch)); Assert.Equal(10000, f.Queue.Snapshot.Entries.Count);
    }
}
