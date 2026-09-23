# MusicNetEasePlugin

Avalonia 网易云音乐插件，使用 C# + Flurl 直连。当前支持微信/App 扫码登录、受保护会话、搜索、我的歌单、共享队列、四种播放模式、进度定位、逐行/翻译/逐字歌词、最近播放和按账号静默恢复，并提供紧凑桌面界面与账号/播放设置 Tool。

**V6-01～18 已接入**：歌词/队列共用右侧抽屉、宽度记忆、歌词定位、准确反馈、队列拖动与单步撤销、阅读密度；实现和验证状态见 [V6 专用实施记录](docs/maintenance/netease-v6-drawer-and-interaction-implementation.md)。V5 原有性能及实机待验仍独立保留，见 [V5 完成度复核](docs/maintenance/netease-v5-completion-audit-20260923.md)。**V1–V4 已完成手工验收**，以用户确认为依据，见[验收收口记录](docs/archive/records/netease-v1-v4-acceptance-20260923.md)。正式交付项目为 Plugin，Standalone 复用生产实现；当前支持 Windows x64。

- [文档总导航](docs/README.md)：当前使用、实现契约、维护与归档入口。
- [扫码登录](docs/quick-start/netease-login.md)与[日常播放快速开始](docs/quick-start/netease-playback.md)：登录、歌单、队列、歌词、恢复和共享 LibVLC 配置。
- [播放契约](docs/reference/netease-music-playback.md)、[日常播放器](docs/reference/netease-daily-player.md)与[界面契约](docs/reference/netease-desktop-ui.md)：当前实现与边界。
- [验证与维护](docs/maintenance/netease-v6-drawer-and-interaction-verification-plan.md)：默认 V6 本地检查，保留 M1/V3/M2/V5 回归与离线原生证据。
- [已完成方案与历史记录](docs/archive/README.md)：V1–V4 设计、实施、验证及部署证据。
- [V5 轻量交互与 UI 优化方案](docs/roadmap/netease-v5-lightweight-interaction-and-ui-plan.md)：GitHub / 官方与开源客户端对照、布局与交互、资源预算、SOLID 约束及专用验证计划；主体已接入，D 验收未收口，E 为可选增强。
- [V6 修改方案](docs/roadmap/netease-v6-drawer-and-interaction-change-plan.md)：18 项全量实施范围、SOLID 分工和边界；原[候选评估](docs/roadmap/netease-v6-interaction-candidates-evaluation.md)保留取舍依据。
- [能力路线图](docs/roadmap/netease-capability-roadmap.md)：M0–M2 已完成，M3–M5 保留为后续候选，下一步另行讨论。

在仓库根目录执行本地开发检查并启动 Standalone：

```powershell
pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V6
dotnet run --project src/MusicNetEasePlugin.Standalone -c Debug --no-build
```

当前开发不使用 AIFLOW、Windows CI 或发布门禁；部署与正式发布流程见[维护说明](docs/maintenance/deployment-and-release.md)。
