# V3 开发部署：复用 Common LibVLC（2026-09-23）

> 后续状态：V1–V4 已于 2026-09-23 根据用户确认完成手工验收，见[验收收口记录](../netease-v1-v4-acceptance-20260923.md)。本文结果和未验收项是当次历史快照，原始证据保持不变。

> 用户指定目标：`D:\data\avalonia\Controls`，不携带原生 LibVLC。源码基线 `2a8f5ea`，包含 V3 实现 `0a491b2`。
> [V3 实施与测试](ui-implementation-20260923.md) · [可复用部署说明](../../../maintenance/deployment-and-release.md) · [本次文件摘要](assets/common-deployment.json)。

## 部署结果

完成 locked restore，以 `IncludeLibVlcRuntime=false` 编译 Plugin 到独立输出。Debug 构建 0 警告、0 错误；通过 `DeployManagedPlugin` 先生成干净暂存目录，再整体替换 `D:\data\avalonia\Controls\MusicNetEasePlugin`。

部署前确认目标 Host 没有运行；核对绝对路径、重解析点、唯一插件身份和 MSBuild 实际部署叶子目录。旧插件副本保存在本仓 `artifacts/v3-common-deployment-20260923-104516/previous-plugin`，不放在 Controls 中避免重复发现。

插件版本仍为 `1.0.0`，SDK 区间 `[3.4.1, 4.0.0)`。目标共 12 个文件，逐个 SHA256 与暂存产物一致；没有 `native` 原生目录、`libvlc.dll` 或 `libvlccore.dll`，保留运行必需的托管 `LibVLCSharp.dll`。未携带 Host 共享 Avalonia/Dock/SDK 程序集。

入口 `MusicNetEasePlugin.Plugin.dll` 的 SHA256：

```text
446C945C8CC1F55163A8DFABCB924F160F43C2DF087089C417EEC3633A03B63A
```

## Common 与现有数据

现有 `playback-settings.json` 已配置 `D:\data\avalonia\Common\libvlc`，无需重写。公共核心 DLL 版本为 `3.0.23`。

部署前后核对 Common 中全部 425 个文件的 SHA256，内容和数量不变；插件用户目录中的 3 个现有文件摘要不变。其他 12 个插件目录名称、数量及目录修改时间保持不变。没有复制、删除或替换公共原生库，也没有改动登录信息。

构建与部署日志、本地备份和更细的前后核对数据在忽略目录 `artifacts/v3-common-deployment-20260923-104516`；仓库只保存不含账号内容的部署摘要。

## 验证边界

V3 源码全量本地开发验证此前已通过 168 项测试及 63 项门禁自测，本轮未改业务代码。此次追加的是无原生库构建、干净部署、产物身份与文件完整性检查。

本轮没有启动 Host，未将文件部署成功等同于真实 Dock、主题或播放验收。启动目标 Host 后加载本次 V3 产物，后续实机项目继续由[V3 专项矩阵](../../../maintenance/netease-v3-ui-verification.md)跟踪。

本次为开发部署，没有使用 AIFLOW、Windows CI、正式 ZIP 或发布门禁，没有推送远端。

## 部署后清理

用户随后要求清理中间产物。先核对清理范围仅在本插件仓库，保留部署目录、Common、验证证据、参考资料和本次回滚副本。

批量递归删除编译/暂存目录的动作被自动审批审核拒绝，仅返回 `blocked by policy`，没有更具体原因；没有尝试换工具或拆分删除绕过限制。随后执行标准 `dotnet clean MusicNetEasePlugin.slnx -c Debug`，以及同一 `IncludeLibVlcRuntime=false` / 独立 OutputPath 的 Plugin clean，两次均成功。

常规 bin/obj 共减少 1,966,477,471 字节，另清理本次独立编译输出约 81.5 MiB，合计约 **1.91 GiB**。Standalone、Tests、LoginProbe 的 bin 已无文件；未删除测试证据、上游参考或回滚备份。

标准 clean 不负责删除干净部署暂存目录及部分还原/生成文件。剩余中间文件约 **124.87 MiB**，见[清理后清单](assets/cleanup-remaining.json)：

| 范围 | 剩余内容 |
| --- | --- |
| Plugin 普通 bin 与本次独立 bin | 各 1 个生成 manifest |
| 四个项目 obj | 44 个还原/缓存文件 |
| 旧 M1 两个暂存根 | 463 个文件，含既有内置库暂存 |
| 本次 V3 暂存 Controls | 12 个干净插件文件 |

因此清理未全部完成。清理后再次逐个验证目标部署 12 文件和 Common 425 文件的 SHA256，均与部署时一致；不影响已部署插件使用。详细标准 clean 日志保留在本次本地 artifacts 记录目录。
