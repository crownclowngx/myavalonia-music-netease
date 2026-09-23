using System.Globalization;
using Flurl.Http.Configuration;
using Flurl.Http.Testing;
using MusicNetEasePlugin.Application.Lyrics;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Features.Music;
using MusicNetEasePlugin.Infrastructure.Http;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class LyricsTests
{
    [Fact, Trait("M2", "Y03,Y04,Y05")]
    public void 逐字使用绝对时间且翻译按时间关联不按数组下标()
    {
        var result = LyricParser.Parse(new("[00:01]原逐行\n[00:03]另一句", Translation: "[00:03]后译\n[00:01]先译\n[00:02]未匹配",
            Yrc: "{\"t\":0}\n[1000,1000](1000,200,0)测 (1500,300,0)试\n[3000,400](3000,400,0)下一句"));
        Assert.Equal(new long[] { 1000, 3000 }, result.Lines.Select(line => line.StartMs));
        Assert.Equal("测 试", result.Lines[0].Text); Assert.Equal("先译", result.Lines[0].Translation); Assert.Equal("后译", result.Lines[1].Translation);
        Assert.Contains("未匹配", result.UnmatchedTranslation);
        var words = result.Lines[0].Words!;
        Assert.Equal(new long[] { 1000, 1500 }, words.Select(word => word.StartMs)); Assert.Equal(new long[] { 200, 300 }, words.Select(word => word.DurationMs));
        Assert.Equal(-1, LyricTimeline.FindWord(words, 999)); Assert.Equal(0, LyricTimeline.FindWord(words, 1000));
        Assert.Equal(-1, LyricTimeline.FindWord(words, 1200)); Assert.Equal(1, LyricTimeline.FindWord(words, 1600)); Assert.Equal(-1, LyricTimeline.FindWord(words, 1800));
        var row = new LyricRow(result.Lines[0]) { IsCurrent = true };
        row.UpdatePosition(1600); Assert.Equal("测 ", row.BeforeText); Assert.Equal("试", row.ActiveText);
        row.UpdatePosition(1000); Assert.Equal("测 ", row.ActiveText); Assert.Equal("试", row.AfterText);
        row.IsCurrent = false; row.UpdatePosition(1000); Assert.Empty(row.ActiveText); Assert.Equal("测 试", row.BeforeText);
    }
    [Theory]
    [InlineData("[1000,1000](-1,200,0)负数")]
    [InlineData("[1000,1000](1000,1500,0)越界")]
    [InlineData("[1000,1000](1000,700,0)前(1300,100,0)重叠")]
    [InlineData("[999999999999999999999,1](0,1,0)异常数字")]
    [InlineData("[1000,1000](1000,200,1)未知格式")]
    [Trait("M2", "Y04,Y07")]
    public void 损坏逐字轨道仅降级该轨道(string yrc)
    {
        var result = LyricParser.Parse(new("[00:01]逐行仍在", Translation: "[00:01]翻译仍在", Yrc: yrc));
        Assert.Equal("逐行仍在", result.Lines[0].Text); Assert.Null(result.Lines[0].Words); Assert.Equal("翻译仍在", result.Lines[0].Translation); Assert.Contains("降级", result.Status);
    }
    [Fact, Trait("M2", "Y04,Y07")]
    public void 逐字片段总数受十万上限约束()
    {
        var source = string.Join('\n', Enumerable.Repeat("[0,1]" + string.Concat(Enumerable.Repeat("(0,0,0)", 501)), 200));
        Assert.True(source.Length < 1024 * 1024);
        var result = LyricParser.Parse(new("[00:00]降级", Yrc: source)); Assert.Null(result.Lines.Single().Words); Assert.Contains("降级", result.Status);
    }
    [Fact, Trait("M2", "Y02,Y04")]
    public async Task 新歌词接口不可用时回退逐行且缺轨道不构造逐字()
    {
        using var http = new HttpTest(); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        http.RespondWithJson(new { code = 404 }); http.RespondWithJson(new { code = 200, lrc = new { lyric = "[00:01]逐行" }, tlyric = new { lyric = "[00:01]翻译" } });
        var api = new NeteaseLyricsApi(new(new(clients, TimeProvider.System), sessions));
        var result = LyricParser.Parse(await api.GetAsync(1, sessions.Capture(), default));
        Assert.Equal("翻译", result.Lines[0].Translation); Assert.Null(result.Lines[0].Words); Assert.Equal(2, http.CallLog.Count);
        Assert.Equal("/eapi/song/lyric", new Uri(http.CallLog[1].Request.Url).AbsolutePath);
    }
    [Theory, InlineData("zh-CN"), InlineData("fr-FR"), InlineData("en-US"), Trait("M2", "Y01,Y02")]
    public void 逐行解析独立期望覆盖多标签偏移稳定排序与文化(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new(culture);
            var result = LyricParser.Parse(new("[ti:标题]\n[offset:-500]\n[01:02.34]后句\n[00:03.2][00:01.025]重复\n[00:01.025]同刻\n[00:00]开头\n[00:60]无效\n[bad]无效\n普通文字"));
            Assert.Equal(new long[] { 0, 525, 525, 2700, 61840 }, result.Lines.Select(l => l.StartMs));
            Assert.Equal(new[] { "开头", "重复", "同刻", "重复", "后句" }, result.Lines.Select(l => l.Text));
            Assert.Equal("普通文字", result.PlainText); Assert.Equal(2, LyricTimeline.FindLine(result.Lines, 525));
            Assert.Equal(0, LyricTimeline.FindLine(result.Lines, 1)); Assert.Equal(-1, LyricTimeline.FindLine(result.Lines, -1));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact, Trait("M2", "Y02,Y07")]
    public void 缺失纯音乐纯文本与解析上限有明确状态()
    {
        Assert.Contains("暂无", LyricParser.Parse(new(null)).Status);
        Assert.Contains("纯音乐", LyricParser.Parse(new(null, true)).Status);
        Assert.Contains("纯文本", LyricParser.Parse(new("只有文字")).Status);
        Assert.Empty(LyricParser.Parse(new(new string('x', 1024 * 1024 + 1))).Lines);
        var many = LyricParser.Parse(new(string.Concat(Enumerable.Repeat("[00:01][00:02]重复\n", 10000))));
        Assert.Equal(10000, many.Lines.Count); Assert.Contains("上限", many.Status);
    }
    [Fact, Trait("M2", "P01,Y02")]
    public async Task 生产歌词适配读取轨道与空歌词并复用受控协议()
    {
        using var http = new HttpTest(); using var clients = new NeteaseFlurlClients(new FlurlClientCache()); using var sessions = new MusicSessions();
        var api = new NeteaseLyricsApi(new(new(clients, TimeProvider.System), sessions));
        http.RespondWithJson(new { code = 200, lrc = new { lyric = "[00:01]原文" } });
        Assert.Equal("[00:01]原文", (await api.GetAsync(1, sessions.Capture(), default)).Lrc);
        Assert.Equal("/eapi/song/lyric/v1", new Uri(http.CallLog[0].Request.Url).AbsolutePath);
        http.RespondWithJson(new { code = 200, nolyric = true }); Assert.True((await api.GetAsync(2, sessions.Capture(), default)).Instrumental);
        Assert.Equal(2, sessions.Commits);
    }
    [Fact, Trait("M2", "Y03,Y05,Y06,Y07,A02")]
    public async Task 歌词迟到不得串歌并按真实进度定位暂停与退出清理()
    {
        await using var f = new PlaybackFixture(); var api = new LyricsFake();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var late = new TaskCompletionSource<RawLyrics>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Get = (id, _) => { if (id == 1) { entered.TrySetResult(); return late.Task; } return Task.FromResult(new RawLyrics("[00:01]第一句\n[00:03]第二句")); };
        await using var lyrics = new LyricsCoordinator(f.Queue, f.Sessions, api); using var view = new LyricsWorkspace(lyrics, new ImmediateUi());
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(1), default); await entered.Task;
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(2), default); late.SetResult(new("[00:01]迟到")); await lyrics.Pending;
        Assert.Equal(2, lyrics.Snapshot.TrackId); Assert.Equal("第一句", lyrics.Snapshot.Document.Lines[0].Text);
        var current = f.Queue.Snapshot; f.Audio.Emit(current.Playback.Generation, PlaybackState.Playing, 3500);
        Assert.Equal(1, lyrics.Snapshot.CurrentLine); view.StopFollowing(); Assert.False(view.Following);
        await f.Queue.PauseAsync(true, default); await f.Queue.SeekAsync(current.CurrentEntryId!.Value, current.Playback.Generation, 1000, default);
        Assert.Equal(0, view.CurrentLine); Assert.True(view.Lines[0].IsCurrent); Assert.Equal(PlaybackState.Paused, f.Queue.Snapshot.Playback.State);
        view.FollowCommand.Execute(null); Assert.True(view.Following);
        f.Sessions.Revoke(); Assert.Empty(view.Lines); Assert.Equal(0, lyrics.CachedCount); Assert.Equal(0, lyrics.Snapshot.TrackId);
    }
    [Fact, Trait("M2", "Y02,Y05,Y07")]
    public async Task 缓存最多二十首且失败仅手动重试试听不猜同步偏移()
    {
        await using var f = new PlaybackFixture(); var api = new LyricsFake();
        await using var lyrics = new LyricsCoordinator(f.Queue, f.Sessions, api);
        for (var id = 1; id <= 22; id++) { await f.Queue.PlaySingleAsync(MusicCatalog.Track(id), default); await lyrics.Pending; }
        Assert.Equal(20, lyrics.CachedCount);
        api.Get = (_, _) => throw new MusicException(MusicError.Network, "失败");
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(30), default); await lyrics.Pending;
        Assert.True(lyrics.Snapshot.Failed); Assert.Equal(PlaybackState.Playing, f.Queue.Snapshot.Playback.State);
        api.Get = (_, _) => Task.FromResult(new RawLyrics("[00:00]歌词")); await lyrics.RetryAsync(); Assert.False(lyrics.Snapshot.Failed);
        f.Catalog.Resource = (id, _) => Task.FromResult(new PlaybackResource(id, new("https://m1.music.126.net/fixture"), "mp3", "standard", true, 30000, 40000));
        await f.Queue.PlaySingleAsync(MusicCatalog.Track(31), default); await lyrics.Pending;
        Assert.True(lyrics.Snapshot.SyncLimited); Assert.Equal(-1, lyrics.Snapshot.CurrentLine);
    }
    internal sealed class LyricsFake : ILyricsApi
    {
        internal Func<long, CancellationToken, Task<RawLyrics>> Get { get; set; } = (_, _) => Task.FromResult(new RawLyrics("[00:01]歌词"));
        public Task<RawLyrics> GetAsync(long trackId, MusicSession session, CancellationToken ct) => Get(trackId, ct);
    }
}
