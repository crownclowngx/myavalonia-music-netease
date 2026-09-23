# 微信默认登录实现与验证记录（2026-09-22）

> 后续状态：V1–V4 已于 2026-09-23 根据用户确认完成手工验收，见[验收收口记录](../netease-v1-v4-acceptance-20260923.md)。本文结果和未验收项是当次历史快照，原始证据保持不变。

> 本记录对应用户要求：增加真正的微信登录并优先使用；继续使用 Flurl、SOLID、朴素模式、中文注释和本地开发门禁。按先前授权提交 Git、部署到指定 Controls 并清理中间文件。

## 1. 问题与结果

此前二维码属于网易云 App 的 type=3 / codekey 流程。微信扫描可进入手机网页登录并完成“微信授权登录网易云音乐”，但手机网页会话并不等于桌面 codekey 被确认，因此桌面仍等待。

现新增微信网站扫码链：网易 SNS 入口 → 微信授权页和长轮询 → 网易 `/back/weichat` → MUSIC_U 候选会话 → 网易账号核验。
微信成为默认绿色按钮；网易云 App 保留独立备用入口。两者共享取消、单账号、保存、恢复、退出与 Document 生命周期。

微信入口来自[网易现有登录页面](https://music.163.com/api/sns/authorize?snsType=10&clientType=web2&callbackType=Login&forcelogin=true)及当次返回的微信授权页，不属于 api-enhanced 固定提交的 440 个模块，不承诺页面协议长期稳定。
旧 App 逻辑对照[固定上游 login_qr_check](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/login_qr_check.js)。

## 2. 设计与边界

- `IQrLoginProvider` / `IQrLoginAttempt` 隔离两种扫码协议；协调器只编排统一状态，界面只展示图片和登录方法。具体状态、票据和临时 Cookie 由一次尝试持有。
- 所有 HTTP 使用已有 Flurl 4.0.2；没有新增 NuGet 包、Node 服务、浏览器依赖、AppSecret 或通用状态机框架。
- 接受官方 HTTPS 入口，手动控制网易同源回调跳转，限制响应体，解析有限脚本赋值，不执行远端 JavaScript。
- Cookie 按域名/路径发送；二维码 UUID、OAuth code/state、微信 Cookie 不持久保存。一次性回调失败不能重放；需绑定或额外验证时明确终止。
- 用户取消、切换入口或关闭发起页面后，旧代次不能发布账号；网易账号核验成功后才进入原 DPAPI 会话保存流程。

完整契约见[HTTP 与会话](../../../reference/netease-http-session.md)，回归映射见[W01–W09](../../../maintenance/netease-login-verification.md#12-w微信登录专项矩阵)。

## 3. 真实手机验证

使用同一生产 Provider、协调器和账号 API 的显式 `--wechat` 探针；存储替换为内存空保存实现，不读取既有 Cookie、不写 session.bin，不输出账号 ID、昵称、Cookie 或票据。

2026-09-22 用户扫描本次新生成的微信二维码，回复“已确认授权，手机显示成功”。进程实测输出：

```text
WaitingForScan
WaitingForConfirmation
Verifying
SignedIn
accountVerified=true
persistentSessionWritten=false
exitCode=0
```

这证明本次真实微信授权、网易回调与账号接口核验闭环成功。初次无人扫码探针按 3 分钟预算停止；第二次扫码完成。
不据此声称真实重启恢复、远端退出、Host 加载、账号绑定/风控分支均已验证。二维码原图与真实凭据不提交仓库。

## 4. 自动验证

源码基线 `d0c33fb`，加本记录所属提交中的微信实现和测试。单元测试阶段 92 项全部通过，无失败、无跳过。
覆盖微信状态、回调编码、Cookie 隔离、拒绝外域跳转、缺 code/无会话/超时、一次性票据不重放、在途长轮询取消、默认与备用切换，以及既有协议/存储/并发/UI 回归。

执行 `pwsh -NoProfile -File tools/verify-development.ps1`：14 项门禁自测通过、全解决方案锁定还原通过、Debug 构建 0 警告/0 错误、92 项测试全部通过且无跳过、18 份文档链接/锚点检查通过。
证据目录为 `TestResults/NetEaseLogin/20260922-121234-655c50be`，包含 TRX、verification.json 与生产 View 的 Headless 渲染图。
实际检查渲染图：绿色微信主按钮、网易 App 备用入口、取消与恢复按钮完整显示，二维码与说明无裁切。
运行环境 Windows 10.0.26200 / .NET SDK 10.0.302；门禁仅记录其自动活动，真实手机结果另见第 3 节。
未使用 Windows CI、发布门禁或发布打包。

## 5. 开发部署与清理

目标 `D:\data\avalonia\Controls\MusicNetEasePlugin\`，使用现有 Plugin SDK Build 的开发部署目标：

```powershell
dotnet build src/MusicNetEasePlugin.Plugin/MusicNetEasePlugin.Plugin.csproj -c Debug --no-restore -warnaserror -p:ManagedPluginDeployRoot=D:/data/avalonia/Controls
```

执行前已核对 Controls 与插件目录的真实路径、无重解析点；未发现从该 Host 安装目录运行的进程。
开发部署成功，0 警告/0 错误。仍为版本 1.0.0、SDK `[3.4.1, 4.0.0)`，10 个文件分别与当前构建资产及解析后的 NuGet 私有依赖 SHA256 一致；其他 12 个插件目录保留。

部署 DLL SHA256：`85A3EB7869ACBAD3169C93B14DD8E7433071E06404D8A4D0C4DB5F17DA3B8EB9`。

执行 `dotnet clean MusicNetEasePlugin.slnx -c Debug --nologo -v:minimal`，再按已检查的明确文件路径清理残留还原/生成文件和本次真实二维码图片。
Plugin、Standalone、Tests、LoginProbe 四个项目的 bin/obj 文件数全部为 0；空目录保留，部署目录和自动验证证据保留，真实账号文件未改动。
清理后部署 DLL 摘要保持一致。下次构建先 restore；开发门禁已包含该步骤。

Host 需完整重启后使用新入口，本次未启动 Host 做加载/恢复/退出验收。此前 App 版部署保留于[历史记录](development-deploy-20260922.md)。
