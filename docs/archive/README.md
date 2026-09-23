# 已完成方案与历史记录

> 更新日期：2026-09-23。V1–V4 已实现并由用户确认手工验收完成，详见[验收收口记录](records/netease-v1-v4-acceptance-20260923.md)。
> 当前入口：[文档总导航](../README.md) · [能力路线图](../roadmap/netease-capability-roadmap.md) · [日常播放器契约](../reference/netease-daily-player.md)。

## 已完成方案与当前承接

| 已归档方案 | 完成范围 | 当前承接位置 |
| --- | --- | --- |
| [V1：Flurl 接入与扫码登录](plans/netease-v1-flurl-login-plan.md) | 登录实现与手工验收完成；原扩展设想按实际能力分流 | [会话契约](../reference/netease-http-session.md)、[登录矩阵](../maintenance/netease-login-verification.md)；未实施扩展见路线图 |
| [V2：M1 搜索与单曲播放](plans/netease-v2-m1-playback-plan.md) | 搜索、单曲播放、账号/播放设置实现与手工验收完成 | [播放契约](../reference/netease-music-playback.md)、[播放矩阵](../maintenance/netease-v2-m1-playback-verification.md) |
| [V2：LibVLC 路径、Tool 与 Dock](plans/netease-v2-libvlc-tool-and-dock-design.md) | V2 配套设计实现与手工验收完成 | [播放快速开始](../quick-start/netease-playback.md)、[项目与窗口职责](../reference/project-and-window-responsibilities.md) |
| [V3：紧凑桌面界面与主题](plans/netease-v3-desktop-ui-and-theme-plan.md) | 布局、主题、轻量动效实现与手工验收完成 | [界面契约](../reference/netease-desktop-ui.md)、[界面矩阵](../maintenance/netease-v3-ui-verification.md) |
| [V4：M2 日常播放器](plans/netease-v4-m2-daily-player-plan.md) | 歌单、队列、歌词、历史与恢复实现及手工验收完成 | [日常播放器契约](../reference/netease-daily-player.md)、[V4 矩阵](../maintenance/netease-v4-m2-daily-player-verification.md) |

方案正文保留设计时的约束、阶段标识和待办快照，不作为新的执行指令；旧页面所有者规则以 V4 当前契约为准。原 V1 的播放器扩展已由 V2–V4 落地，其余候选统一由路线图承接。

## 验收与文档收口

| 日期 | 记录 | 依据与范围 |
| --- | --- | --- |
| 2026-09-23 | [V1–V4 验收收口与文档整理](records/netease-v1-v4-acceptance-20260923.md) | 用户确认现有 V 编号内容已实现并完成手工验收；方案归档、当前契约和维护矩阵去漂移 |

以下实施记录的证明范围均指当次操作；其中“待验收”“未启动 Host”不代表当前仍未完成。原始 TRX、截图、探针与部署摘要保持原样，后续用户确认由上表单独承接。正式发布、跨平台适配和 M3–M5 不包含在本次验收结论中。

## 实施与验证记录

以下记录均为 2026-09-22，按该日实施先后排列：

| 顺序 | 记录 | 证明范围 |
| --- | --- | --- |
| 1 | [P0 协议与 Flurl 最小验证](records/netease-v1/p0-protocol-verification-20260922.md) | 原始协议向量、无历史 Cookie 的 key/801/未登录联网观察；不含真实手机授权 |
| 2 | [App 扫码登录实施与开发验证](records/netease-v1/login-implementation-20260922.md) | 登录编排、会话保护、界面与本地自动验证；真实账号和 Host 边界按该次记录 |
| 3 | [早期 App 版开发部署](records/netease-v1/development-deploy-20260922.md) | 微信入口加入前的插件开发部署和文件核对，保留为历史 |
| 4 | [微信默认登录实现、实测与部署](records/netease-v1/wechat-login-implementation-20260922.md) | 微信授权、网易回调、账号核验、自动验证以及后续开发部署；探针未持久保存真实会话 |

V1 最后一次部署见第 4 项，后续部署见下面的 V2 记录。各记录中的测试数量、摘要和未验证项只描述其对应操作；需要新增验证时另建记录，并更新维护矩阵。

| 日期 | V2 实施记录 | 证明范围 |
| --- | --- | --- |
| 2026-09-23 | [M1 搜索、单曲播放与 LibVLC 设置实现](records/netease-v2/m1-implementation-20260923.md) | 自动回归、真实 MP3 解码、视频目录复用、内置/精简开发产物；真实听感及 Host/Dock 待验收 |
| 2026-09-23 | [M1 公共 LibVLC 配置与开发部署](records/netease-v2/shared-libvlc-deployment-20260923.md) | 指定 Controls 部署、Common 路径配置、公共库解码及 Host 实际加载；标准清理与自动审批阻止的剩余项 |

| 日期 | V3 实施记录 | 证明范围 |
| --- | --- | --- |
| 2026-09-23 | [紧凑桌面界面、主题与轻量动效](records/netease-v3/ui-implementation-20260923.md) | 本地自动门禁、真实 View 截图、同款基础主题组合与有限动效回收；真实 Host/Dock、缩放和硬件帧时间待验收，无部署 |
| 2026-09-23 | [V3 复用 Common LibVLC 开发部署](records/netease-v3/common-deployment-20260923.md) | 无原生库构建、指定 Controls 整体替换、12 文件摘要一致，公共库与用户配置不变；未启动 Host |

| 日期 | V4 实施记录 | 证明范围 |
| --- | --- | --- |
| 2026-09-23 | [M2 日常播放器分阶段实施](records/netease-v4/m2-implementation-20260923.md) | V4.0–V4.5 功能、完整 V4 自动门禁、三曲/定位/静默恢复 PCM、双色截图与阶段提交；真实声卡、Host/Dock 和重启验收单列 |
| 2026-09-23 | [V4 复用 Common LibVLC 部署与清理](records/netease-v4/common-deployment-20260923.md) | M2 无原生库构建、指定 Controls 的 12 文件部署与摘要验证；标准 clean 清理约 1.91 GiB，剩余约 130.96 MiB 附手工规则；未启动 Host |
