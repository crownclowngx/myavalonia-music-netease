using Avalonia.Threading;
using MusicNetEasePlugin.Application.Authentication;

namespace MusicNetEasePlugin.Infrastructure.Ui;

internal sealed class LoginUiDispatcher : ILoginUiDispatcher
{
    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
