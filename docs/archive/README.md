# 历史方案、实施与证据

> 更新日期：2026-09-25。这里保存各次操作的事实，归档不代表验收完成。当前状态与待验范围统一见[文档总导航](../README.md#当前状态与待验范围)。

## 最近记录

| 记录 | 范围 |
| --- | --- |
| [2026-09-25 V8 / M4 实施](records/netease-v8/m4-implementation-20260925.md) | 推荐、作品、FM 和远端记录，全量开发验证及专用证据；[真实验收待办](records/netease-v8/acceptance-20260925.md)单列 |
| [2026-09-24 V7 / M3 实施](records/netease-v7/m3-implementation-20260924.md) | 音乐库管理、完整本地验证与专用证据；[真实验收待办](records/netease-v7/acceptance-20260924.md)单列 |
| [2026-09-24 V5 / V6 分类整理](records/documentation/classification-20260924.md) | 原方案与候选评估归档，性能 / 实机待验集中到维护清单 |
| [2026-09-24 文档整理](records/documentation/reorganization-20260924.md) | 入口简化、记录归位、当前状态承接、链接与证据检查 |
| [V6 最近部署与清理](records/netease-v6/deployment-20260923.md) | 2026-09-23 指定 Controls 部署、不携带原生 libVLC、实际 Host 启动复测 |
| [V6 实施与自动验证](records/netease-v6/drawer-and-interaction-implementation-20260923.md) | 18 项交互实现、本地开发证据及实机边界 |
| [V5 历史完成度复核](records/netease-v5/completion-audit-20260923.md) | 当时 C / D / E 范围；后续 C 项由 V6 承接，D 待验保留 |
| [V1–V4 验收收口](records/netease-v1-v4-acceptance-20260923.md) | 用户已确认的手工验收范围 |

V1–V8 原方案均按历史设计归档；其中 V1–V4 已验收，V5 / V6 / V7 / V8 完整验收仍未收口，剩余事项见[V5 / V6 待验清单](../maintenance/netease-v5-v6-acceptance.md)和[V7 待验记录](records/netease-v7/acceptance-20260924.md)。以下实施记录只证明当次操作，原始测试、截图与部署摘要不随后续状态更新而改写。

## 历史方案与当前承接

| 已归档方案 | 实现与验收状态 | 当前承接位置 |
| --- | --- | --- |
| [V1：Flurl 接入与扫码登录](plans/netease-v1-flurl-login-plan.md) | 登录实现与手工验收完成；原扩展设想按实际能力分流 | [会话契约](../reference/netease-http-session.md)、[登录矩阵](../maintenance/netease-login-verification.md)；未实施扩展见路线图 |
| [V2：M1 搜索与单曲播放](plans/netease-v2-m1-playback-plan.md) | 搜索、单曲播放、账号/播放设置实现与手工验收完成 | [播放契约](../reference/netease-music-playback.md)、[播放矩阵](../maintenance/netease-v2-m1-playback-verification.md) |
| [V2：LibVLC 路径、Tool 与 Dock](plans/netease-v2-libvlc-tool-and-dock-design.md) | V2 配套设计实现与手工验收完成 | [播放快速开始](../quick-start/netease-playback.md)、[项目与窗口职责](../reference/project-and-window-responsibilities.md) |
| [V3：紧凑桌面界面与主题](plans/netease-v3-desktop-ui-and-theme-plan.md) | 布局、主题、轻量动效实现与手工验收完成 | [界面契约](../reference/netease-desktop-ui.md)、[界面矩阵](../maintenance/netease-v3-ui-verification.md) |
| [V4：M2 日常播放器](plans/netease-v4-m2-daily-player-plan.md) | 歌单、队列、歌词、历史与恢复实现及手工验收完成 | [日常播放器契约](../reference/netease-daily-player.md)、[V4 矩阵](../maintenance/netease-v4-m2-daily-player-verification.md) |
| [V5：轻量交互与 UI](plans/netease-v5-lightweight-interaction-and-ui-plan.md) | 主体已实现，C01–C08 由 V6 承接；性能及实机验收未完成 | [界面契约](../reference/netease-desktop-ui.md)、[V5 矩阵](../maintenance/netease-v5-ui-interaction-verification-plan.md)、[待验清单](../maintenance/netease-v5-v6-acceptance.md) |
| [V6：抽屉与交互修改方案](plans/netease-v6-drawer-and-interaction-change-plan.md) | 18 项已实现并通过本地检查；完整实机验收未完成 | [界面契约](../reference/netease-desktop-ui.md)、[V6 矩阵](../maintenance/netease-v6-drawer-and-interaction-verification-plan.md)、[待验清单](../maintenance/netease-v5-v6-acceptance.md) |
| [V6：交互候选评估](plans/netease-v6-interaction-candidates-evaluation.md) | 18 项均已选定并实施；保留提案时的评估与取舍 | [冻结方案](plans/netease-v6-drawer-and-interaction-change-plan.md)、[实施记录](records/netease-v6/drawer-and-interaction-implementation-20260923.md) |
| [V7：M3 音乐库管理](plans/netease-v7-m3-music-library-management-plan.md) | V7-01～10 已实现，自动验证通过；真实账号 / Host 待验 | [音乐库契约](../reference/netease-music-library-management.md)、[V7 矩阵](../maintenance/netease-v7-m3-music-library-verification-plan.md)、[真实验收待办](records/netease-v7/acceptance-20260924.md) |
| [V8：M4 音乐发现](plans/netease-v8-m4-music-discovery-plan.md) | V8-01～10 已实现，本地验证通过；真实账号 / Host 待验 | [音乐发现契约](../reference/netease-music-discovery.md)、[V8 矩阵](../maintenance/netease-v8-m4-music-discovery-verification-plan.md)、[真实验收待办](records/netease-v8/acceptance-20260925.md) |

方案正文保留设计时的约束、阶段标识和待办快照，不作为新的执行指令；旧页面所有者规则以 V4 当前契约为准。原 V1 的播放器扩展已由 V2–V4 落地，其余候选统一由路线图承接。

## 验收与文档收口

| 日期 | 记录 | 依据与范围 |
| --- | --- | --- |
| 2026-09-23 | [V1–V4 验收收口与文档整理](records/netease-v1-v4-acceptance-20260923.md) | 用户确认现有 V 编号内容已实现并完成手工验收；方案归档、当前契约和维护矩阵去漂移 |

以下实施记录的证明范围均指当次操作。V1–V4 旧记录中的“待验收”“未启动 Host”由上述后续用户确认承接；该确认不延伸到 V5 / V6。原始 TRX、截图、探针与部署摘要保持原样，正式发布、跨平台适配和 M3–M5 不包含在已完成结论中。


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
| 2026-09-23 16:11 | [V4 当前代码重新编译、部署与清理](records/netease-v4/redeployment-20260923-1609.md) | 以 `235ea40` 构建，不携带原生 libVLC；12 文件部署与摘要核对，旧插件已备份；标准清理约 1.91 GiB，约 137.05 MiB 剩余项受自动审批限制；V5 仍为方案 |

## V5 实施、复核与部署

以下均为 2026-09-23 的记录；首轮实现、故障修复和重新部署分别保留，避免混用证据。

| 记录 | 当次范围 |
| --- | --- |
| [轻量交互与 UI 首轮实施](records/netease-v5/ui-interaction-implementation-20260923.md) | 生产实现、自动回归、资源测量及原有差项 |
| [首轮部署](records/netease-v5/deployment-20260923.md) | 初次 V5 开发部署、文件摘要与清理；不能代替后续启动修复 |
| [完成度重新核查](records/netease-v5/completion-audit-20260923.md) | C01–C08 体验差项、D01–D05 性能与实机待验、E 可选项的历史快照 |
| [启动加载故障修复](records/netease-v5/startup-fix-20260923.md) | Tool 贡献根重复注册修复、309 项回归及真实 Host 启动前后对照 |
| [修复版重新部署](records/netease-v5/redeployment-20260923.md) | 无原生库插件替换、实际启动与当次清理结果 |

V5 初轮证据见 [gate 报告](records/netease-v5/gate-20260923/verification.json)，修复证据见 [startup-fix 报告](records/netease-v5/startup-fix-20260923/gate/verification.json)。后续交互修补以 V6 实施记录为准，旧报告不补写后续测试结果。

## V6 实施与部署

| 日期 | 记录 | 当次范围 |
| --- | --- | --- |
| 2026-09-23 | [歌词抽屉与交互实施](records/netease-v6/drawer-and-interaction-implementation-20260923.md) | V6-01～18 实现、326 项测试及 310 项门禁自测、Headless 布局 / 手势、实机待验边界 |
| 2026-09-23 | [指定 Controls 部署与清理](records/netease-v6/deployment-20260923.md) | 完整门禁重跑、12 文件无原生库部署、Host 启动复测和标准清理；未新增备份、未正式发布 ZIP |

两次门禁分别保留 [实施报告](records/netease-v6/gate-20260923/verification.json)与[部署阶段报告](records/netease-v6/deployment-20260923/gate/verification.json)，不混用 runId / revision / sourceSha256。自动报告中的 `hostVerified=false`、`deployed=false` 保留生成时事实，后续结果见[独立 Host 启动摘要](records/netease-v6/deployment-20260923/host-startup.json)与[部署摘要](records/netease-v6/deployment-20260923/deployment.json)。

完整 Dock、DPI、物理输入、读屏、声卡与用户体验验收仍待执行；当前清单见[文档导航](../README.md#当前状态与待验范围)，复用方法见[验证指南](../maintenance/verification.md)。

## 文档整理记录

| 日期 | 记录 | 内容 |
| --- | --- | --- |
| 2026-09-23 | [V1–V4 验收与整理](records/netease-v1-v4-acceptance-20260923.md) | 早期方案归档、契约与维护矩阵同步 |
| 2026-09-24 | [文档入口与 V5 / V6 记录归位](records/documentation/reorganization-20260924.md) | 当前说明与历史快照分工、状态承接和导航检查 |
| 2026-09-24 | [V5 / V6 方案分类与验收承接](records/documentation/classification-20260924.md) | 已实施方案和评估归档、维护清单集中跟踪待验、可选项保留在路线图 |

## V7 音乐库管理实施

[专用实施记录](records/netease-v7/m3-implementation-20260924.md)保存代码提交、完整门禁及原始证据；[H01–H05 待验记录](records/netease-v7/acceptance-20260924.md)说明真实账号、官方令牌、Host 和物理交互的缺口。本轮没有部署、Windows CI 或发布。
