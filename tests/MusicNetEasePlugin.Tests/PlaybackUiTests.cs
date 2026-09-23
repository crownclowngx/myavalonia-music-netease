using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Music;
using MusicNetEasePlugin.Features.Settings;
using MusicNetEasePlugin.Infrastructure.Ui;
using Xunit;

namespace MusicNetEasePlugin.Tests;

[CollectionDefinition("AvaloniaHeadless", DisableParallelization = true)]
public sealed class AvaloniaHeadlessCollection;

[Collection("AvaloniaHeadless")]
public sealed class PlaybackUiTests
{
    public static AppBuilder BuildAvaloniaApp() => UiCompositionTests.BuildAvaloniaApp();
    [Fact, Trait("M1", "U01,U02,U03,U07,U08,T08")]
    public async Task 真实音乐视图和设置视图重复重挂保留状态及草稿()
    {
        var headless = HeadlessSessions.Get(typeof(PlaybackUiTests));
        await headless.Dispatch(async () =>
        {
            await using var f = new PlaybackFixture(); await using var login = TestLogin.Create(new(), new(), TimeProvider.System, LoginOptions.Default);
            using var lifetime = new MusicLifetime(); var ui = new LoginUiDispatcher();
            using var document = new MusicWorkspace(f.Catalog, f.Sessions, f.Queue, login, ui, lifetime);
            var settings = new MemoryVlcSettings(); var runtime = new RuntimeStatus();
            using var tool = new MusicSettingsTool(login, settings, new DirectoryProbe(p => new(p, [])), runtime, ui);
            var music = new MusicView { DataContext = document }; var settingsView = new MusicSettingsView { DataContext = tool };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*") };
            // V3 的播放条需要真实的有限高度；外包 ScrollViewer 会重新引入手机式整页滚动。
            Grid.SetColumn(settingsView, 1); grid.Children.Add(music); grid.Children.Add(settingsView);
            var window = new Window { Width = 1080, Height = 880, Content = grid };
            try
            {
                window.Show(); await tool.EnsureLoadedAsync(); Dispatcher.UIThread.RunJobs();
                tool.DirectoryPath = "尚未保存的共享目录";
                await document.SearchAsync(0, "音乐"); Dispatcher.UIThread.RunJobs();
                document.SelectedTrack = document.Tracks[0]; await document.PlayCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
                Assert.True(music.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "PauseButton")!.IsEnabled);
                for (var i = 0; i < 20; i++)
                {
                    window.Content = null; window.Content = grid; Dispatcher.UIThread.RunJobs();
                    Assert.Equal(PlaybackState.Playing, f.Player.Snapshot.State);
                }
                await document.Player.PauseCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
                window.Content = null;
                grid.Children.Remove(settingsView); settingsView = new MusicSettingsView { DataContext = tool }; Grid.SetColumn(settingsView, 1); grid.Children.Add(settingsView);
                window.Content = grid; Dispatcher.UIThread.RunJobs();
                Assert.Equal("尚未保存的共享目录", tool.DirectoryPath); Assert.Equal(1, settings.Loads);
                Assert.False(runtime.LoadAttempted); Assert.Single(f.Audio.Opened); Assert.Equal(PlaybackState.Paused, f.Player.Snapshot.State);
                Assert.Null(music.GetVisualDescendants().OfType<Image>().Single(c => c.Name == "CoverImage")!.Source);
                var artifacts = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS");
                if (!string.IsNullOrEmpty(artifacts))
                {
                    using var wide = window.CaptureRenderedFrame();
                    wide!.Save(Path.Combine(artifacts, "m1-music-and-settings.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    window.Width = 760; window.Height = 720; window.UpdateLayout();
                    using var compact = window.CaptureRenderedFrame();
                    compact!.Save(Path.Combine(artifacts, "m1-compact.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                using var other = new MusicSettingsTool(login, new MemoryVlcSettings(), new DirectoryProbe(p => new(p, [])), runtime, ui);
                foreach (var reason in new[] { "detach", "rebind", "draft", "valid" })
                {
                    var selected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var calls = 0;
                    var pickerView = new MusicSettingsView(_ => { calls++; return selected.Task; }) { DataContext = tool };
                    window.Content = pickerView; Dispatcher.UIThread.RunJobs();
                    var picking = pickerView.ChooseDirectoryAsync();
                    await pickerView.ChooseDirectoryAsync(); Assert.Equal(1, calls);
                    if (reason == "detach") window.Content = null;
                    if (reason == "rebind") pickerView.DataContext = other;
                    if (reason == "draft") tool.DirectoryPath = "updated-draft";
                    selected.SetResult("selected-" + reason); await picking;
                    if (reason == "valid") Assert.Equal("selected-valid", tool.DirectoryPath);
                    else Assert.NotEqual("selected-" + reason, tool.DirectoryPath);
                    Assert.NotEqual("selected-" + reason, other.DirectoryPath);
                    Assert.Equal(PlaybackState.Paused, f.Player.Snapshot.State);
                }
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
}
