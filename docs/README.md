# 网易云音乐插件文档

> 本插件唯一文档总导航；整理日期：2026-09-24。当前行为以 `quick-start` 和 `reference` 为准，历史操作与证据见 `archive`。

## 按任务查找

| 我想做什么 | 从这里阅读 |
| --- | --- |
| 了解项目、构建与首次登录 | [项目首页](../README.md)、[扫码登录](quick-start/netease-login.md) |
| 搜索、播放、管理队列和查看歌词 | [播放器快速开始](quick-start/netease-playback.md) |
| 配置多个插件共用的 LibVLC | [运行库配置](quick-start/netease-playback.md#配置多个插件共用的-libvlc)、[无原生库构建](quick-start/netease-playback.md#不携带原生库的开发产物) |
| 修改业务或界面 | [项目与窗口职责](reference/project-and-window-responsibilities.md)、下方当前契约 |
| 验证改动或查看测试依据 | [验证指南](maintenance/verification.md) |
| 完成 V5 / V6 剩余验收 | [性能与实机待验清单](maintenance/netease-v5-v6-acceptance.md) |
| 部署、打包和检查真实 Host | [部署与发布](maintenance/deployment-and-release.md) |
| 查看下一步候选功能 | [能力路线图](roadmap/netease-capability-roadmap.md) |
| 按计划开发 M3 音乐库管理 | [V7 专用实施方案](roadmap/netease-v7-m3-music-library-management-plan.md)、[V7 验证矩阵](maintenance/netease-v7-m3-music-library-verification-plan.md) |
| 查找过去的设计、实施与部署 | [归档索引](archive/README.md) |
| 新增、更新或迁移文档 | [文档维护约定](maintenance/documentation.md) |

## 当前状态与待验范围

V 表示方案 / 实施序号，M 表示能力里程碑，均不是插件包版本。当前插件身份为 `myavalonia.plugin.music.netease`，目标 Windows x64。

| 范围 | 当前结论 | 依据 |
| --- | --- | --- |
| V1–V4 / M0–M2 | 登录、播放、日常播放器及早期界面已实现；用户已确认手工验收 | [验收收口记录](archive/records/netease-v1-v4-acceptance-20260923.md) |
| V5 轻量交互与 UI | 主体已实现，整体验收未收口；旧复核 C01–C08 已由 V6 接续实现，D01–D05 仍待验证 | [V5 历史复核](archive/records/netease-v5/completion-audit-20260923.md)、[V6 承接说明](archive/records/netease-v6/drawer-and-interaction-implementation-20260923.md#5-实机验收及历史问题) |
| V6 抽屉与交互 | V6-01～18 已实现、本地开发验证通过；完整实机验收未完成 | [实施与自动证据](archive/records/netease-v6/drawer-and-interaction-implementation-20260923.md)、[验证矩阵](maintenance/netease-v6-drawer-and-interaction-verification-plan.md) |
| 最近开发部署 | 2026-09-23 部署到指定 Controls，共用 LibVLC；实际 Host 启动复测通过 | [V6 部署记录](archive/records/netease-v6/deployment-20260923.md) |
| V7 / M3 音乐库管理 | 实施方案与专用验证矩阵已编写；业务代码、V7 门禁和真实验收均待实施 | [V7 方案](roadmap/netease-v7-m3-music-library-management-plan.md)、[V7 验证矩阵](maintenance/netease-v7-m3-music-library-verification-plan.md) |
| M4–M5 / 正式发布 | 后续功能尚未实施；正式 ZIP 发布、跨平台适配不在已完成结论内 | [能力路线图](roadmap/netease-capability-roadmap.md)、[发布流程](maintenance/deployment-and-release.md) |

剩余的 D01–D05 性能 / 实机项目与 V6 新增交互验收统一维护在[待验清单](maintenance/netease-v5-v6-acceptance.md)，其中列出已有证据、缺口和关闭条件。V5 的封面取色仍为可选未实施项，见[后续候选](roadmap/netease-capability-roadmap.md#现有界面的可选增强)。方案归档不改变这些状态；后续验证新增带日期记录，再更新清单与本表。

## 当前实现与开发参考

| 文档 | 内容 |
| --- | --- |
| [HTTP 与会话](reference/netease-http-session.md) | 微信 / App 登录、Flurl、端点、受保护会话、取消与生命周期 |
| [音乐与播放](reference/netease-music-playback.md) | 搜索与单曲执行、账号隔离、媒体预算、LibVLC 来源及设置 |
| [日常播放器](reference/netease-daily-player.md) | 歌单、共享队列、四模式、定位、歌词、历史和静默恢复 |
| [Document / Tool 界面](reference/netease-desktop-ui.md) | V6 抽屉、焦点、响应式布局、主题、偏好与图片预算 |
| [项目与窗口职责](reference/project-and-window-responsibilities.md) | 代码结构、Plugin / Standalone / Tests 分工、公开 SDK 与资源所有权 |
| [插件图标](reference/plugin-icons.md) | 图标注册、页面使用和私有依赖声明 |
| [Workbench Command](reference/workbench-commands.md) | 接入参考；当前未注册工作台命令或快捷键，Document 内 Ctrl+F 属于局部输入 |
| [Workflow Action](reference/workflow-actions.md) | 接入参考；当前未选择或注册 Provider / Consumer 角色 |
| [固定上游能力清单](reference/netease-api-enhanced-capabilities.md) | 固定提交的模块索引和移植边界；不代表插件已支持全部上游能力 |

## 方案归档与回归验证

V1–V6 原方案及已完成决策的 V6 候选评估统一放在 `archive/plans`；当前行为查 `reference`，剩余验收查 `maintenance`，后续功能查 `roadmap`。V1–V4 已验收，V5 / V6 仍待完整验收，详见[归档索引](archive/README.md)。

| 范围 | 方案 | 可复用验证 |
| --- | --- | --- |
| 登录 | [V1 历史方案](archive/plans/netease-v1-flurl-login-plan.md) | [登录矩阵](maintenance/netease-login-verification.md) |
| 搜索与单曲播放 | [V2 历史方案](archive/plans/netease-v2-m1-playback-plan.md) | [M1 矩阵](maintenance/netease-v2-m1-playback-verification.md) |
| 早期桌面界面 | [V3 历史方案](archive/plans/netease-v3-desktop-ui-and-theme-plan.md) | [V3 矩阵](maintenance/netease-v3-ui-verification.md) |
| 日常播放器 | [V4 历史方案](archive/plans/netease-v4-m2-daily-player-plan.md) | [M2 矩阵](maintenance/netease-v4-m2-daily-player-verification.md) |
| 轻量交互与 UI | [V5 方案](archive/plans/netease-v5-lightweight-interaction-and-ui-plan.md) | [V5 矩阵](maintenance/netease-v5-ui-interaction-verification-plan.md) |
| 抽屉与交互 | [V6 方案](archive/plans/netease-v6-drawer-and-interaction-change-plan.md)、[候选评估依据](archive/plans/netease-v6-interaction-candidates-evaluation.md) | [V6 矩阵](maintenance/netease-v6-drawer-and-interaction-verification-plan.md) |
| 音乐库管理（待开发） | [V7 实施方案](roadmap/netease-v7-m3-music-library-management-plan.md) | [V7 验证规范（待实施）](maintenance/netease-v7-m3-music-library-verification-plan.md) |

默认完整开发检查为 V6，包含适用的 M1 / V3 / M2 / V5 回归。命令、产物、手工验证与发布边界统一见[验证指南](maintenance/verification.md)。当前本地开发不使用 AIFLOW、Windows CI 或发布门禁。

V7 是下一阶段开发依据，方案保留在 `roadmap`；V7 场景映射及门禁脚本尚未创建，不能执行 `-Milestone V7`，也不改变 V5 / V6 的既有待验结论。

## 文档目录

```text
docs/
├─ README.md          # 总导航、当前状态及待验范围
├─ quick-start/       # 当前可执行的使用步骤
├─ reference/         # 当前实现契约、开发参考、固定上游索引
├─ roadmap/           # 尚未实施的能力与可选增强
├─ maintenance/       # 验证矩阵、待验清单、部署和文档维护方法
└─ archive/
   ├─ README.md       # 历史方案、实施与证据索引
   ├─ plans/          # 历史方案与评估；验收状态单独标明
   └─ records/        # 带日期的实施、复核、验收及部署记录
```

迁移规则和检查命令见[文档维护约定](maintenance/documentation.md)。前轮变更见[入口整理记录](archive/records/documentation/reorganization-20260924.md)，本轮见[V5 / V6 分类记录](archive/records/documentation/classification-20260924.md)。
