using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Features.Library;
using Xunit;

namespace MusicNetEasePlugin.Tests;

[Collection("AvaloniaHeadless")]
public sealed class DailyPlayerUiTests
{
    public static AppBuilder BuildAvaloniaApp() => UiCompositionTests.BuildAvaloniaApp();
    [Theory, InlineData(false), InlineData(true), Trait("M2", "P06,U01,U03,U04,U05")]
    public async Task 生产歌单视图可导航且大列表保持虚拟化(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DailyPlayerUiTests));
        await session.Dispatch(async () =>
        {
            using var sessions = new MusicSessions(); var api = new PlaylistTests.PlaylistFake();
            api.Pages = (offset, _) => Task.FromResult(new PlaylistPage([new(7, "测试歌单与长名称", null, 10000, true)], 1, false));
            api.Detail = (id, _) => Task.FromResult(new PlaylistTracks(id, "测试歌单", Enumerable.Range(1, 10000).Select(i => (long)i).ToArray(), true, ""));
            using var browser = new PlaylistBrowser(api, sessions, new ImmediateUi());
            var view = new PlaylistView { DataContext = browser };
            var window = new Window { Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Width = 520, Height = 280 };
            try
            {
                window.Show(); await browser.RefreshCommand.ExecuteAsync(null); DesktopUiTests.Pump(window);
                browser.SelectedPlaylist = browser.Playlists.Single();
                await browser.OpenCommand.ExecuteAsync(null); DesktopUiTests.Pump(window);
                Assert.Equal(1, browser.TabIndex); Assert.Equal(50, browser.Tracks.Count);
                var list = view.FindControl<ListBox>("PlaylistTrackList")!;
                Assert.True(list.Bounds.Height > 30); Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 20);
                await browser.NextTracksCommand.ExecuteAsync(null); DesktopUiTests.Pump(window);
                Assert.Equal(50, browser.Tracks[0].Index);
                foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                {
                    var point = button.TranslatePoint(default, view)!.Value;
                    Assert.InRange(point.X, 0, view.Bounds.Width);
                    Assert.True(point.X + button.Bounds.Width <= view.Bounds.Width + 1);
                }
                sessions.Revoke(); DesktopUiTests.Pump(window); Assert.Empty(browser.Tracks);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
}
