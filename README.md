# MusicNetEasePlugin

Avalonia 网易云音乐插件，默认微信扫码登录，保留网易云 App 扫码：C# + Flurl 直连、二维码状态、取消、
账号核验、受保护会话保存/恢复和退出。真实交付项目是 Plugin，Standalone 复用同一套服务和界面。

```powershell
pwsh -NoProfile -File tools/verify-development.ps1
dotnet run --project src/MusicNetEasePlugin.Standalone -c Debug --no-build
```

- [文档总导航](docs/README.md)
- [扫码登录快速开始](docs/quick-start/netease-login.md)
- [HTTP 与会话契约](docs/reference/netease-http-session.md)
- [专用开发验证矩阵](docs/maintenance/netease-login-verification.md)
- [本阶段实现与验证记录](docs/archive/records/netease-v1/login-implementation-20260922.md)
- [微信登录实现、实测与部署记录](docs/archive/records/netease-v1/wechat-login-implementation-20260922.md)
- [后续计划](docs/roadmap/netease-v1-flurl-login-plan.md)与[上游 440 个模块能力清单](docs/roadmap/netease-api-enhanced-capabilities.md)

2026-09-22 已通过真实微信扫码、网易授权回调和账号核验；重启恢复、退出与真实 Host 联调仍待人工验证。
搜索、播放与歌单属于后续阶段。按用户指定路径开发部署；不使用 AIFLOW、Windows CI 或发布门禁。
