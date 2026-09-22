namespace MusicNetEasePlugin.Application.Authentication;

public enum LoginStage
{
    SignedOut, Restoring, CreatingQr, WaitingForScan, WaitingForConfirmation, Verifying,
    SignedIn, Expired, Cancelled, Failed, SigningOut
}

/// <summary>只读界面投影。Revision 让 UI 丢弃跨线程迟到通知；二维码凭据只在当前等待期间存在。</summary>
public sealed record LoginSnapshot(
    long Revision, LoginStage Stage, string Message, NeteaseAccount? Account = null,
    byte[]? QrImage = null, bool Remembered = false, bool CleanupRequired = false, LoginMethod Method = LoginMethod.WeChat)
{
    public bool IsBusy => Stage is LoginStage.Restoring or LoginStage.CreatingQr or LoginStage.WaitingForScan
        or LoginStage.WaitingForConfirmation or LoginStage.Verifying or LoginStage.SigningOut;
    public override string ToString() => $"{Stage} (revision {Revision})";
}

/// <summary>客户端策略，不代表网易承诺的二维码寿命；测试可缩短或通过可控时钟推进。</summary>
public sealed record LoginOptions(TimeSpan PollInterval, TimeSpan AttemptBudget, int NetworkRetries = 2)
{
    public static LoginOptions Default { get; } = new(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(3));
}
