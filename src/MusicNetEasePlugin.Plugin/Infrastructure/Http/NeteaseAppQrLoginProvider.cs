using MusicNetEasePlugin.Application.Authentication;
using MusicNetEasePlugin.Infrastructure.Protocol;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>保留原网易云 App 扫码协议；图片在创建阶段生成，View 不再接触任何协议 key。</summary>
internal sealed class NeteaseAppQrLoginProvider(INeteaseAuthApi api) : IQrLoginProvider
{
    public LoginMethod Method => LoginMethod.NeteaseApp;

    public async Task<IQrLoginAttempt> CreateAsync(AuthContext context, CancellationToken cancellationToken)
    {
        var key = await api.CreateKeyAsync(context, cancellationToken).ConfigureAwait(false);
        return new Attempt(api, key);
    }

    private sealed class Attempt(INeteaseAuthApi api, QrKey key) : IQrLoginAttempt
    {
        private AuthContext _context = key.Context;
        private bool _disposed;
        public byte[] Image { get; } = LoginQrCode.Render(key.Key);
        public async Task<QrCheck> CheckAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var result = await api.CheckQrAsync(key.Key, _context, cancellationToken).ConfigureAwait(false);
            _context = result.Context;
            return result;
        }
        public void Dispose() { _disposed = true; _context = _context with { Cookies = _context.Cookies.Clear() }; }
        public override string ToString() => "[网易云 App 扫码尝试]";
    }
}
