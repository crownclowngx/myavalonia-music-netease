# MusicNetEasePlugin 文档导航

> 本插件唯一文档总导航；更新日期：2026-09-23。
> 当前基线：登录、搜索、歌单、共享队列、四模式、定位、歌词、本地历史/静默恢复与设置 Tool 已实现；完整 V4 本地自动门禁可运行。
> 下一验收阶段：V4 / M2 真实听感、Host/Dock、重启及翻译样本；M1/V3 的剩余实机项目仍保留。

## 使用与后续开发

| 我想了解 | 文档 | 内容 |
| --- | --- | --- |
| 如何构建与运行 | [项目首页](../README.md)、[扫码登录快速开始](quick-start/netease-login.md) | 本地开发入口、独立数据目录、扫码、恢复、退出与排错 |
| 下一步开发什么 | [能力路线图](roadmap/netease-capability-roadmap.md) | M1–M5 能力范围、先后依赖、验收标准与首轮任务拆分 |
| 如何播放与配置运行库 | [日常播放快速开始](quick-start/netease-playback.md)、[当前播放契约](reference/netease-music-playback.md) | 歌单、队列、歌词、历史/恢复、共享路径与探针 |
| 如何实现 M1 | [V2：搜索与单曲播放方案](roadmap/netease-v2-m1-playback-plan.md) | SOLID 分工、S0–S5 进度与剩余验收 |
| 如何实施 M2 日常播放器 | [V4：M2 日常播放器实施指导](roadmap/netease-v4-m2-daily-player-plan.md) | V4.0–V4.5 功能与 V4.6 自动收口完成；真实验收保持单列 |
| 如何优化当前 Document 和 Tool | [V3：紧凑桌面界面改造方案](roadmap/netease-v3-desktop-ui-and-theme-plan.md) | 已实现；现代紧凑布局、双色资源与轻量动效，真实 Dock/性能验收继续跟踪 |
| 当前界面如何工作 | [V3 当前界面契约](reference/netease-desktop-ui.md) | 布局断点、局部主题、动效寿命、偏好存储与 SOLID 分工 |
| V4 已实现哪些增量 | [日常播放器当前实现](reference/netease-daily-player.md) | 歌单、共享队列、账号隔离、定位、歌词、历史与静默恢复当前契约 |
| 多个插件如何使用 LibVLC | [V2：运行库、Tool 与 Dock 专项设计](roadmap/netease-v2-libvlc-tool-and-dock-design.md) | 内置优先/指定目录、设置生效、VideoSecurityPlayer 踩坑对照；共享目录已解码，真实共存待验收 |
| 当前具体实现了什么 | [HTTP 与会话契约](reference/netease-http-session.md) | 微信/App 登录、已接入端点、Flurl、会话、取消和生命周期 |
| 上游还有哪些能力 | [api-enhanced 能力清单](reference/netease-api-enhanced-capabilities.md) | 固定提交的模块索引与移植边界；不代表插件已支持 |

当前以登录可用为后续开发起点。真实微信闭环已有记录；恢复、远端退出及 Host 等项目的证据覆盖范围，以[验证矩阵](maintenance/netease-login-verification.md)和当次记录为准，不将用户的整体使用反馈扩写成逐项实测结论。

## 开发参考

| 文档 | 用途与适用范围 |
| --- | --- |
| [项目、Host 与 Standalone 职责](reference/project-and-window-responsibilities.md) | 项目结构、唯一业务实现、公开 SDK、资源所有权和验证分工 |
| [公共资源与插件图标](reference/plugin-icons.md) | 当前入口图标、注册模式、页面使用和私有依赖声明 |
| [Workbench Command](reference/workbench-commands.md) | 未来工作台命令接入参考；当前未注册命令或快捷键 |
| [Workflow Action](reference/workflow-actions.md) | Provider / Consumer 接入示例；当前未选择或注册任一角色 |

业务与界面放在 Plugin，Standalone 和 Tests 复用同一实现。稳定插件身份为 `myavalonia.plugin.music.netease`；manifest 由 Build 包生成。新增依赖、Host 部署和 ZIP 交付按下方维护说明执行。

## 验证与维护

| 文档 | 用途 |
| --- | --- |
| [登录开发验证矩阵](maintenance/netease-login-verification.md) | 协议、HTTP、状态、存储、UI、微信专项测试与人工验证范围；本地开发检查入口 |
| [V2 / M1 专用开发验证矩阵](maintenance/netease-v2-m1-playback-verification.md) | 搜索、播放、路径/Tool、Dock、共享运行库、资源与各平台验收；自动映射和人工边界分开维护 |
| [V3 界面专用开发验证](maintenance/netease-v3-ui-verification.md) | V3 回归入口、主题与尺寸、动效/回收证据及未完成的实机矩阵 |
| [V4 / M2 日常播放器专用验证](maintenance/netease-v4-m2-daily-player-verification.md) | 默认 V4 门禁：58 个自动场景、M1/V3 回归、门禁失败注入和真实验收边界 |
| [开发部署、正式发布与验收](maintenance/deployment-and-release.md) | 私有依赖声明、干净部署目录、正式 ZIP 和真实 Host 验收；按实际任务选择流程 |

当前开发不使用 AIFLOW、Windows CI 或发布门禁。发布说明保留为正式交付时的参考；文档整理不会触发构建、部署或发布。

## 历史计划与记录

完整入口见[归档索引](archive/README.md)。原 V1 登录计划已经归档，后续能力由当前路线图承接；尚未覆盖的验证项继续在维护矩阵跟踪。

最近一次开发部署见[V3 Common LibVLC 部署记录](archive/records/netease-v3/common-deployment-20260923.md)：无原生库的 V3 产物已替换指定 Controls，保留公共库及用户配置；本轮未启动 Host。此前[M1 公共库配置](archive/records/netease-v2/shared-libvlc-deployment-20260923.md)、微信与 App 版记录保留在归档中。

最新播放器实施见[V4 分阶段实现与验证记录](archive/records/netease-v4/m2-implementation-20260923.md)，包含阶段提交、自动门禁、截图审阅与真实验收剩余项，本轮未部署。此前界面见[V3 记录](archive/records/netease-v3/ui-implementation-20260923.md)，初始播放能力见[V2 / M1 记录](archive/records/netease-v2/m1-implementation-20260923.md)。

## 文档目录与维护约定

```text
docs/
├─ README.md          # 唯一总导航
├─ quick-start/       # 当前可执行的使用与开发步骤
├─ roadmap/           # 当前能力路线与待实施方案
├─ reference/         # 当前契约、SDK 接入参考、固定上游调研
├─ maintenance/       # 可复用的验证、部署与发布说明
└─ archive/
   ├─ README.md       # 历史索引与承接关系
   ├─ plans/          # 已归档方案，保留当时的设计与待办快照
   └─ records/        # 带日期的实施、验证及部署证据
```

- 当前实现更新 `reference`，使用步骤更新 `quick-start`，后续范围与优先级更新 `roadmap`。
- 验证方法与剩余覆盖项维护在 `maintenance`；测试数量、源码基线、TRX 和部署摘要只保留在当次记录中。
- 方案归档时，先把剩余功能和验证项移交路线图或维护矩阵；归档不意味着所有验证或正式发布已完成。
- 迁移文档时同步所有相对链接，删除已合并的空迁移提示；历史记录只修正导航，不改写原始结果。
- 仅整理文档时，在仓库根目录执行下面的文档检查，无需运行完整业务门禁：

```powershell
. ./tools/DevelopmentChecks.ps1
Assert-MarkdownLinks (Get-Location).Path
git diff --check
```
