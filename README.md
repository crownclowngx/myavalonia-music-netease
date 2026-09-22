# MusicNetEasePlugin

Avalonia 网易云音乐插件，使用 C# + Flurl 直连。当前提供微信扫码登录、网易云 App 备用入口、
账号核验及受保护会话管理。用户已确认登录可用，下一阶段推进搜索与播放。

正式交付项目为 Plugin，Standalone 复用同一套服务和界面；当前目标为 Windows x64。

- [文档总导航](docs/README.md)
- [能力路线图](docs/roadmap/netease-capability-roadmap.md)：搜索播放 → 歌单、队列与歌词 → 音乐库管理 → 推荐发现
- [V2：M1 搜索与单曲播放方案](docs/roadmap/netease-v2-m1-playback-plan.md)与[专用开发验证矩阵](docs/maintenance/netease-v2-m1-playback-verification.md)：LibVLCSharp + LibVLC、账号设置 Tool 与跨平台音频；待实施
- [V2：LibVLC 路径、Tool 与 Dock 专项设计](docs/roadmap/netease-v2-libvlc-tool-and-dock-design.md)：内置优先、可指定共享目录，参考 VideoSecurityPlayer 的 Dock 与资源经验
- [扫码登录快速开始](docs/quick-start/netease-login.md)：登录、恢复、退出与排错
- [HTTP 与会话契约](docs/reference/netease-http-session.md)：当前实现与接口边界

在仓库根目录执行本地开发检查并启动 Standalone：

```powershell
pwsh -NoProfile -File tools/verify-development.ps1
dotnet run --project src/MusicNetEasePlugin.Standalone -c Debug --no-build
```

验证范围见[维护矩阵](docs/maintenance/netease-login-verification.md)，实际操作证据见[历史记录索引](docs/archive/README.md)。
当前开发不使用 AIFLOW、Windows CI 或发布门禁；部署与正式发布流程单独维护。
