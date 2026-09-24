using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using MusicNetEasePlugin.Application.Library;

namespace MusicNetEasePlugin.Infrastructure.Http;

/// <summary>
/// 仅在用户收藏操作时短暂加载网易官方 Watchman SDK，取得一次性令牌后关闭控制器。
/// 设计思路：官方 SDK 需要真正的浏览器环境，使用系统 WebView2，避免在 C# 中伪造令牌或引入 Node 服务。
/// 浏览器使用专用隔离目录和 InPrivate 配置，不读取用户浏览器、Cookie 或登录会话；没有常驻轮询。
/// 创建与释放均在 Avalonia 的 STA UI 消息循环执行，业务调用方不阻塞 UI 线程。
/// </summary>
internal sealed class WebViewLibraryCheckTokenProvider : ILibraryCheckTokenProvider
{
    private const string DocumentUrl = "https://music.163.com/__music_library_token__";
    private readonly string _directory;
    public WebViewLibraryCheckTokenProvider(string root) => _directory = Path.Combine(root, "library-browser");

    public Task<LibraryCheckToken> GetAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new LibraryReadException(LibraryReadError.Restricted, "收藏令牌当前需要 Windows WebView2 运行库。");
        var completion = new TaskCompletionSource<LibraryCheckToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try { completion.TrySetResult(await AcquireAsync(ct)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(ct); }
            catch (Exception)
            {
                // COM、SDK 或网页错误可能包含令牌与网页细节，统一转为可行动的安全消息。
                completion.TrySetException(new LibraryReadException(LibraryReadError.Restricted,
                    "无法取得收藏令牌。请确认已安装 Microsoft Edge WebView2 运行库且能访问网易；可稍后重试。"));
            }
        });
        return completion.Task;
    }
    private async Task<LibraryCheckToken> AcquireAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var result = new TaskCompletionSource<LibraryCheckToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = timeout.Token.Register(() => result.TrySetCanceled(timeout.Token));
        var handle = CreateWindowExW(0, "STATIC", "NetEase library token", 0, 0, 0, 320, 240, 0, 0, 0, 0);
        if (handle == 0) throw new InvalidOperationException("无法创建令牌宿主。");
        CoreWebView2Controller? controller = null;
        try
        {
            var environmentTask = CoreWebView2Environment.CreateAsync(null, _directory);
            // WebView 创建 API 没有取消参数。若取消先发生，迟到的 controller 仍在 UI 线程关闭，不能遗留隐藏窗口。
            var environment = await environmentTask.WaitAsync(timeout.Token);
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = "LibraryToken"; options.IsInPrivateModeEnabled = true;
            var controllerTask = environment.CreateCoreWebView2ControllerAsync(handle, options);
            try { controller = await controllerTask.WaitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                var orphanHandle = handle; handle = 0;
                _ = CloseLateAsync(controllerTask, orphanHandle);
                throw;
            }
            controller.IsVisible = false;
            var core = controller.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false; core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.AddWebResourceRequestedFilter(DocumentUrl, CoreWebView2WebResourceContext.Document);
            core.WebResourceRequested += (_, e) =>
            {
                if (e.Request.Uri == DocumentUrl)
                    e.Response = environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(Html)), 200, "OK", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
            };
            core.NavigationStarting += (_, e) => { if (e.Uri != DocumentUrl) e.Cancel = true; };
            core.WebMessageReceived += (_, e) =>
            {
                if (e.Source != DocumentUrl) return;
                try
                {
                    using var json = JsonDocument.Parse(e.WebMessageAsJson);
                    if (json.RootElement.TryGetProperty("token", out var value) && value.ValueKind == JsonValueKind.String &&
                        value.GetString() is { Length: > 0 and <= 8192 } token) result.TrySetResult(new(token));
                    else result.TrySetException(new InvalidOperationException("令牌未生成。"));
                }
                catch (JsonException) { result.TrySetException(new InvalidOperationException("令牌响应无效。")); }
            };
            core.Navigate(DocumentUrl);
            return await result.Task;
        }
        finally { controller?.Close(); if (handle != 0) DestroyWindow(handle); }
    }
    private static async Task CloseLateAsync(Task<CoreWebView2Controller> pending, nint handle)
    {
        try { (await pending).Close(); }
        catch (Exception) { /* 创建失败仍需归还隐藏宿主，不能把 COM 异常变成未观察任务。 */ }
        finally { DestroyWindow(handle); }
    }
    // SDK 脚本只在令牌页面按需从网易域加载；不更改 webdriver、语言或插件指纹，也不携带用户凭据。
    internal const string Html = """
        <!doctype html><html><head><meta charset="utf-8"></head><body>
        <script src="https://acstatic-dun.126.net/tool.min.js"></script>
        <script>
        (() => {
          let done = false;
          const finish = token => { if (!done) { done = true; chrome.webview.postMessage({token: token || ''}); } };
          setTimeout(() => finish(''), 40000);
          try {
            initWatchman({auto: true, productNumber: 'YD00000558929251',
              onload(instance) {
                let started = false;
                const acquire = () => { if (started || done) return; started = true;
                  instance.getToken('bd5d2f973ef74cd2a61325a412ae54d9', token => finish(typeof token === 'string' ? token : '')); };
                instance.getInstance().I(acquire); setTimeout(acquire, 15000);
              }, onerror() { finish(''); }
            });
          } catch { finish(''); }
        })();
        </script></body></html>
        """;
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint handle);
}
