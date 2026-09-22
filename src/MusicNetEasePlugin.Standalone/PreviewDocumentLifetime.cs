using MyAvaloniaManagement.PluginSdk;

namespace MusicNetEasePlugin.Standalone;

/// <summary>独立预览实现相同的只读关闭契约，业务代码无需识别是否运行在 Host 中。</summary>
internal sealed class PreviewDocumentLifetime : IDocumentLifetime, IDisposable
{
    private readonly CancellationTokenSource _closing = new();
    public CancellationToken ClosingToken => _closing.Token;
    public bool IsClosing => _closing.IsCancellationRequested;
    public void Close() => _closing.Cancel();
    public void Dispose() => _closing.Dispose();
}
