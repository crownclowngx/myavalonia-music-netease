namespace MusicNetEasePlugin.Application.Authentication;

/// <summary>UI 调度只负责投递，不让业务网络层依赖 Avalonia；测试使用立即执行或可控队列。</summary>
public interface ILoginUiDispatcher { void Post(Action action); }

/// <summary>独立头像下载端口；没有账号凭据参数，图片错误只影响头像，不改变登录事实。</summary>
public interface IAccountImageSource
{
    /// <summary>当前歌曲封面优先于等待中的歌单缩略图；普通实现可沿用同一下载路径。</summary>
    Task<byte[]?> LoadPriorityAsync(string? address, CancellationToken cancellationToken) => LoadAsync(address, cancellationToken);
    Task<byte[]?> LoadAsync(string? address, CancellationToken cancellationToken);
}
