using System.Net;
using System.Text;
using Flurl.Http.Configuration;
using MusicNetEasePlugin.Application.Playback;
using MusicNetEasePlugin.Infrastructure.Http;
using MusicNetEasePlugin.Infrastructure.Media;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class MediaBufferTests
{
    private static PlaybackResource Resource(string url = "https://m1.music.126.net/file") => new(1, new Uri(url), "mp3", "standard", false, null);
    private static byte[] Audio(int size = 64) { var bytes = new byte[size]; "ID3"u8.CopyTo(bytes); return bytes; }
    private static NeteaseFlurlClients Clients(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new FlurlClientCache().Add("netease-media", configure: b => b.AddMiddleware(() => new ScriptedHandler(handler))));
    private static void Empty(TestDirectory directory) => Assert.DoesNotContain(Directory.EnumerateFiles(directory.Path, "*", SearchOption.AllDirectories), p => Path.GetFileName(p) != ".owner");

    [Theory]
    [InlineData("https://music.126.net.evil.test/file")]
    [InlineData("https://127.0.0.1/file")]
    [InlineData("file:///c:/fixture")]
    [InlineData("https://user:secret@m1.music.126.net/file")]
    [InlineData("https://m1.music.126.net:8443/file")]
    [Trait("M1", "B01")]
    public void 非许可源在请求前拒绝(string url) => Assert.Throws<MusicException>(() => FlurlMediaBuffer.ValidateAddress(new Uri(url)));

    [Fact, Trait("M1", "B01,B06,T09")]
    public async Task 合法重定向逐跳无凭据且临时文件归句柄所有()
    {
        using var directory = new TestDirectory();
        var count = 0;
        using var clients = Clients((request, _) =>
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.False(request.Headers.Contains("Cookie")); Assert.False(request.Headers.Contains("x-music-u"));
            count++;
            var response = count == 1 ? new HttpResponseMessage(HttpStatusCode.Found) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(Audio()) };
            if (count == 1) response.Headers.Location = new Uri("https://m2.music.126.net/file");
            return Task.FromResult(response);
        });
        using var buffer = new FlurlMediaBuffer(clients, directory.Path, MediaLimits.Default);
        using var media = await buffer.DownloadAsync(Resource("http://m1.music.126.net/file"), default);
        Assert.Equal(2, count); Assert.True(File.Exists(media.Path));
        using (File.Open(media.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        media.Dispose(); media.Dispose(); Empty(directory);
    }

    [Theory, InlineData(false), InlineData(true)]
    [Trait("M1", "B01")]
    public async Task 越界重定向与循环均有界失败(bool loop)
    {
        using var directory = new TestDirectory(); var count = 0;
        using var clients = Clients((_, _) =>
        {
            count++; var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(loop ? "https://m1.music.126.net/again" : "https://external.test/secret");
            return Task.FromResult(response);
        });
        using var buffer = new FlurlMediaBuffer(clients, directory.Path, MediaLimits.Default);
        await Assert.ThrowsAsync<MusicException>(() => buffer.DownloadAsync(Resource(), default));
        Assert.Equal(loop ? 4 : 1, count); Empty(directory);
    }

    [Theory, InlineData("declared"), InlineData("actual"), InlineData("truncated"), InlineData("html")]
    [Trait("M1", "B02,B05")]
    public async Task 长度不可信和非媒体响应不能发布半成品(string kind)
    {
        using var directory = new TestDirectory();
        var stream = new ChunkStream(kind == "html" ? Encoding.UTF8.GetBytes("<html>failure</html>") : Audio(65));
        using var clients = Clients((_, _) =>
        {
            var content = new StreamContent(stream);
            if (kind == "declared") content.Headers.ContentLength = 1000;
            if (kind == "truncated") content.Headers.ContentLength = 100;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var buffer = new FlurlMediaBuffer(clients, directory.Path, MediaLimits.Default with { MaximumBytes = kind == "truncated" ? 200 : 64 });
        await Assert.ThrowsAsync<MusicException>(() => buffer.DownloadAsync(Resource(), default));
        Assert.True(stream.Disposed); Empty(directory);
    }

    [Theory, InlineData("header"), InlineData("idle"), InlineData("total"), InlineData("cancel")]
    [Trait("M1", "B03,B04")]
    public async Task 各阶段超时与主动取消都会关闭响应及清理文件(string mode)
    {
        using var directory = new TestDirectory(); using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new ChunkStream(Audio()) { Waiting = true, Entered = entered };
        using var clients = Clients(async (_, ct) =>
        {
            if (mode == "header") { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            return new(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        });
        var shortBudget = TimeSpan.FromMilliseconds(60);
        using var buffer = new FlurlMediaBuffer(clients, directory.Path, new(1024,
            mode == "header" ? shortBudget : TimeSpan.FromSeconds(5), mode == "idle" ? shortBudget : TimeSpan.FromSeconds(5),
            mode == "total" ? shortBudget : TimeSpan.FromSeconds(5)));
        var work = buffer.DownloadAsync(Resource(), cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (mode == "cancel") { cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(3))); }
        else Assert.Equal(MusicError.Timeout, (await Assert.ThrowsAsync<MusicException>(() => work.WaitAsync(TimeSpan.FromSeconds(3)))).Kind);
        if (mode != "header") Assert.True(stream.Disposed);
        Empty(directory);
    }

    [Fact, Trait("M1", "B05,B06")]
    public async Task 目录写入失败安全分类且残留回收不影响活动实例和无关目录()
    {
        using var directory = new TestDirectory();
        using var clients = Clients((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Audio()) }));
        var blocked = Path.Combine(directory.Path, "occupied"); File.WriteAllText(blocked, "occupied");
        using (var buffer = new FlurlMediaBuffer(clients, blocked, MediaLimits.Default))
            Assert.Equal(MusicError.Storage, (await Assert.ThrowsAsync<MusicException>(() => buffer.DownloadAsync(Resource(), default))).Kind);
        var old = Path.Combine(directory.Path, "instance-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, ".owner"), ""); File.WriteAllText(Path.Combine(old, "left.partial"), "partial");
        var unrelated = Path.Combine(directory.Path, "instance-external"); Directory.CreateDirectory(unrelated); File.WriteAllText(Path.Combine(unrelated, "keep"), "keep");
        using var first = new FlurlMediaBuffer(clients, directory.Path, MediaLimits.Default);
        using var a = await first.DownloadAsync(Resource(), default);
        Assert.False(Directory.Exists(old));
        using var second = new FlurlMediaBuffer(clients, directory.Path, MediaLimits.Default);
        using var b = await second.DownloadAsync(Resource(), default);
        Assert.True(File.Exists(a.Path)); Assert.True(File.Exists(b.Path)); Assert.True(File.Exists(Path.Combine(unrelated, "keep")));
    }

    [Theory, InlineData(403, MusicError.Network), InlineData(410, MusicError.AddressExpired)]
    [Trait("M1", "B07")]
    public async Task 普通拒绝不同于明确过期(int code, MusicError expected)
    {
        using var directory = new TestDirectory();
        using var clients = Clients((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("secret-url") }));
        using var buffer = new FlurlMediaBuffer(clients, directory.Path, MediaLimits.Default);
        var error = await Assert.ThrowsAsync<MusicException>(() => buffer.DownloadAsync(Resource(), default));
        Assert.Equal(expected, error.Kind); Assert.DoesNotContain("secret", error.ToString()); Empty(directory);
    }

    private sealed class ChunkStream(byte[] content) : Stream
    {
        private int _offset;
        public bool Waiting { get; init; }
        public TaskCompletionSource? Entered { get; init; }
        public bool Disposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (Waiting) { Entered!.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            var count = Math.Min(Math.Min(buffer.Length, 17), content.Length - _offset);
            content.AsMemory(_offset, count).CopyTo(buffer); _offset += count; return count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
    }
}
