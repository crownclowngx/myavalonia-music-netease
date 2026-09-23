using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Application.Playback;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class MusicSessionTests
{
    [Fact, Trait("M1", "A01,A05,A06")]
    public async Task 仅核验通过的账号可取快照且保存失败仍可播放()
    {
        var store = new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()), FailSave = true };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeAuthApi { Account = async (ctx, _) => { entered.SetResult(); await release.Task; return new(new(123, "账号", null), ctx); } };
        await using var login = TestLogin.Create(api, store, TimeProvider.System, LoginOptions.Default);
        Assert.Throws<MusicException>(login.CaptureMusicSession);
        var restore = login.RestoreAsync(Guid.NewGuid(), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Throws<MusicException>(login.CaptureMusicSession);
        release.SetResult(); await restore;
        var session = login.CaptureMusicSession();
        Assert.True(login.IsMusicSessionCurrent(session));
        Assert.False(login.Snapshot.Remembered);
        store.FailSave = false;
        await login.RetrySaveAsync(Guid.NewGuid(), default);
        Assert.True(login.Snapshot.Remembered);
        Assert.Equal(session.Epoch, login.CaptureMusicSession().Epoch);
        Assert.False(session.Revoked.IsCancellationRequested);
        api.Account = (_, _) => throw new AuthException(AuthError.Network, "暂时断网");
        await login.RestoreAsync(Guid.NewGuid(), default, true);
        Assert.True(login.IsMusicSessionCurrent(session));
        api.Account = (_, _) => throw new AuthException(AuthError.SessionExpired, "会话失效");
        await login.RestoreAsync(Guid.NewGuid(), default, true);
        // 已有核验账号时恢复入口是幂等操作，不再次请求；音乐端点明确失效才撤销当前账号。
        Assert.Equal(1, api.Accounts);
        Assert.True(login.IsMusicSessionCurrent(session));
        await login.InvalidateMusicSessionAsync(session);
        Assert.True(session.Revoked.IsCancellationRequested);
        Assert.Throws<MusicException>(login.CaptureMusicSession);
    }

    [Fact, Trait("M1", "A02,A04,A05,A06")]
    public async Task 凭据版本拒绝旧更新且同账号重登拒绝旧失效响应()
    {
        var store = new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()) };
        await using var login = TestLogin.Create(new(), store, TimeProvider.System, LoginOptions.Default);
        await login.RestoreAsync(Guid.NewGuid(), default);
        var old = login.CaptureMusicSession();
        var fresh = old.Context with { Cookies = old.Context.Cookies.SetItem("MUSIC_U", new("new-cookie")) };
        await login.CommitMusicContextAsync(old, fresh, default);
        await login.CommitMusicContextAsync(old, old.Context with { Cookies = old.Context.Cookies.SetItem("MUSIC_U", new("stale-cookie")) }, default);
        Assert.Equal("new-cookie", login.CaptureMusicSession().Context.Cookies["MUSIC_U"].Value);
        await login.LogoutAsync(Guid.NewGuid(), default);
        Assert.True(old.Revoked.IsCancellationRequested);
        store.Saved = FakeAuthApi.Authorized(AuthContext.Create());
        await login.RestoreAsync(Guid.NewGuid(), default, true);
        var next = login.CaptureMusicSession();
        Assert.NotEqual(old.Epoch, next.Epoch);
        await login.InvalidateMusicSessionAsync(old);
        Assert.True(login.IsMusicSessionCurrent(next));
        await login.InvalidateMusicSessionAsync(next);
        Assert.Null(store.Saved);
        Assert.True(next.Revoked.IsCancellationRequested);
        Assert.Throws<MusicException>(login.CaptureMusicSession);
    }

    [Fact, Trait("M1", "A04,A06")]
    public async Task 音乐响应保存已提交但尚未返回时退出仍最终清除会话()
    {
        var store = new MemorySessionStore { Saved = FakeAuthApi.Authorized(AuthContext.Create()) };
        await using var login = TestLogin.Create(new(), store, TimeProvider.System, LoginOptions.Default);
        await login.RestoreAsync(Guid.NewGuid(), default);
        var request = login.CaptureMusicSession();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.AfterCommit = async () => { saved.SetResult(); await release.Task; };
        var commit = login.CommitMusicContextAsync(request, request.Context with { Cookies = request.Context.Cookies.SetItem("MUSIC_U", new("fresh")) }, default);
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var logout = login.LogoutAsync(Guid.NewGuid(), default);
        Assert.True(request.Revoked.IsCancellationRequested);
        release.SetResult();
        await Task.WhenAll(commit, logout).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(store.Saved);
        Assert.Null(login.Snapshot.Account);
    }
}
