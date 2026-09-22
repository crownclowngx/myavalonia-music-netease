namespace MusicNetEasePlugin.Application.Authentication;

public enum LoginMethod { WeChat, NeteaseApp }

/// <summary>
/// 两种扫码协议的共同边界仅包含创建与状态查询。账号核验、保存、退出仍由原有用例统一处理。
/// Provider 本身可复用；每次 Create 返回独占的尝试，不能在单例中保存二维码、授权码或临时 Cookie。
/// </summary>
public interface IQrLoginProvider
{
    LoginMethod Method { get; }
    Task<IQrLoginAttempt> CreateAsync(AuthContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 一次扫码尝试拥有图片与临时授权上下文。调用者串行查询，取消并等待在途请求完成后再释放。
/// 返回 Authorized 前必须完成该平台回调并取得网易会话；微信确认本身不等于网易登录完成。
/// </summary>
public interface IQrLoginAttempt : IDisposable
{
    byte[] Image { get; }
    Task<QrCheck> CheckAsync(CancellationToken cancellationToken);
}
