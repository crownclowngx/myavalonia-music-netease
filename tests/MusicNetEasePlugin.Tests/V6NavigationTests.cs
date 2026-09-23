using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Features.Music;
using MusicNetEasePlugin.Infrastructure.Persistence;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class V6NavigationTests
{
    [Fact, Trait("V6", "N01,N02")]
    public void 侧栏互斥但浏览独立且窗口限位不改期望宽度()
    {
        var nav = new MusicNavigation(); nav.SetDesiredWidth(480); nav.SetAvailableSize(1200, 300);
        nav.Browse(MusicBrowsePage.Library); nav.ShowLyricsCommand.Execute(null);
        Assert.True(nav.IsLyrics && nav.IsLibrary && nav.IsBeside);
        nav.ShowQueueCommand.Execute(null); Assert.True(nav.IsQueue); Assert.False(nav.IsLyrics);
        nav.Browse(MusicBrowsePage.History); Assert.True(nav.IsHistory && nav.IsQueue);
        nav.SetAvailableSize(320, 420); Assert.Equal(304, nav.ActualDrawerWidth); Assert.False(nav.IsBeside);
        nav.SetAvailableSize(1200, 720); Assert.Equal(480, nav.ActualDrawerWidth);
        nav.ShowQueueCommand.Execute(null); Assert.False(nav.IsOpen); Assert.True(nav.IsHistory);
        nav.SetDesiredWidth(double.NaN); Assert.Equal(480, nav.ActualDrawerWidth);
    }
    [Theory, InlineData(1), InlineData(2), Trait("V6", "N02,U01")]
    public async Task 旧配置迁移并独立保存抽屉宽度和舒适密度(int schema)
    {
        using var dir = new TestDirectory(); var path = Path.Combine(dir.Path, "ui-preferences.json");
        await File.WriteAllTextAsync(path, $"{{\"schemaVersion\":{schema},\"reduceMotion\":true,\"showArtwork\":false,\"showTranslation\":false}}");
        var store = new UiPreferencesStore(dir.Path); using var preferences = new UiPreferences(store, new ImmediateUi());
        await preferences.EnsureLoadedAsync(); Assert.Equal(360, preferences.DrawerWidth); Assert.False(preferences.ComfortableDensity);
        preferences.DrawerWidth = 460; preferences.ComfortableDensity = true; await preferences.PendingSave;
        var restored = await store.LoadAsync(default); Assert.Equal(460, restored.DrawerWidth); Assert.True(restored.ComfortableDensity); Assert.True(restored.ReduceMotion);
        Assert.Equal(schema == 1, restored.ShowArtwork); Assert.Equal(schema == 1, restored.ShowTranslation);
        preferences.DrawerWidth = double.PositiveInfinity; Assert.Equal(460, preferences.DrawerWidth);
    }
}

[Collection("AvaloniaHeadless")]
public sealed class V6DrawerUiTests
{
    [Fact, Trait("V6", "N01,N02,N03,U01,R01")]
    public async Task 真实抽屉键盘缩放重挂保持浏览与播放且关闭归还焦点()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new DailyPlayerAcceptanceTests.Fixture(); await f.Initialize();
            using var dir = new TestDirectory(); using var preferences = new UiPreferences(new UiPreferencesStore(dir.Path), new ImmediateUi());
            await preferences.EnsureLoadedAsync(); preferences.ReduceMotion = true;
            using var life = new MusicLifetime(); using var model = new MusicWorkspace(f.Player.Catalog, f.Player.Sessions, f.Player.Queue, f.Login, new ImmediateUi(), life, preferences: preferences, lyrics: f.Lyrics);
            var view = new MusicView { DataContext = model }; var window = new Window { Content = view, Width = 1200, Height = 720 };
            try
            {
                window.Show(); await model.Player.ResumeCommand.ExecuteAsync(null); await f.Lyrics.Pending; DesktopUiTests.Pump(window);
                var search = view.FindControl<TextBox>("SearchInput")!; search.Text = "原关键词"; search.Focus();
                model.ShowLyricsCommand.Execute(null); DesktopUiTests.Pump(window);
                var drawer = view.FindControl<Border>("Drawer")!;
                Assert.True(drawer.IsEffectivelyVisible); Assert.True(model.Navigation.IsSearch); Assert.True(model.Navigation.IsBeside);
                Assert.True(view.FindControl<Button>("CloseDrawerButton")!.IsFocused);
                var handle = view.FindControl<Border>("DrawerResize")!; handle.Focus();
                Press(window, Key.Left); DesktopUiTests.Pump(window); Assert.Equal(380, preferences.DrawerWidth);
                var handlePoint = handle.TranslatePoint(new Point(4, 30), window)!.Value;
                window.MouseDown(handlePoint, MouseButton.Left); window.MouseMove(handlePoint + new Vector(-30, 0)); DesktopUiTests.Pump(window);
                Assert.Equal(380, preferences.DrawerWidth); Assert.Equal(410, model.Navigation.ActualDrawerWidth);
                window.MouseUp(handlePoint + new Vector(-30, 0), MouseButton.Left); DesktopUiTests.Pump(window); Assert.Equal(410, preferences.DrawerWidth);
                handlePoint = handle.TranslatePoint(new Point(4, 30), window)!.Value;
                window.MouseDown(handlePoint, MouseButton.Left); window.MouseMove(handlePoint + new Vector(-20, 0)); Press(window, Key.Escape);
                window.MouseUp(handlePoint + new Vector(-20, 0), MouseButton.Left); DesktopUiTests.Pump(window);
                Assert.True(model.Navigation.IsOpen); Assert.Equal(410, preferences.DrawerWidth); Assert.Equal(410, model.Navigation.ActualDrawerWidth);
                preferences.DrawerWidth = 380;
                window.Width = 520; window.Height = 420; DesktopUiTests.Pump(window); Assert.False(model.Navigation.IsBeside); Assert.Equal(380, preferences.DrawerWidth);
                Assert.True(drawer.Bounds.Width <= view.Bounds.Width);
                Press(window, Key.Escape); DesktopUiTests.Pump(window); Assert.False(drawer.IsVisible); Assert.False(drawer.IsHitTestVisible); Assert.True(search.IsFocused);
                model.ShowQueueCommand.Execute(null); Press(window, Key.F, RawInputModifiers.Control); Assert.True(search.IsFocused); Assert.Equal("原关键词", search.SelectedText);
                for (var i = 0; i < 20; i++) { window.Content = null; window.Content = view; DesktopUiTests.Pump(window); }
                Assert.True(model.Navigation.IsQueue); Assert.Equal("原关键词", search.Text); Assert.Single(f.Player.Audio.Opened);
                preferences.ComfortableDensity = true; DesktopUiTests.Pump(window); Assert.Contains("netease-comfortable", view.Classes);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    { window.KeyPress(key, modifiers, default, null); window.KeyRelease(key, modifiers, default, null); }
}
