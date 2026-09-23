# MusicNetEasePlugin

Avalonia 网易云音乐插件，使用 C# + Flurl 直连。当前提供微信扫码登录、网易云 App 备用入口、
账号核验及受保护会话管理，并已实现搜索、我的歌单、共享队列、四种播放模式、进度定位、
逐行/翻译/逐字歌词、最近播放和按账号静默恢复。真实账号 MP3 已解码验证；扬声器听感及真实 Host/Dock 验收仍待完成。

正式交付项目为 Plugin，Standalone 复用同一套服务和界面；当前目标为 Windows x64。

- [文档总导航](docs/README.md)
- [能力路线图](docs/roadmap/netease-capability-roadmap.md)：搜索播放 → 歌单、队列与歌词 → 音乐库管理 → 推荐发现
- [V4：M2 日常播放器实施指导](docs/roadmap/netease-v4-m2-daily-player-plan.md)与[专用验证矩阵](docs/maintenance/netease-v4-m2-daily-player-verification.md)：功能及完整本地门禁已实现，实机验收单列；[当前契约](docs/reference/netease-daily-player.md)与[逐阶段记录](docs/archive/records/netease-v4/m2-implementation-20260923.md)
- [V3：Document / Tool 紧凑桌面界面改造](docs/roadmap/netease-v3-desktop-ui-and-theme-plan.md)：实现完成，深浅主题、紧凑布局与轻量动效已纳入本地开发验证
- [V3 当前界面契约](docs/reference/netease-desktop-ui.md)、[专项验证](docs/maintenance/netease-v3-ui-verification.md)与[实施记录](docs/archive/records/netease-v3/ui-implementation-20260923.md)：局部主题、减少动态效果、截图与剩余实机验收
- [日常播放快速开始](docs/quick-start/netease-playback.md)与[当前播放契约](docs/reference/netease-music-playback.md)：歌单/队列、歌词、静默恢复、共享 LibVLC 和排错
- [V2：M1 搜索与单曲播放方案](docs/roadmap/netease-v2-m1-playback-plan.md)与[专用开发验证矩阵](docs/maintenance/netease-v2-m1-playback-verification.md)：实现进度及剩余人工验收
- [V2：LibVLC 路径、Tool 与 Dock 专项设计](docs/roadmap/netease-v2-libvlc-tool-and-dock-design.md)：内置优先、可指定共享目录，参考 VideoSecurityPlayer 的 Dock 与资源经验
- [扫码登录快速开始](docs/quick-start/netease-login.md)：登录、恢复、退出与排错
- [HTTP 与会话契约](docs/reference/netease-http-session.md)：当前实现与接口边界

在仓库根目录执行本地开发检查并启动 Standalone：

```powershell
pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V4
dotnet run --project src/MusicNetEasePlugin.Standalone -c Debug --no-build
```

验证范围见[维护矩阵](docs/maintenance/netease-login-verification.md)，实际操作证据见[历史记录索引](docs/archive/README.md)。
当前开发不使用 AIFLOW、Windows CI 或发布门禁；部署与正式发布流程单独维护。
