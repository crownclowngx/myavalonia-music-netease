# 开发验证指南

> 命令在插件仓库根目录执行，需要 PowerShell 7 和 .NET SDK 10。默认完整检查为 V8；脚本事实见 [verify-development.ps1](../../tools/verify-development.ps1)。

## 仅修改文档

修改导航、说明、链接或归档位置时执行：

```powershell
. ./tools/DevelopmentChecks.ps1
Assert-MarkdownLinks (Get-Location).Path
git diff --check
```

链接检查覆盖根 README、第三方声明和 `docs` 内所有 Markdown，核对仓库内相对路径与 Markdown 标题锚点；邻仓链接单列，不要求邻仓存在。它不检查外部网站可用性或文字与实现是否一致。迁移文件后还需检查入站链接、出站链接与导航覆盖，方法见[文档维护约定](documentation.md)。

纯文档变更无需运行完整业务门禁。历史测试报告、TRX、JSON、日志、截图不因整理导航而重新生成。

## 完整本地开发检查

```powershell
pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V8
```

此命令依次运行门禁自测、locked restore、Debug 零警告构建、全量测试，以及场景映射、TRX、证据身份 / 时间 / 摘要、静态资源、文档和 Git 空白检查。开始和结束还会比较源码版本与指纹。

结果写入 `TestResults/NetEaseV8/<runId>/`，包括 `verification.json`、`netease-login-development.trx`、截图、布局 / 手势、FM / 发现、资源和原生解码证据。持久证据归档到 `docs/archive/records/<版本>/<本次运行>/`，保留原始字节和来源身份；本地输出目录可被清理，不作为永久链接。

该检查不连接网易业务、不读取用户保存会话、不启动真实 Host，不向系统音频设备出声。原生 LibVLC 离线解码通过不等于真实扬声器播放通过。

## 专项检查与回归

`-Milestone` 选择额外证据校验范围；脚本中的 `dotnet test` 仍运行整个测试项目，不按里程碑过滤用例。完整改动验证使用 V8。

| 参数 | 增加的检查与说明 |
| --- | --- |
| `Login` | 通用构建、全量测试、TRX 与文档；[登录矩阵](netease-login-verification.md) |
| `M1` | 在前项上核验搜索、单曲播放、运行库和原生证据；[M1 矩阵](netease-v2-m1-playback-verification.md) |
| `V3` | 再核验界面、主题、动效与资源；[V3 矩阵](netease-v3-ui-verification.md) |
| `V4` | 再核验 M2 歌单、队列、歌词、定位、恢复与寿命；[M2 矩阵](netease-v4-m2-daily-player-verification.md) |
| `V5` | 再核验交互、图片缓存 / 租约、静态资源与布局；[V5 矩阵](netease-v5-ui-interaction-verification-plan.md) |
| `V6` | 再核验抽屉、手势、焦点、反馈、密度和单步撤销；[V6 矩阵](netease-v6-drawer-and-interaction-verification-plan.md) |
| `V7` | 再核验音乐库管理、46 场景、16 张截图、一致性 / 寿命 / 12 布局组合；[V7 矩阵](netease-v7-m3-music-library-verification-plan.md) |
| `V8`（默认） | 再核验音乐发现、50 场景、16 张截图、60 布局组合、FM 预算 / 竞争 / 反馈、账号 / 恢复及远端记录；[V8 矩阵](netease-v8-m4-music-discovery-verification-plan.md) |

场景到真实测试方法的映射见 [M1](../../tools/m1-test-map.json)、[V3](../../tools/v3-test-map.json)、[M2](../../tools/m2-test-map.json)、[V5](../../tools/v5-test-map.json)、[V6](../../tools/v6-test-map.json)、[V7](../../tools/v7-test-map.json)、[V8](../../tools/v8-test-map.json)。映射及原始产物共同证明覆盖，不能仅以测试总数代替场景结果。

仅调试门禁脚本的失败注入时可运行：

```powershell
pwsh -NoProfile -File tools/test-development-gate.ps1
```

自测会构造失败、缺字段、旧证据等情形；通过不表示生产业务测试已执行。完整验证入口会自动运行它。

## V7 音乐库管理验证

[V7 当前契约](../reference/netease-music-library-management.md)与[验证矩阵](netease-v7-m3-music-library-verification-plan.md)覆盖喜欢 / 收藏 / 歌单编辑、回查、部分结果、账号隔离、生产 UI 及失败注入。实际方法映射和证据已接入；V7 路径继续强制核验 V6 与全部适用前置，新增根 `Directory.Build.targets` 也纳入源码指纹。

[实施记录](../archive/records/netease-v7/m3-implementation-20260924.md)保存当次数量与原始证据。真实账号、WebView2 官方令牌和 Host 按[H01–H05 记录](../archive/records/netease-v7/acceptance-20260924.md)验收，不进入默认离线门禁，不使用 Windows CI 或发布门禁。

## V8 音乐发现验证

[V8 当前契约](../reference/netease-music-discovery.md)与[专用验证矩阵](netease-v8-m4-music-discovery-verification-plan.md)覆盖推荐 / 榜单、歌手 / 专辑、私人 FM 和远端记录。50 个自动业务场景与 6 组门禁自测已经接入；重点核验来源 / 完整性、作品身份、FM 单次推进 / 有限补取 / 不喜欢写入，以及远端与本地历史隔离。

默认支持 `-Milestone V8`，继承 V7 及全部适用前置。[实施记录](../archive/records/netease-v8/m4-implementation-20260925.md)保存实际命令、源身份和原始证据；6 组[真实验收](../archive/records/netease-v8/acceptance-20260925.md)仍待执行。本轮不使用 AIFLOW、Windows CI 或发布门禁。

## Standalone 与真实环境

通过完整检查后可打开独立工作台：

```powershell
dotnet run --project src/MusicNetEasePlugin.Standalone -c Debug --no-build
```

Standalone 复用生产 View 和模型，适合登录、播放、主题及尺寸检查；独立数据目录和联网探针见[登录指南](../quick-start/netease-login.md)，真实账号播放探针见[播放指南](../quick-start/netease-playback.md#开发验证与运行)。清理构建输出后不可继续使用 `--no-build`，需要重新构建。

真实 Host 加载、Dock、多窗口、DPI、物理输入、读屏和声卡需按[部署验收](deployment-and-release.md#真实-host-最小验收)及[V5 / V6 待验清单](netease-v5-v6-acceptance.md)记录。Standalone、Headless 截图、离线 PCM 和 Host 启动各有证明范围，不能相互替代。

## 已有证据与发布边界

- [V1–V4 手工验收](../archive/records/netease-v1-v4-acceptance-20260923.md)：用户已确认的范围。
- [V5 历史复核](../archive/records/netease-v5/completion-audit-20260923.md)：当时的 C / D / E 清单；C01–C08 的后续实现由 V6 承接，D 待验仍保留。
- [V6 实施记录](../archive/records/netease-v6/drawer-and-interaction-implementation-20260923.md)：18 项落地、自动验证及实机边界。
- [V6 最近部署](../archive/records/netease-v6/deployment-20260923.md)：2026-09-23 完整检查重跑、Controls 部署与实际 Host 启动；原始报告和部署 / 启动结果分别保存。

以上是历史证据，不表示在本次工作区重新运行过。当前本地开发不使用 AIFLOW、Windows CI 或发布门禁；正式 ZIP 按[发布流程](deployment-and-release.md#正式发布-zip)另行执行。
