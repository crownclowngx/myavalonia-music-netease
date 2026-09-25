using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Music;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class SearchDocumentTests
{
    [Fact, Trait("M1", "C01,C02,C03,C06,U01,U04")]
    public async Task 搜索只投影最新响应并在失败后保留原页允许重试()
    {
        await using var f = new PlaybackFixture(); await using var login = TestLogin.Create(new(), new(), TimeProvider.System, LoginOptions.Default);
        using var lifetime = new MusicLifetime(); using var document = new MusicWorkspace(f.Catalog, f.Sessions, f.Queue, login, new ImmediateUi(), lifetime);
        var old = new TaskCompletionSource<MusicSearchPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<(string Keyword, int Offset)>();
        f.Catalog.Search = (keyword, offset, _) =>
        {
            requests.Add((keyword, offset));
            if (keyword == "old") return old.Task;
            if (keyword == "error") throw new MusicException(MusicError.Network, "请求失败");
            return Task.FromResult(new MusicSearchPage(offset == 0 ? [MusicCatalog.Track(2)] : [], offset, offset == 0));
        };
        await document.SearchAsync(0, "  "); Assert.Empty(requests);
        var a = document.SearchAsync(0, "old"); await document.SearchAsync(0, "new");
        old.SetResult(new([MusicCatalog.Track(1)], 0, true)); await a;
        Assert.Equal(2, Assert.Single(document.Tracks).Id); Assert.False(document.IsLoading);
        await document.SearchAsync(0, "error");
        Assert.Equal(2, Assert.Single(document.Tracks).Id); Assert.Contains("失败", document.SearchMessage); Assert.False(document.IsLoading);
        document.Keyword = "尚未提交的新草稿";
        await document.NextSearchPageCommand.ExecuteAsync(null);
        Assert.Equal(("new", 30), requests[^1]); Assert.Empty(document.Tracks); Assert.False(document.HasMore); Assert.Equal("第 2 页", document.PageText);
        await document.PreviousSearchPageCommand.ExecuteAsync(null); Assert.Equal(("new", 0), requests[^1]);
    }

    [Fact, Trait("M1", "L09,U04,U05,U06")]
    public async Task 多文档共用队列且只有最后页面关闭才释放播放资源()
    {
        await using var f = new PlaybackFixture(); await using var login = TestLogin.Create(new(), new(), TimeProvider.System, LoginOptions.Default);
        using var lifeA = new MusicLifetime(); using var lifeB = new MusicLifetime();
        await using var playbackLifetime = new MusicPlaybackLifetime(f.Queue);
        using var leaseA = new MusicPlaybackLease(playbackLifetime, lifeA);
        using var leaseB = new MusicPlaybackLease(playbackLifetime, lifeB);
        using var a = new MusicWorkspace(f.Catalog, f.Sessions, f.Queue, login, new ImmediateUi(), lifeA, playbackLease: leaseA);
        using var b = new MusicWorkspace(f.Catalog, f.Sessions, f.Queue, login, new ImmediateUi(), lifeB, playbackLease: leaseB);
        a.SelectedTrack = MusicCatalog.Track(1); await a.PlayCommand.ExecuteAsync(null);
        Assert.Equal(a.Player.CurrentTrack, b.Player.CurrentTrack);
        lifeB.Close(); b.Dispose(); Assert.Equal(PlaybackState.Playing, f.Player.Snapshot.State);
        lifeA.Close(); a.Dispose(); Assert.Equal(PlaybackState.Stopped, f.Player.Snapshot.State);
        Assert.Null(f.Audio.Current); Assert.Equal(1, f.Audio.SessionReleases);
        using var lifeC = new MusicLifetime();
        using var leaseC = new MusicPlaybackLease(playbackLifetime, lifeC);
        using var c = new MusicWorkspace(f.Catalog, f.Sessions, f.Queue, login, new ImmediateUi(), lifeC, playbackLease: leaseC);
        Assert.Contains("歌曲1", c.Player.CurrentTrack); Assert.Single(f.Audio.Opened);
        Assert.Equal(PlaybackState.Stopped, f.Queue.Snapshot.Playback.State);
        await f.Queue.PauseAsync(false, default); Assert.Equal(2, f.Audio.Opened.Count);
        await f.Queue.StopAsync(); Assert.Null(f.Audio.Current);
        f.Audio.Emit(f.Audio.Opened.First().Generation, PlaybackState.Playing);
        Assert.Equal(PlaybackState.Stopped, f.Player.Snapshot.State);
    }

    [Fact, Trait("M1", "U03,C05")]
    public async Task 迟到封面不能覆盖新歌曲且失败保留占位()
    {
        await using var f = new PlaybackFixture(); await using var login = TestLogin.Create(new(), new(), TimeProvider.System, LoginOptions.Default);
        using var lifetime = new MusicLifetime(); var images = new CoverImages();
        using var document = new MusicWorkspace(f.Catalog, f.Sessions, f.Queue, login, new ImmediateUi(), lifetime, images);
        document.Player.SetVisible(true);
        f.Catalog.Detail = (id, _) => Task.FromResult(MusicCatalog.Track(id) with { Cover = id.ToString() });
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default); await images.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        document.Player.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(document.Player.CoverBytes) && document.Player.CoverBytes is [2]) applied.TrySetResult(); };
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(2), default);
        images.Old.SetResult([1]); await applied.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new byte[] { 2 }, document.Player.CoverBytes);
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(3), default); Assert.Null(document.Player.CoverBytes); Assert.Equal(PlaybackState.Playing, f.Player.Snapshot.State);
    }
    private sealed class CoverImages : IAccountImageSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<byte[]?> Old { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<byte[]?> LoadAsync(string? address, CancellationToken ct)
        {
            if (address == "1") { Entered.SetResult(); return Old.Task; }
            if (address == "3") throw new IOException("损坏图片");
            return Task.FromResult<byte[]?>([2]);
        }
    }
}
