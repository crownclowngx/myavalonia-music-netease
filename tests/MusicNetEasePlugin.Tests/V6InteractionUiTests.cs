using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Music;
using MusicNetEasePlugin.Infrastructure.Persistence;
using Xunit;

namespace MusicNetEasePlugin.Tests;

[Collection("AvaloniaHeadless")]
public sealed class V6InteractionUiTests
{
    [Fact, Trait("V6", "L01,P01,B02,R01")]
    public async Task 歌词当前行居中手动停止跟随后恢复且紧凑播放控制可达()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new DailyPlayerAcceptanceTests.Fixture(); await f.Initialize();
            using var dir = new TestDirectory(); using var preferences = new UiPreferences(new UiPreferencesStore(dir.Path), new ImmediateUi());
            preferences.ReduceMotion = true;
            using var life = new MusicLifetime(); using var model = new MusicWorkspace(f.Player.Catalog, f.Player.Sessions, f.Player.Queue, f.Login, new ImmediateUi(), life, preferences: preferences, lyrics: f.Lyrics);
            var view = new MusicView { DataContext = model }; var window = new Window { Content = view, Width = 1200, Height = 720 };
            try
            {
                window.Show(); await model.Player.ResumeCommand.ExecuteAsync(null); await f.Lyrics.Pending;
                model.ShowLyricsCommand.Execute(null); DesktopUiTests.Pump(window);
                f.Player.Audio.Emit(f.Player.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 45000); DesktopUiTests.Pump(window);
                var lyricsView = view.GetVisualDescendants().OfType<LyricsView>().Single();
                var list = lyricsView.FindControl<ListBox>("LyricList")!; var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                var row = (Control)list.ContainerFromIndex(model.Lyrics!.CurrentLine)!;
                var center = row.TranslatePoint(new Point(0, row.Bounds.Height / 2), scroll)!.Value.Y;
                var centerDeviation = Math.Abs(center - scroll.Viewport.Height / 2); Assert.InRange(centerDeviation, 0, 5);
                model.Lyrics.StopFollowing(); var offset = scroll.Offset;
                f.Player.Audio.Emit(f.Player.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 48000); DesktopUiTests.Pump(window);
                Assert.Equal(offset, scroll.Offset); model.Lyrics.FollowCommand.Execute(null); DesktopUiTests.Pump(window); Assert.NotEqual(offset, scroll.Offset);
                Save(window, "v6-drawer-lyrics.png");
                model.Navigation.Back(); window.Width = 520; window.Height = 420; DesktopUiTests.Pump(window);
                var bar = view.FindControl<PlaybackBarView>("PlayerBar")!;
                foreach (var name in new[] { "PauseButton", "NextTrackButton", "PreviousTrackButton", "LyricsToggle", "QueueToggle", "MorePlaybackButton" })
                {
                    var button = bar.FindControl<Button>(name)!; Assert.True(button.IsEffectivelyVisible);
                    var point = button.TranslatePoint(default, view)!.Value;
                    Assert.InRange(point.X, 0, view.Bounds.Width - button.Bounds.Width + 1); Assert.InRange(point.Y, 0, view.Bounds.Height - button.Bounds.Height + 1);
                }
                Assert.False(bar.FindControl<TextBlock>("PlayerArtists")!.IsVisible); Assert.True(bar.Bounds.Height <= 140);
                Save(window, "v6-compact-player.png");
                TestEvidence.Write("v6-interaction-ui.json", new { schemaVersion = 1, realHost = false, CenterDeviation = centerDeviation, CompactHeight = bar.Bounds.Height, AudioOpened = f.Player.Audio.Opened.Count, ManualFollowRestored = true });
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    private static void Save(Window window, string name)
    {
        var root = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS"); if (string.IsNullOrEmpty(root)) return;
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(root, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        TestEvidence.Screenshot(name, frame.PixelSize.Width, frame.PixelSize.Height);
    }
}
