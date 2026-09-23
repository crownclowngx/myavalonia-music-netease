using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Music;
using MusicNetEasePlugin.Infrastructure.Persistence;
using Xunit;

namespace MusicNetEasePlugin.Tests;

[Collection("AvaloniaHeadless")]
public sealed class V6ResponsiveUiTests
{
    [Theory, InlineData(false), InlineData(true), Trait("V6", "N01,N03,P01,U01,R01")]
    public async Task 三尺寸双色双抽屉保留浏览且密度切换不重开媒体(bool dark)
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new DailyPlayerAcceptanceTests.Fixture(); await f.Initialize();
            using var dir = new TestDirectory(); using var prefs = new UiPreferences(new UiPreferencesStore(dir.Path), new ImmediateUi()); prefs.ReduceMotion = true;
            using var life = new MusicLifetime(); using var model = new MusicWorkspace(f.Player.Catalog, f.Player.Sessions, f.Player.Queue, f.Login, new ImmediateUi(), life, preferences: prefs, lyrics: f.Lyrics);
            var view = new MusicView { DataContext = model }; var window = new Window { Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                window.Show(); await model.Player.ResumeCommand.ExecuteAsync(null); await f.Lyrics.Pending;
                var tracks = Enumerable.Range(1, 40).Select(i => MusicCatalog.Track(i) with { Name = "长中文歌曲名称与完整信息 · " + i, Artists = "合唱歌手甲 / 合唱歌手乙", Album = "测试专辑与长文本" }).ToArray();
                f.Player.Catalog.Search = (_, offset, _) => Task.FromResult(new MusicSearchPage(tracks, offset, true));
                model.Keyword = "保留搜索"; await model.SearchCommand.ExecuteAsync(null); model.SelectedTrack = model.Tracks[4];
                await f.Player.Queue.EnqueueAsync(tracks.Select(t => QueueEntry.FromTrack(t)).ToArray(), false, default);
                foreach (var (width, height) in new[] { (1200, 720), (800, 600), (520, 420) })
                {
                    window.Width = width; window.Height = height; DesktopUiTests.Pump(window);
                    foreach (var queue in new[] { false, true })
                    {
                        if (queue) model.ShowQueueCommand.Execute(null); else model.ShowLyricsCommand.Execute(null);
                        DesktopUiTests.Pump(window); Assert.True(model.Navigation.IsSearch);
                        if (!queue)
                        {
                            prefs.ComfortableDensity = true; DesktopUiTests.Pump(window);
                            Assert.All(view.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && text.Classes.Contains("lyric")), text => Assert.Equal(22, text.FontSize));
                            prefs.ComfortableDensity = false; DesktopUiTests.Pump(window);
                        }
                        foreach (var song in view.GetVisualDescendants().OfType<SongRowView>().Where(s => s.IsEffectivelyVisible && s.Bounds.Width >= 760))
                        {
                            var artist = song.FindControl<TextBlock>("ArtistColumn")!;
                            var album = song.FindControl<TextBlock>("AlbumColumn")!;
                            var textGrid = song.FindControl<Grid>("TextLayout")!;
                            Assert.True(artist.Bounds.X >= textGrid.Children[0].Bounds.Right, $"song={song.Bounds}, title={textGrid.Children[0].Bounds}, artist={artist.Bounds}, album={album.Bounds}, columns={string.Join(',', textGrid.ColumnDefinitions.Select(c => c.ActualWidth))}");
                        }
                        var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == (queue ? "QueueList" : "LyricList"));
                        Assert.True(list.IsEffectivelyVisible); Assert.True(list.Bounds.Height > 50);
                        Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 30);
                        var realized = list.GetVisualDescendants().OfType<ListBoxItem>().Where(item => item.IsEffectivelyVisible).Select(item => (item.Bounds.Height, Y: item.TranslatePoint(default, list)!.Value.Y, Index: list.IndexFromContainer(item))).OrderBy(item => item.Y).ToArray();
                        for (var index = 1; index < realized.Length; index++) Assert.True(realized[index].Y >= realized[index - 1].Y + realized[index - 1].Height - 1, $"overlap {width}/{queue}: {string.Join(';', realized)}");
                        foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                        {
                            if (button.GetVisualAncestors().OfType<ListBoxItem>().Any()) continue; // 虚拟化缓冲行可处于视口之外。
                            var position = button.TranslatePoint(default, view)!.Value;
                            Assert.True(position.X >= -1 && position.Y >= -1 && position.X + button.Bounds.Width <= width + 1 && position.Y + button.Bounds.Height <= height + 1, $"{button.Name}/{button.Content}: {position}, {button.Bounds}");
                        }
                        Save(window, $"v6-{(queue ? "queue" : "lyrics")}-{(dark ? "dark" : "light")}-{width}.png");
                    }
                    model.Navigation.Back(); DesktopUiTests.Pump(window);
                    var trackList = view.FindControl<SongListBox>("TrackList")!; trackList.ScrollIntoView(4); DesktopUiTests.Pump(window);
                    var before = ((Control)trackList.ContainerFromIndex(4)!).Bounds.Height;
                    prefs.ComfortableDensity = true; DesktopUiTests.Pump(window);
                    Assert.True(((Control)trackList.ContainerFromIndex(4)!).Bounds.Height >= before);
                    Assert.Equal(5, model.SelectedTrack!.Id); Assert.Equal("保留搜索", model.Keyword); prefs.ComfortableDensity = false;
                }
                Assert.Single(f.Player.Audio.Opened);
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
