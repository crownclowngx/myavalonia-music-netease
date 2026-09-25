# MusicNetEasePlugin · 网易云音乐插件

基于 Avalonia 的网易云音乐插件，使用 C# + Flurl 直连，支持微信 / 网易云 App 扫码登录、搜索、喜欢歌曲、收藏与管理自有歌单、共享播放队列、四种播放模式、进度定位、逐行 / 翻译 / 逐字歌词、最近播放和按账号静默恢复。歌词与队列共用右侧抽屉，提供宽度记忆、队列拖动、单步撤销和阅读密度设置。

当前支持 **Windows x64**。正式交付项目为 Plugin，Standalone 复用生产界面与业务实现。

## 从这里开始

| 目的 | 入口 |
| --- | --- |
| 查看项目状态、待验事项和全部文档 | [文档总导航](docs/README.md) |
| 构建、扫码、恢复账号 | [登录快速开始](docs/quick-start/netease-login.md) |
| 播放、歌单、队列、歌词和共享 LibVLC | [播放器快速开始](docs/quick-start/netease-playback.md) |
| 理解项目结构与 Host 边界 | [项目与窗口职责](docs/reference/project-and-window-responsibilities.md) |
| 运行检查、定位验证证据 | [验证指南](docs/maintenance/verification.md) |
| 查看 V5 / V6 尚待完成的验收 | [性能与实机待验清单](docs/maintenance/netease-v5-v6-acceptance.md) |
| 部署插件或生成正式包 | [部署与发布](docs/maintenance/deployment-and-release.md) |
| 查看后续候选能力 | [能力路线图](docs/roadmap/netease-capability-roadmap.md) |
| 使用和维护 M3 音乐库管理 | [V7 已实施方案](docs/archive/plans/netease-v7-m3-music-library-management-plan.md)、[专用验证矩阵](docs/maintenance/netease-v7-m3-music-library-verification-plan.md) |
| 按计划实施 M4 音乐发现 | [V8 实施计划](docs/roadmap/netease-v8-m4-music-discovery-plan.md)、[V8 专用验证矩阵](docs/maintenance/netease-v8-m4-music-discovery-verification-plan.md) |

## 当前状态

- V1–V4 / M0–M2 已实现，用户已确认手工验收完成。
- V6 的 18 项交互增强已实现并通过本地开发验证，已完成指定 Controls 部署与 Host 启动复测；性能、Dock、DPI、物理输入、读屏及真实出声等完整验收仍单列。
- V5 复核中的 C01–C08 已由 V6 接续实现，D01–D05 性能及实机待验仍保留；M4–M5 尚未实施，正式 ZIP 尚未发布。
- V7 / M3 的喜欢、收藏与歌单管理已实现；默认本地门禁为 V7，保留全部适用旧版回归。真实账号、官方令牌链与 Host 验收仍待执行，见[音乐库契约](docs/reference/netease-music-library-management.md)和[实施记录](docs/archive/records/netease-v7/m3-implementation-20260924.md)。
- V8 / M4 的推荐、榜单、歌手 / 专辑、私人 FM 和远端记录已编写实施计划及专用验证矩阵，业务、V8 门禁和真实验收尚未实施；当前命令仍使用 V7。

V / M 编号表示方案序号与能力里程碑，不是插件包版本。详细状态和依据统一见[文档导航](docs/README.md#当前状态与待验范围)。

## 本地开发

需要 .NET SDK 10、PowerShell 7，在仓库根目录执行：

```powershell
pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V7
dotnet run --project src/MusicNetEasePlugin.Standalone -c Debug --no-build
```

默认检查执行锁定还原、Debug 构建、全量测试及证据校验；登录和播放需要另行在界面中操作。Standalone 默认使用独立数据目录，自定义路径见[登录指南](docs/quick-start/netease-login.md)。清理 bin/obj 后需要重新构建。

仅修改文档时使用[文档检查](docs/maintenance/verification.md#仅修改文档)。当前本地开发不使用 AIFLOW、Windows CI 或发布门禁。

项目许可见 [LICENSE](LICENSE)，上游参考与第三方声明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
