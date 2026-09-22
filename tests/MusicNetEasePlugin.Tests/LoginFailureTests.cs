using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;
using MusicNetEasePlugin.Application.Authentication;
using Xunit;

namespace MusicNetEasePlugin.Tests;

public sealed class LoginFailureTests
{
    // 等待定时器实际登记再推进时间，避免靠 sleep 或“已显示状态”推断后台已进入 Delay。
    private sealed class Clock : TimeProvider
    {
        private readonly FakeTimeProvider _inner = new();
        private readonly Channel<TimeSpan> _scheduled = Channel.CreateUnbounded<TimeSpan>();
        public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _inner.CreateTimer(callback, state, dueTime, period);
            if (dueTime < LoginOptions.Default.AttemptBudget) _scheduled.Writer.TryWrite(dueTime);
            return timer;
        }
        public async Task AdvanceNext(double expectedSeconds)
        {
            var interval = await _scheduled.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), interval);
            _inner.Advance(interval);
        }
    }

    [Theory]
    [InlineData(AuthError.Network, null)]
    [InlineData(AuthError.RateLimited, 7)]
    [Trait("Scenario", "H05,H06,L10")]
    public async Task 轮询遵守退避并在预算耗尽后停止(AuthError error, int? retrySeconds)
    {
        var clock = new Clock();
        var api = new FakeAuthApi { Check = (_, _, _) => throw new AuthException(error, "fixture", retryAfter: retrySeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null) };
        await using var login = TestLogin.Create(api, new MemorySessionStore(), clock, LoginOptions.Default);
        var run = login.StartAsync(Guid.NewGuid(), default);
        await clock.AdvanceNext(2);
        await clock.AdvanceNext(retrySeconds ?? 2);
        await clock.AdvanceNext(2);
        await clock.AdvanceNext(retrySeconds ?? 4);
        await clock.AdvanceNext(2);
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(3, api.Checks);
        Assert.Equal(1, api.Keys);
        Assert.Equal(LoginStage.Failed, login.Snapshot.Stage);
    }

    [Theory]
    [InlineData(AuthError.VerificationRequired)]
    [InlineData(AuthError.Protocol)]
    [InlineData(AuthError.RateLimited)]
    [Trait("Scenario", "H05,H06,L10")]
    public async Task 未知状态或验证要求不自动重试(AuthError error)
    {
        var clock = new Clock();
        var api = new FakeAuthApi { Check = (_, _, _) => throw new AuthException(error, "fixture") };
        await using var login = TestLogin.Create(api, new MemorySessionStore(), clock, LoginOptions.Default);
        var run = login.StartAsync(Guid.NewGuid(), default);
        await clock.AdvanceNext(2);
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, api.Checks);
        Assert.Equal(LoginStage.Failed, login.Snapshot.Stage);
    }

    [Fact]
    [Trait("Scenario", "L04,L08,S08")]
    public async Task 账号检查中退出后迟到账号不能被保存且取消的退出仍清理磁盘()
    {
        var entered = new TaskCompletionSource<AuthContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AccountCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeAuthApi { Account = (context, _) => { entered.TrySetResult(context); return release.Task; } };
        var clock = new Clock();
        var store = new MemorySessionStore();
        await using var login = TestLogin.Create(api, store, clock, LoginOptions.Default);
        var run = login.StartAsync(Guid.NewGuid(), default);
        await clock.AdvanceNext(2);
        var context = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var logout = login.LogoutAsync(Guid.NewGuid(), cancellation.Token);
        release.SetResult(new(new(123, "late", null), context));
        await Task.WhenAll(run, logout).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(login.Snapshot.Account);
        Assert.Null(store.Saved);
        Assert.Equal(0, store.Saves);
        Assert.Equal(1, store.Clears);
        Assert.False(login.Snapshot.CleanupRequired);
    }
}
