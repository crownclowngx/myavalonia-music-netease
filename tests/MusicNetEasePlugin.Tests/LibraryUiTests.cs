using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using MusicNetEasePlugin.Application.Appearance;
using MusicNetEasePlugin.Application.Library;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Library;
using MusicNetEasePlugin.Features.Music;
using MusicNetEasePlugin.Infrastructure.Persistence;
using MusicNetEasePlugin.Infrastructure.Ui;
using Xunit;

namespace MusicNetEasePlugin.Tests;

/// <summary>通过生产 View/按钮/焦点验证功能；只替换远端事实和音频边界，截图来自 Skia 实际渲染。</summary>
[Collection("AvaloniaHeadless")]
public sealed class LibraryUiTests
{
    [Fact]
    public async Task 搜索歌单队列历史及播放菜单共享喜欢且换曲不误写新曲()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); await f.Initialize(); using var p = f.Page(); var w = new Window { Width = 800, Height = 600, Content = p.View }; w.Show();
            try
            {
                await p.Model.Player.ResumeCommand.ExecuteAsync(null); f.Player.Player.Audio.Emit(f.Player.Player.Queue.Snapshot.Playback.Generation, PlaybackState.Playing, 3500);
                p.Model.Keyword = "歌曲"; await p.Model.SearchCommand.ExecuteAsync(null); Pump(w); await f.Library.SetLikedAsync(1, true); Pump(w);
                CheckActions(p.View, p.Editor);
                await p.Open(7, w); CheckActions(p.View, p.Editor);
                p.Model.ShowQueueCommand.Execute(null); Pump(w); CheckActions(Control<Grid>(p.View, "QueuePanel"), p.Editor);
                p.Model.Navigation.Back(); p.Model.ShowHistoryCommand.Execute(null); Pump(w); Assert.NotEmpty(p.Model.History!.Rows); CheckActions(Control<ListBox>(p.View, "HistoryList"), p.Editor);
                var more = Button(p.View, "MorePlaybackButton"); await Click(more); Pump(w);
                var content = (Control)((Flyout)more.Flyout!).Content!; CheckActions(content, p.Editor);
                var action = content.GetVisualDescendants().OfType<SongLibraryActionsView>().Single(); Assert.Equal(1, action.TrackId);
                action.FindControl<Button>("LikeSongButton")!.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); await p.Editor.LikeCommand.ExecutionTask!; Pump(w);
                Assert.False(f.Library.Snapshot.IsLiked(1)); more.Flyout.Hide(); Assert.Single(f.Player.Player.Audio.Opened);
                var release = new TaskCompletionSource<LibraryWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously); f.Api.Write = (_, _) => release.Task;
                var old = p.Editor.LikeCommand.ExecuteAsync(1L); await f.Player.Player.Queue.PlayNowAsync(QueueEntry.FromTrack(MusicCatalog.Track(2)), default);
                f.Api.Likes = f.Api.Likes.Add(1); release.SetResult(new(LibraryReceiptState.Accepted)); await old; Pump(w);
                Assert.Equal(2, p.Model.Player.CurrentTrackId); Assert.False(p.Editor.Snapshot.IsLiked(2)); Assert.Equal(1, f.Api.Writes.Last().TargetId);
            }
            finally { w.Close(); }
            return true;
        }, default);
        static void CheckActions(Control root, PlaylistEditor editor)
        {
            var rows = root.GetVisualDescendants().OfType<SongLibraryActionsView>().Where(v => v.IsEffectivelyVisible && v.TrackId == 1).ToArray(); Assert.NotEmpty(rows);
            Assert.All(rows, v => { Assert.Same(editor, v.Editor); Assert.Equal("♥", v.FindControl<Button>("LikeSongButton")!.Content); });
        }
    }

    [Fact]
    public async Task 保存回包不能覆盖关闭后重新打开的表单()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); await f.Initialize(); using var p = f.Page(); var w = new Window { Width = 800, Height = 600, Content = p.View }; w.Show();
            try
            {
                await p.Open(7, w); p.Editor.EditPanelCommand.Execute(null); p.Editor.NameDraft = "第一稿";
                var pending = new TaskCompletionSource<LibraryWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously); f.Api.Write = (_, _) => pending.Task;
                var saving = p.Editor.SaveNameCommand.ExecuteAsync(null); p.Editor.DiscardCommand.Execute(null); p.Editor.EditPanelCommand.Execute(null); p.Editor.NameDraft = "第二稿";
                f.Api.Playlists[7] = f.Api.Playlists[7] with { Name = "第一稿" }; pending.SetResult(new(LibraryReceiptState.Accepted)); await saving; await p.Settle(w);
                Assert.Equal("第二稿", p.Editor.NameDraft); Assert.Equal(LibraryEditorPage.Edit, p.Editor.Page); Assert.Single(f.Api.Writes);
            }
            finally { w.Close(); }
            return true;
        }, default);
    }
    [Fact]
    public async Task 三尺寸双色两密度的库编辑确认与结果可滚动可命中()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); await f.Initialize(); using var page = f.Page();
            var window = new Window { Content = page.View }; window.Show();
            var measurements = new List<object>();
            try
            {
                await page.Open(7, window);
                foreach (var dark in new[] { false, true }) foreach (var (width, height) in new[] { (1200, 720), (800, 600), (520, 420) }) foreach (var comfortable in new[] { false, true })
                {
                    window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; window.Width = width; window.Height = height; f.Preferences.ComfortableDensity = comfortable; Pump(window);
                    var theme = dark ? "dark" : "light";
                    var edit = Button(page.View, "EditPlaylistButton"); Inside(edit, page.View); Assert.True(edit.IsEffectivelyEnabled);
                    Assert.True(Control<ListBox>(page.View, "PlaylistTrackList").Bounds.Height >= 60, $"{width}/{height}/{comfortable} 歌曲视口过小");
                    var actions = page.View.GetVisualDescendants().OfType<SongLibraryActionsView>().Where(v => v.IsEffectivelyVisible && v.TrackId > 0).ToArray();
                    Assert.NotEmpty(actions); Assert.All(actions, v => Assert.Same(page.Editor, v.Editor));
                    if (!comfortable) Save(window, $"v7-library-{theme}-{width}.png");
                    await Click(edit); Pump(window);
                    Assert.Equal(LibraryEditorPage.Edit, page.Editor.Page);
                    var name = Control<TextBox>(page.View, "PlaylistNameInput"); Assert.Same(name, window.FocusManager!.GetFocusedElement());
                    name.Text = new string('长', 101); Control<TextBox>(page.View, "PlaylistDescriptionInput").Text = string.Join('\n', Enumerable.Repeat("较长的中文描述与换行，用来检验滚动和字段反馈。", 15)); Pump(window);
                    Assert.False(Button(page.View, "SaveNameButton").IsEffectivelyEnabled); Assert.NotEmpty(page.Editor.NameError);
                    var save = Button(page.View, "SaveDescriptionButton"); save.BringIntoView(); Pump(window); Pump(window); Inside(save, page.View);
                    var point = save.TranslatePoint(new(save.Bounds.Width / 2, save.Bounds.Height / 2), page.View)!.Value;
                    var hit = page.View.InputHitTest(point) as Visual; Assert.True(hit == save || hit?.GetVisualAncestors().Contains(save) == true, $"{width}/{height}/{comfortable}: {point}, hit={hit}, save={save.Bounds}, scroll={Control<ScrollViewer>(page.View, "EditorScroll").Offset}");
                    Inside(Button(page.View, "CloseEditor"), page.View);
                    if (comfortable) Save(window, $"v7-editor-{theme}-{width}.png");

                    page.Editor.DiscardCommand.Execute(null); Pump(window);
                    Assert.False(page.Editor.IsOpen); Assert.Same(edit, window.FocusManager.GetFocusedElement());
                    var horizontalOverflow = Math.Max(0, point.X + save.Bounds.Width / 2 - page.View.Bounds.Width);
                    measurements.Add(new { width, height, dark, comfortable, submitReachable = hit == save || hit?.GetVisualAncestors().Contains(save) == true, focusReturned = ReferenceEquals(edit, window.FocusManager.GetFocusedElement()), horizontalOverflow });
                }
                foreach (var dark in new[] { false, true })
                {
                    window.Width = 800; window.Height = 600; window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; Pump(window);
                    await Click(Button(page.View, "DeletePlaylistButton")); Pump(window);
                    Assert.Same(Button(page.View, "CloseEditor"), window.FocusManager!.GetFocusedElement());
                    Assert.Contains("ID 7", Control<TextBlock>(page.View, "ConfirmationTarget").Text);
                    Save(window, $"v7-delete-{(dark ? "dark" : "light")}.png");
                    var writeCount = f.Api.Writes.Count; PressKey(window, Key.Enter); Assert.Equal(writeCount, f.Api.Writes.Count); Assert.False(page.Editor.IsOpen);
                    f.Api.Write = (_, _) => Task.FromResult(new LibraryWriteReceipt(LibraryReceiptState.Uncertain, "操作可能已生效，请重新读取结果。", StopRecheck: true));
                    await f.Library.ExecuteAsync(new(LibraryOperationKind.AddTracks, 7, TrackIds: [3])); Pump(window);
                    await f.Library.RefreshLikesAsync(); Pump(window); Assert.Contains("重新读取结果", page.Editor.LastResultMessage);
                    await Click(Button(page.View, "LibraryResultsButton")); Pump(window); Assert.True(Button(page.View, "RecheckLibraryButton").IsEffectivelyEnabled);
                    Assert.Contains("playlist:7", page.Editor.PendingKeys); Save(window, $"v7-recheck-{(dark ? "dark" : "light")}.png");
                    PressKey(window, Key.Escape); Assert.False(page.Editor.IsOpen);
                }
                Assert.Equal(12, measurements.Count); Assert.Empty(f.Player.Player.Audio.Opened);
                var artifacts = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS");
                if (!string.IsNullOrEmpty(artifacts))
                {
                    var screenshots = Directory.GetFiles(artifacts, "v7-*.png").Length; Assert.Equal(16, screenshots);
                    TestEvidence.Write("v7-library-layout.json", new { schemaVersion = 1, realHost = false, combinations = measurements, screenshots });
                }
            }
            finally { window.Close(); }
            return true;
        }, default);
    }

    [Fact]
    public async Task ID表单新建独立字段草稿冲突与输入法键盘不会误提交()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); await f.Initialize(); using var p = f.Page(); var w = new Window { Width = 800, Height = 600, Content = p.View }; w.Show();
            try
            {
                await p.Model.ShowLibraryCommand.ExecuteAsync(null); Pump(w);
                await Click(Button(p.View, "OpenByIdButton")); Pump(w);
                var input = Control<TextBox>(p.View, "PlaylistIdInput"); input.Text = "bad"; Pump(w); Assert.False(Button(p.View, "OpenPlaylistButton").IsEffectivelyEnabled);
                var reads = f.Api.ReadIds.Count; PressKey(w, Key.Enter); Assert.Equal(reads, f.Api.ReadIds.Count);
                input.Text = "7"; await Click(Button(p.View, "OpenPlaylistButton")); await p.Settle(w); Assert.Equal(7, p.Browser.Snapshot!.PlaylistId);
                await Click(Button(p.View, "EditPlaylistButton")); Pump(w); Control<TextBox>(p.View, "PlaylistNameInput").Text = "新名称";
                Control<TextBox>(p.View, "PlaylistDescriptionInput").Text = "描述草稿\n第二行";
                await Click(Button(p.View, "SaveNameButton")); await p.Settle(w); Assert.Equal("新名称", f.Api.Playlists[7].Name); Assert.Equal("描述草稿\n第二行", p.Editor.DescriptionDraft);
                f.Api.Write = (_, _) => Task.FromResult(new LibraryWriteReceipt(LibraryReceiptState.Rejected, "描述拒绝"));
                await Click(Button(p.View, "SaveDescriptionButton")); await p.Settle(w); Assert.Equal("描述草稿\n第二行", p.Editor.DescriptionDraft); Assert.Equal("原描述", f.Api.Playlists[7].Description);
                f.Api.Playlists[7] = f.Api.Playlists[7] with { Name = "外部修改" }; Control<TextBox>(p.View, "PlaylistNameInput").Text = "保留草稿";
                await Click(Button(p.View, "SaveNameButton")); Pump(w); Assert.Contains("变化", p.Editor.Message); Assert.Equal("保留草稿", p.Editor.NameDraft); Assert.Equal(2, f.Api.Writes.Count);
                Control<TextBox>(p.View, "PlaylistNameInput").Focus();
                var presenter = Control<TextBox>(p.View, "PlaylistNameInput").GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single();
                presenter.PreeditText = "拼音"; PressKey(w, Key.Escape); Assert.Equal(LibraryEditorPage.Edit, p.Editor.Page); presenter.PreeditText = null;
                PressKey(w, Key.Escape); Assert.Equal(LibraryEditorPage.Discard, p.Editor.Page); p.Editor.KeepEditingCommand.Execute(null); Pump(w); Assert.Equal("保留草稿", p.Editor.NameDraft);
                f.Player.Player.Sessions.Relogin(); Pump(w); Assert.False(p.Editor.IsOpen); Assert.Equal("", p.Editor.NameDraft); Assert.False(p.Editor.ConfirmCommand.CanExecute(null));
            }
            finally { w.Close(); }
            return true;
        }, default);
    }

    [Fact]
    public async Task 添加弹层创建只选择目标再次添加才写曲目且取消确认不删歌单()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); await f.Initialize(); using var p = f.Page(); var w = new Window { Width = 800, Height = 600, Content = p.View }; w.Show();
            try
            {
                await p.Open(7, w); await p.Editor.AddPanelCommand.ExecuteAsync(3L); Pump(w); Assert.Single(p.Editor.Targets);
                p.Editor.CreatePanelCommand.Execute(null); Pump(w); Control<TextBox>(p.View, "PlaylistNameInput").Text = "新目标";
                await Click(Button(p.View, "CreatePlaylistButton")); await p.Settle(w); Assert.Equal(LibraryEditorPage.Add, p.Editor.Page); Assert.Equal(99, p.Editor.SelectedTarget!.Id); Assert.Single(f.Api.Writes); Assert.Empty(f.Api.Playlists[99].TrackIds);
                await Click(Button(p.View, "AddTrackButton")); Pump(w); Assert.Equal(new long[] { 3 }, f.Api.Playlists[99].TrackIds); Assert.Equal(2, f.Api.Writes.Count);
                PressKey(w, Key.Escape); await p.Open(7, w); await Click(Button(p.View, "DeletePlaylistButton")); Pump(w); PressKey(w, Key.Escape); Assert.True(f.Api.Playlists.ContainsKey(7));
                await Click(Button(p.View, "DeletePlaylistButton")); Pump(w); await p.Browser.OpenAsync(8); await p.Settle(w); Assert.False(p.Editor.ConfirmCommand.CanExecute(null));
                Assert.Equal(2, f.Api.Writes.Count);
            }
            finally { w.Close(); }
            return true;
        }, default);
    }

    [Fact]
    public async Task 双文档喜欢同步删除相关页和隐藏页恢复且播放队列不受影响()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); await f.Initialize(); using var a = f.Page(); using var b = f.Page();
            var wa = new Window { Width = 800, Height = 600, Content = a.View }; var wb = new Window { Width = 800, Height = 600, Content = b.View }; wa.Show(); wb.Show();
            try
            {
                await a.Open(7, wa); await b.Open(7, wb); await a.Model.Player.ResumeCommand.ExecuteAsync(null);
                var opened = f.Player.Player.Audio.Opened.Count; var queue = f.Player.Player.Queue.Snapshot; var position = queue.Playback.PositionMs;
                var action = a.View.GetVisualDescendants().OfType<SongLibraryActionsView>().First(v => v.IsEffectivelyVisible && v.TrackId == 1);
                var like = action.FindControl<Button>("LikeSongButton")!; like.Focus(); PressKey(wa, Key.Space); await a.Editor.LikeCommand.ExecutionTask!; await a.Settle(wa); await b.Settle(wb);
                Assert.True(a.Editor.Snapshot.IsLiked(1)); Assert.True(b.Editor.Snapshot.IsLiked(1)); Assert.Equal(opened, f.Player.Player.Audio.Opened.Count);
                // 隐藏页面仅记失效；重显才读取并清除已删除详情。
                b.Editor.SetVisible(false); await f.Library.ExecuteAsync(new(LibraryOperationKind.Delete, 7)); await a.Settle(wa); Pump(wb);
                Assert.Null(a.Browser.Snapshot); Assert.NotNull(b.Browser.Snapshot); b.Editor.SetVisible(true); await b.Settle(wb); Assert.Null(b.Browser.Snapshot);
                Assert.Equal(queue.Entries, f.Player.Player.Queue.Snapshot.Entries); Assert.Equal(position, f.Player.Player.Queue.Snapshot.Playback.PositionMs); Assert.Equal(opened, f.Player.Player.Audio.Opened.Count);
            }
            finally { wa.Close(); wb.Close(); }
            return true;
        }, default);
    }

    [Fact]
    public async Task 页面关闭取消刷新等待但不把取消异常投递到UI()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); await f.Initialize(); using var p = f.Page();
            var read = new TaskCompletionSource<System.Collections.Immutable.ImmutableHashSet<long>>(TaskCreationOptions.RunContinuationsAsynchronously); f.Api.ReadLikes = _ => read.Task;
            var refresh = p.Editor.RefreshAsync(); p.Dispose(); await refresh;
            read.SetResult(System.Collections.Immutable.ImmutableHashSet<long>.Empty); await f.Library.RefreshLikesAsync(); Assert.Equal(0, f.Library.SubscriberCount);
            return true;
        }, default);
    }

    [Fact]
    public async Task 二十次页面挂卸独立草稿关闭在途再开和Shutdown无残留()
    {
        await HeadlessSessions.Get(typeof(UiCompositionTests)).Dispatch(async () =>
        {
            await using var f = new Fixture(); await f.Initialize(); var w = new Window { Width = 800, Height = 600 }; w.Show(); var rounds = 0;
            try
            {
                for (; rounds < 20; rounds++)
                {
                    using var p = f.Page(); w.Content = p.View; Pump(w); p.Editor.CreatePanelCommand.Execute(null); p.Editor.NameDraft = "独立草稿"; Pump(w);
                    p.Editor.DiscardCommand.Execute(null); w.Content = null; p.Dispose(); Pump(w); Assert.Equal(0, f.Library.SubscriberCount);
                }
                var pending = new TaskCompletionSource<LibraryWriteReceipt>(TaskCreationOptions.RunContinuationsAsynchronously); f.Api.Write = (_, _) => pending.Task;
                Task write; using (var p = f.Page()) { await f.Library.RefreshLikesAsync(); write = p.Editor.LikeCommand.ExecuteAsync(1L); p.Dispose(); }
                f.Api.Likes = f.Api.Likes.Add(1); pending.SetResult(new(LibraryReceiptState.Accepted)); await write;
                using (var p = f.Page()) { w.Content = p.View; await p.Settle(w); Assert.Equal("", p.Editor.NameDraft); Assert.True(f.Library.Snapshot.IsLiked(1)); w.Content = null; }
                await f.Library.DisposeAsync(); Assert.Equal(0, f.Library.PendingTasks); Assert.Equal(0, f.Library.SubscriberCount);
                var before = f.Api.Writes.Count; await using var fresh = new MusicLibraryCoordinator(f.Player.Player.Sessions, f.Api, f.Api, f.Api); await fresh.RefreshLikesAsync();
                Assert.Equal(before, f.Api.Writes.Count); Assert.Empty(f.Player.Player.Audio.Opened);
                TestEvidence.Write("v7-library-lifetime.json", new { schemaVersion = 1, realHost = false, rounds, subscribers = f.Library.SubscriberCount, pendingTasks = f.Library.PendingTasks, extraMediaOpens = f.Player.Player.Audio.Opened.Count, shutdownDrained = f.Library.PendingTasks == 0, replayWrites = f.Api.Writes.Count - before });
            }
            finally { w.Close(); }
            return true;
        }, default);
    }

    internal static void Pump(Window w) => DesktopUiTests.Pump(w);
    internal static T Control<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    internal static Button Button(Control root, string name) => Control<Button>(root, name);
    internal static async Task Click(Button button)
    {
        Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEffectivelyEnabled, button.Name); button.Focus();
        var window = (Window)TopLevel.GetTopLevel(button)!; button.BringIntoView(); Pump(window);
        var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump(window);
        if (button.Command is IAsyncRelayCommand { ExecutionTask: { } work }) await work;
    }
    internal static void PressKey(Window w, Key key) { w.KeyPress(key, RawInputModifiers.None, default, null); w.KeyRelease(key, RawInputModifiers.None, default, null); Pump(w); }
    private static void Inside(Control c, Visual root)
    {
        var p = c.TranslatePoint(default, root)!.Value;
        Assert.True(c.Bounds.Width > 0 && c.Bounds.Height > 0 && p.X >= -1 && p.Y >= -1 && p.X + c.Bounds.Width <= root.Bounds.Width + 1 && p.Y + c.Bounds.Height <= root.Bounds.Height + 1, $"{c.Name}: {p} / {c.Bounds} outside {root.Bounds}");
    }
    private static void Save(Window w, string name)
    {
        var root = Environment.GetEnvironmentVariable("NETEASE_TEST_ARTIFACTS"); if (string.IsNullOrEmpty(root)) return;
        using var frame = w.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(root, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); TestEvidence.Screenshot(name, frame.PixelSize.Width, frame.PixelSize.Height);
    }
    internal sealed class Fixture : IAsyncDisposable
    {
        internal readonly DailyPlayerAcceptanceTests.Fixture Player = new(); internal readonly LibraryFake Api = new(); internal readonly MusicLibraryCoordinator Library;
        private readonly TestDirectory _dir = new(); internal UiPreferences Preferences { get; }
        internal Fixture()
        {
            Library = new(Player.Player.Sessions, Api, Api, Api); Preferences = new(new UiPreferencesStore(_dir.Path), new LoginUiDispatcher()) { ReduceMotion = true };
            Player.Playlists.Pages = (_, _) => Task.FromResult(new PlaylistPage(Api.Playlists.Values.Select(p => new MusicPlaylist(p.Id, p.Name, null, p.TrackCount ?? 0, p.CreatorId == 123)).ToArray(), Api.Playlists.Count, false));
            Player.Playlists.Detail = (id, _) => Api.Playlists.TryGetValue(id, out var p) ? Task.FromResult(new PlaylistTracks(p.Id, p.Name, p.TrackIds, p.IsComplete, "")) : throw new MusicException(MusicError.Unavailable, "不可访问");
        }
        internal async Task Initialize() { await Player.Initialize(); Library.CaptureSession(); await Library.RefreshLikesAsync(); }
        internal Page Page() => new(this);
        public async ValueTask DisposeAsync() { await Library.DisposeAsync(); Preferences.Dispose(); _dir.Dispose(); await Player.DisposeAsync(); }
    }
    internal sealed class Page : IDisposable
    {
        private readonly MusicLifetime _life = new(); internal PlaylistBrowser Browser { get; } internal PlaylistEditor Editor { get; } internal MusicWorkspace Model { get; } internal MusicView View { get; }
        internal Page(Fixture f)
        {
            var ui = new LoginUiDispatcher(); Browser = new(f.Player.Playlists, f.Player.Player.Sessions, ui, f.Player.Player.Queue); Editor = new(f.Library, f.Player.Playlists, Browser, ui);
            Model = new(f.Player.Player.Catalog, f.Player.Player.Sessions, f.Player.Player.Queue, f.Player.Login, ui, _life, preferences: f.Preferences, playlists: Browser, lyrics: f.Player.Lyrics, persistence: f.Player.Player.Persistence, library: Editor); View = new() { DataContext = Model };
        }
        internal async Task Open(long id, Window w) { await Model.ShowLibraryCommand.ExecuteAsync(null); await Browser.OpenAsync(id); await Settle(w); }
        internal async Task Settle(Window w) { for (var i = 0; i < 8; i++) { Pump(w); await Editor.MetadataLoading; await Task.Yield(); } Pump(w); }
        private bool _closed; public void Dispose() { if (_closed) return; _closed = true; Model.Dispose(); _life.Dispose(); }
    }
}
