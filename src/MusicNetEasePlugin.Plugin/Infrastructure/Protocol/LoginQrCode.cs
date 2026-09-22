using QRCoder;

namespace MusicNetEasePlugin.Infrastructure.Protocol;

/// <summary>二维码完全本地生成；使用 URI 编码一次，二维码 key 不进入日志或账号保存文件。</summary>
internal static class LoginQrCode
{
    internal static string Url(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 2048) throw new ArgumentException("二维码凭据过长。", nameof(key));
        return "https://music.163.com/login?codekey=" + Uri.EscapeDataString(key);
    }

    internal static byte[] Render(string key)
    {
        using var data = QRCodeGenerator.GenerateQrCode(Url(key), QRCodeGenerator.ECCLevel.M);
        using var code = new PngByteQRCode(data);
        return code.GetGraphic(8);
    }
}
