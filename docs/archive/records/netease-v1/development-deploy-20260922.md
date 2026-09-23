# 网易插件开发部署记录（2026-09-22）

> 后续状态：V1–V4 已于 2026-09-23 根据用户确认完成手工验收，见[验收收口记录](../netease-v1-v4-acceptance-20260923.md)。本文结果和未验收项是当次历史快照，原始证据保持不变。

> 用户明确指定部署根 `D:\data\avalonia\Controls\`，并要求部署后清理中间文件。本次为开发部署，不执行 Windows CI 或发布门禁。

## 1. 部署结果

- 目标：`D:\data\avalonia\Controls\MusicNetEasePlugin\`。
- 源码基线：`56a7364f256fb6283265ef69cb249870d440dbd7`，加本记录所属提交中的许可资产声明；业务代码未改变。
- 配置：Debug / net10.0，插件身份 `myavalonia.plugin.music.netease`，版本 1.0.0，SDK 区间 `[3.4.1, 4.0.0)`。
- 部署前目标插件目录不存在；未发现从该 Host 安装目录运行的进程。其他 12 个插件目录保留。
- 使用 Build 包的干净资产筛选和部署目标，不复制普通 bin 全目录，不手写 manifest。

执行命令：

```powershell
dotnet restore src/MusicNetEasePlugin.Plugin/MusicNetEasePlugin.Plugin.csproj --locked-mode
dotnet build src/MusicNetEasePlugin.Plugin/MusicNetEasePlugin.Plugin.csproj -c Debug --no-restore -warnaserror -p:ManagedPluginDeployRoot=D:/data/avalonia/Controls
```

locked restore 成功，构建 0 警告/0 错误，部署目标输出 10 个文件：

| 类别 | 文件 |
| --- | --- |
| 插件与清单 | MusicNetEasePlugin.Plugin.dll、MusicNetEasePlugin.Plugin.pdb、MusicNetEasePlugin.Plugin.deps.json、plugin.manifest.json |
| 私有依赖 | Flurl.dll、Flurl.Http.dll、QRCoder.dll、System.Security.Cryptography.ProtectedData.dll、MyAvaloniaManagement.Icons.dll |
| 许可 | THIRD-PARTY-NOTICES.md |

检查发现仅配置 CopyToOutputDirectory/CopyToPublishDirectory 不会使许可说明进入干净部署目录，因此补充 `ManagedPluginAsset` 与明确 TargetPath；随部署文件核验。
10 个文件的 SHA256 均与本次生成资产或 ResolveReferences 解析的 NuGet 私有依赖相同；无 Avalonia、Plugin SDK、CommunityToolkit 或 Microsoft.Extensions 等 Host 共享 DLL。

部署 DLL SHA256：`6318164457E55F610FE020389E08B995890C397EC5F5C79596269E4E97DF7310`。

## 2. 中间文件清理

先执行 `dotnet clean MusicNetEasePlugin.slnx -c Debug --nologo -v:minimal`，再逐项删除经过路径、未跟踪状态和文件名核对的残留编译/还原文件。
直接递归删除受到执行策略限制，最终采用明确文件路径的非递归清理方式；空目录保留。

| 项目 | bin 残留文件 | obj 残留文件 |
| --- | --- | --- |
| MusicNetEasePlugin.Plugin | 0 | 0 |
| MusicNetEasePlugin.Standalone | 0 | 0 |
| MusicNetEasePlugin.Tests | 0 | 0 |
| MusicNetEasePlugin.LoginProbe | 0 | 0 |

清理后再次核对部署目录仍为 10 个文件、插件 DLL 摘要不变。未清理源码、锁文件、既有测试证据、用户账号数据或其他插件。
下一次编译前需正常 restore；本地开发门禁已包含该步骤。

## 3. 验证边界

本轮只改变部署资产元数据，执行锁定还原、警告视为错误的构建、实际部署文件核对和文档检查；未重复业务单测。
此前 67 项测试证据见[登录实现记录](login-implementation-20260922.md)。
Host 尚未启动验证插件加载，真实手机授权仍待人工执行。启动 Host 后进行加载与扫码验证；不将文件部署成功等同于真实 Host 或账号验收通过。
