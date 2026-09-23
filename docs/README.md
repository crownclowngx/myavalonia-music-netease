# MusicNetEasePlugin 文档导航

> 本插件唯一文档总导航；更新日期：2026-09-23。
> 当前实现：V5 主体已接入，启动故障已修复；整体尚未正式完成，见 [完成度复核](maintenance/netease-v5-completion-audit-20260923.md)。V1–V4 已实现，用户已确认手工验收完成；对应能力里程碑 M0–M2 已完成。
> 验收依据：[V1–V4 验收收口记录](archive/records/netease-v1-v4-acceptance-20260923.md)。后续 M3–M5 尚未实施，下一步另行讨论。

## 当前使用与实现

| 我想了解 | 文档 | 内容 |
| --- | --- | --- |
| 如何构建、登录与恢复账号 | [项目首页](../README.md)、[扫码登录快速开始](quick-start/netease-login.md) | 本地开发入口、独立数据目录、扫码、恢复、退出与排错 |
| 如何使用播放器 | [日常播放快速开始](quick-start/netease-playback.md) | 搜索、歌单、队列、歌词、历史/恢复、共享运行库配置 |
| 播放器与运行库如何工作 | [音乐与播放当前契约](reference/netease-music-playback.md) | 单曲执行、账号隔离、媒体预算、LibVLC 来源和设置 Tool |
| 歌单、队列与歌词如何工作 | [日常播放器当前实现](reference/netease-daily-player.md) | 四模式、定位、歌词、历史、静默恢复与页面寿命 |
| 界面如何工作 | [Document / Tool 当前界面契约](reference/netease-desktop-ui.md) | 紧凑布局、深浅主题、动效、偏好存储与资源所有权 |
| V5 交互与 UI 依据什么优化 | [V5 轻量交互与 UI 优化方案](roadmap/netease-v5-lightweight-interaction-and-ui-plan.md) | 官方与开源客户端对照、逐页交互、视觉规范、资源预算和实施验收计划；D 验收及交互差项未收口 |
| 登录与请求如何工作 | [HTTP 与会话契约](reference/netease-http-session.md) | 微信/App 登录、Flurl、端点、会话、取消和生命周期 |
| 后续有哪些候选能力 | [能力路线图](roadmap/netease-capability-roadmap.md) | 已完成 M0–M2，待讨论的 M3–M5 范围与依赖 |
| 上游还有哪些能力 | [api-enhanced 能力清单](reference/netease-api-enhanced-capabilities.md) | 固定提交的模块索引与移植边界；不代表全部已支持 |

V 编号表示实施文档序号，M 编号表示能力里程碑，均不是插件包版本。当前以已验收的 Windows x64 日常播放器为基线。

## 开发参考

| 文档 | 用途与适用范围 |
| --- | --- |
| [项目、Host 与 Standalone 职责](reference/project-and-window-responsibilities.md) | 项目结构、唯一业务实现、公开 SDK、资源所有权和验证分工 |
| [公共资源与插件图标](reference/plugin-icons.md) | 当前入口图标、注册模式、页面使用和私有依赖声明 |
| [Workbench Command](reference/workbench-commands.md) | 接入参考；当前未注册命令或快捷键 |
| [Workflow Action](reference/workflow-actions.md) | Provider / Consumer 接入示例；当前未选择或注册任一角色 |

业务与界面放在 Plugin，Standalone 和 Tests 复用同一实现。稳定插件身份为 `myavalonia.plugin.music.netease`；manifest 由 Build 包生成。

## 验证与维护

| 文档 | 用途 |
| --- | --- |
| [登录验证矩阵](maintenance/netease-login-verification.md) | 协议、HTTP、状态、存储、UI、微信专项测试与人工回归方法 |
| [V2 / M1 播放验证矩阵](maintenance/netease-v2-m1-playback-verification.md) | 搜索、播放、路径/Tool、Dock、共享运行库与平台边界；页面寿命已同步 V4 |
| [V3 界面验证矩阵](maintenance/netease-v3-ui-verification.md) | 主题、尺寸、动效、资源回收与实机回归方法 |
| [V4 / M2 日常播放器验证矩阵](maintenance/netease-v4-m2-daily-player-verification.md) | V4 历史完整检查：M2 场景、M1/V3 回归、门禁自测与实机回归方法 |
| [V5 交互与 UI 专用验证计划](maintenance/netease-v5-ui-interaction-verification-plan.md) | 现行 V5 场景、单元/组件/资源检查、门禁失败注入与证据要求 |
| [V5 专用实施记录](maintenance/netease-v5-ui-interaction-implementation.md) | 实现差异、自动证据、资源对比、SOLID 分工和实机边界 |
| [V5 完成度重新核查](maintenance/netease-v5-completion-audit-20260923.md) | 未完整实现的体验、未达标/待验证项、可选增强与待用户决定的范围 |
| [V5 启动故障修复](archive/records/netease-v5/startup-fix-20260923.md) | 注册所有权修复、309 项回归、实际 Host 启动前后复测及重新部署 |
| [开发部署、正式发布与验收](maintenance/deployment-and-release.md) | 私有依赖、干净部署目录、正式 ZIP 和真实 Host 回归流程 |

以上矩阵继续作为后续回归依据，V1–V4 手工验收已收口。自动测试、用户手工验收和部署分别引用各自记录；正式发布与跨平台适配不包含在本次完成结论中。

当前开发不使用 AIFLOW、Windows CI 或发布门禁。

## 已完成方案与历史记录

V1–V4 方案统一移入 [archive/plans](archive/README.md)，设计细节保留供追溯，当前行为以 `reference` 为准。

- [验收收口与文档整理记录](archive/records/netease-v1-v4-acceptance-20260923.md)：本次用户确认、文档归位与漂移修正。
- [V4 分阶段实施记录](archive/records/netease-v4/m2-implementation-20260923.md)：功能、自动门禁、截图和当时的验证边界。
- [V4 历史开发部署与清理记录](archive/records/netease-v4/redeployment-20260923-1609.md)：无原生 libVLC 的 12 文件部署与备份；标准清理约 1.91 GiB，另列受自动审批限制的剩余项。
- [完整归档索引](archive/README.md)：V1 登录、V2 播放、V3 界面与 V4 日常播放器的方案及记录。

旧记录中的“未验收”“未启动 Host”等描述仅代表当次操作，不覆盖后续用户验收结论；原始测试报告和部署摘要保持原样。

## 文档目录与维护约定

```text
docs/
├─ README.md          # 唯一总导航
├─ quick-start/       # 当前可执行的使用与开发步骤
├─ roadmap/           # 当前能力路线与尚未实施的候选范围
├─ reference/         # 当前契约、SDK 接入参考、固定上游调研
├─ maintenance/       # 可复用的回归验证、部署与发布说明
└─ archive/
   ├─ README.md       # 已完成方案与历史记录索引
   ├─ plans/          # 已完成方案及历史设计快照
   └─ records/        # 带日期的实施、验证、部署与验收记录
```

- 当前实现更新 `reference`，使用步骤更新 `quick-start`，后续候选更新 `roadmap`。
- 已实现且验收收口的版本方案进入 `archive/plans`，可复用验证矩阵继续留在 `maintenance`。
- 单次测试数量、源码基线、TRX、部署摘要与验收依据保存在对应记录中。
- 迁移时同步所有相对链接；历史记录只补导航和后续状态入口，不改写原始结果。
- 仅整理文档时执行以下检查，无需完整业务门禁：

```powershell
. ./tools/DevelopmentChecks.ps1
Assert-MarkdownLinks (Get-Location).Path
git diff --check
```
