using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Music;
using Xunit;

namespace MusicNetEasePlugin.Tests;

[Collection("AvaloniaHeadless")]
public sealed class V6QueueUiTests
{
    [Fact, Trait("V6", "Q01,Q02,R01")]
    public async Task 真实手柄拖动释放单次提交边缘滚动取消和重挂不触发播放()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new PlaybackFixture(); var entries = Enumerable.Range(1, 1000).Select(_ => QueueEntry.FromTrack(MusicCatalog.Track(1))).ToArray();
            await f.Queue.EnqueueAsync(entries, false, default); using var model = new QueueWorkspace(f.Queue, new ImmediateUi());
            var view = new QueueView { DataContext = model }; var window = new Window { Content = view, Width = 480, Height = 600 };
            try
            {
                window.Show(); DesktopUiTests.Pump(window); var list = view.FindControl<SongListBox>("QueueList")!;
                Point Handle(int index) => ((Control)list.ContainerFromIndex(index)!).GetVisualDescendants().OfType<Button>().Single(b => b.Name == "QueueDragHandle").TranslatePoint(new Point(12, 15), window)!.Value;
                var source = Handle(0); var target = ((Control)list.ContainerFromIndex(2)!).TranslatePoint(new Point(80, 45), window)!.Value;
                var before = f.Queue.Snapshot.QueueRevision;
                window.MouseDown(source, MouseButton.Left); window.MouseMove(target); DesktopUiTests.Pump(window);
                Assert.True(view.Drag.IsDragging); Assert.True(view.FindControl<Border>("DropIndicator")!.IsVisible); Assert.Equal(before, f.Queue.Snapshot.QueueRevision);
                window.MouseUp(target, MouseButton.Left); DesktopUiTests.Pump(window);
                Assert.Equal(entries[0].EntryId, model.Rows[2].Id); Assert.Equal(before + 1, f.Queue.Snapshot.QueueRevision); Assert.Empty(f.Audio.Opened);
                model.UndoCommand.Execute(null); DesktopUiTests.Pump(window); Assert.Equal(entries[0].EntryId, model.Rows[0].Id);
                var handlePoint = Handle(0); var hit = window.InputHitTest(handlePoint) as Control;
                Assert.True(hit?.Name == "QueueDragHandle" || hit?.GetVisualAncestors().OfType<Control>().Any(c => c.Name == "QueueDragHandle") == true, $"Hit {hit} at {handlePoint}");
                window.MouseDown(handlePoint, MouseButton.Left);
                Assert.Empty(f.Audio.Opened); Assert.Equal(entries[0].EntryId, model.Selected?.Id);
                var edge = list.TranslatePoint(new Point(30, list.Bounds.Height - 8), window)!.Value;
                window.MouseMove(edge); Assert.True(view.Drag.EdgeTimerRunning, $"drag={view.Drag.IsDragging}, handle={Handle(0)}, edge={edge}, bounds={list.Bounds}, visible={list.IsEffectivelyVisible}, indicator={view.FindControl<Border>("DropIndicator")!.IsVisible}");
                await Task.Delay(150); DesktopUiTests.Pump(window);
                var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First(); var observedScroll = scroll.Offset.Y; Assert.True(observedScroll > 0);
                window.KeyPress(Key.Escape, RawInputModifiers.None, default, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, default, null);
                window.MouseUp(edge, MouseButton.Left); Assert.False(view.Drag.EdgeTimerRunning); Assert.False(view.Drag.IsDragging);
                scroll.Offset = default; DesktopUiTests.Pump(window); window.MouseDown(Handle(0), MouseButton.Left); window.MouseMove(target);
                await f.Queue.EnqueueAsync([QueueEntry.FromTrack(MusicCatalog.Track(2))], false, default); DesktopUiTests.Pump(window);
                Assert.False(view.Drag.IsDragging); Assert.Contains("取消拖动", model.Message); window.MouseUp(target, MouseButton.Left);
                window.MouseDown(Handle(0), MouseButton.Left); window.MouseMove(edge); view.IsVisible = false;
                Assert.False(view.Drag.EdgeTimerRunning); window.MouseUp(edge, MouseButton.Left); view.IsVisible = true;
                for (var i = 0; i < 20; i++) { window.Content = null; window.Content = view; DesktopUiTests.Pump(window); }
                Assert.False(view.Drag.IsDragging); Assert.False(view.Drag.EdgeTimerRunning); Assert.Empty(f.Audio.Opened);
                Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 30);
                TestEvidence.Write("v6-queue-gesture.json", new { schemaVersion = 1, realHost = false, Items = model.Rows.Count, CommitsOnRelease = 1, EdgeScrollOffset = observedScroll, TimerStopped = !view.Drag.EdgeTimerRunning, Reattachments = 20, AudioOpened = f.Audio.Opened.Count });
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
}
