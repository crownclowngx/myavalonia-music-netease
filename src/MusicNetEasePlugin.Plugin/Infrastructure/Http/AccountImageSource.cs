using Flurl.Http;
using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Infrastructure.Http;

internal sealed class AccountImageSource(NeteaseFlurlClients clients) : IAccountImageSource
{
    public async Task<byte[]?> LoadAsync(string? address, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") ||
            !(uri.Host.EndsWith(".music.126.net", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".music.163.com", StringComparison.OrdinalIgnoreCase)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return null;
        // 账号资料可能返回旧的 http 图片地址，限定网易图片域后升级 HTTPS，不向图片域携带账号 Cookie。
        var secure = new UriBuilder(uri) { Scheme = "https", Port = -1 };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await clients.Images.Request(secure.Uri).SetQueryParam("param", "160y160")
                .AllowAnyHttpStatus().GetAsync(completionOption: HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken: cancellation.Token).ConfigureAwait(false);
            if (response.StatusCode != 200) return null;
            await using var stream = await response.GetStreamAsync().ConfigureAwait(false);
            return await NeteaseTransport.ReadBoundedAsync(stream, 2 * 1024 * 1024, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (FlurlHttpException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (Exception ex) when (ex is FlurlHttpException or OperationCanceledException or AuthException or IOException) { return null; }
    }
}
