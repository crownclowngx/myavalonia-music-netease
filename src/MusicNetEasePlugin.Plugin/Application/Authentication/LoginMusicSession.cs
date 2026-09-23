using MusicNetEasePlugin.Application.Playback;

namespace MusicNetEasePlugin.Application.Authentication;

/// <summary>
/// 已提交会话仍由 LoginCoordinator 独占。此适配只给音乐用例受控入口，不让网络服务读取持久化 Cookie。
/// 会话响应保存复用登录的串行补偿队列，避免音乐保存晚于退出而把已清除的凭据写回来。
/// </summary>
internal sealed class LoginMusicSession(LoginCoordinator login) : IMusicSessionAccessor
{
    public MusicSession Capture() => login.CaptureMusicSession();
    public bool IsCurrent(MusicSession session) => login.IsMusicSessionCurrent(session);
    public Task CommitAsync(MusicSession session, AuthContext context, CancellationToken ct) =>
        login.CommitMusicContextAsync(session, context, ct);
    public Task InvalidateAsync(MusicSession session) => login.InvalidateMusicSessionAsync(session);
}
