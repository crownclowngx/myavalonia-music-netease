# V2 / M1 搜索、单曲播放与 LibVLC 设置实现记录

> 日期：2026-09-23，Windows x64 本地开发。实施起点：`cb061f3`（V2 路径/Tool/Dock 设计），登录基线：`c469f91`；本记录随实现提交，具体提交见 Git 日志。
> 当前结论：核心功能已实现，自动回归及真实账号 MP3 解码通过；M1 真实扬声器、Standalone 操作、Host/Dock 与双插件共存尚未完成验收。
> 方案：[V2 主方案](../../../roadmap/netease-v2-m1-playback-plan.md)；契约：[音乐与播放](../../../reference/netease-music-playback.md)；步骤：[快速开始](../../../quick-start/netease-playback.md)。未使用 AIFLOW、Windows CI 或发布门禁。

## 1. 实际实现

- 原生 C# + Flurl 的歌曲搜索、30 条分页、详情和 standard 资源解析；xeapi 公钥签名/解密、X25519 密钥封装、账号内协议会话复用。
- 受控音乐会话快照，epoch/凭据版本/撤销 Token；Cookie 更新继续走唯一登录协调器，退出与迟到保存按原有顺序收口。
- 64 MiB / 响应头 15 秒 / 空闲 15 秒 / 总计 120 秒的无 Cookie 媒体下载；逐跳来源检查、有限重取、临时文件和独占目录残留清理。
- 插件进程内 LibVLC 单曲播放、暂停、继续、停止、音量、只读进度、试听与错误反馈；快速切歌和旧原生事件隔离，发起 Document 的关闭所有权。
- 账号与播放设置 Tool：登录摘要、恢复/重试保存/退出、路径选择/检测/保存/清空、候选与实际来源。有效内置优先，可指定共享目录，原生加载后保存需重启。
- 生产音乐 View、封面占位、可撤销订阅、Tool 单例草稿；Standalone 使用同一实现的音乐/设置页签。
- `-Milestone M1` 本地门禁、完整方法/参数化映射、失败注入自测、Headless 渲染和原生解码证据。

锁定依赖：LibVLCSharp 3.10.0、VideoLAN.LibVLC.Windows 3.0.23.1（实际原生 3.0.23 Vetinari）、BouncyCastle.Cryptography 2.6.2。更新了 Plugin/Standalone/Tests/Probe lock 文件和私有依赖/原生资产声明。

## 2. 设计审查及修正

| 项目 | 结论 |
| --- | --- |
| D01：SOLID 与依赖方向 | Application 通过 API/媒体/音频/会话端口协作；ViewModel 不操作 HTTP、Cookie、文件或 LibVLC。Infrastructure 隔离加密、Flurl、原生与文件；组合入口唯一 |
| D02：朴素设计与中文注释 | 使用普通类、记录、短锁、异步门和尾任务，没有通用框架。关键代次、保存顺序、回调、Dock、原生绑定和文件所有权写有中文设计说明 |
| D03：敏感数据与持久化 | 音乐会话和临时资源重写 ToString；安全异常不携带底层链；媒体 Client 不带账号头。探针只输出摘要，受保护会话副本和媒体都在专用临时目录清理 |
| D04：文档状态 | 当前契约/快速开始/导航/路线/验证矩阵及本记录同步；M1 核心代码完成与 R 类验收待办明确区分 |

两项实际调整：

1. SDK 3.4.1 没有 Tool 主动打开指定 Document 的公开运行时端口，改为提示从“新建”进入原登录页，保留 Tool 的恢复和退出能力；未引用 Host 内部服务。
2. 引擎按容器复用，每首歌重建 MediaPlayer/Media，使旧事件自然携带旧代次。真实原生解码首次运行暴露在事件回调中查询 Time/Length 的启动超时；改为使用事件参数缓存后复测通过。停止、释放和等待 UI 都不在原生回调里执行。

参考 VideoSecurityPlayer `4549c44` 的 Dock 与资源经验，未改动视频仓库。额外核对 [LibVLCSharp 3.10.0 Core](https://github.com/videolan/libvlcsharp/blob/3.10.0/src/LibVLCSharp/Shared/Core/Core.cs) 的按名原生导入：本插件为自己的私有 LibVLCSharp 绑定选定句柄，避免多个同名 DLL 的来源漂移。未设置全局 DLL 搜索目录或替换其他插件对象。

全量并行测试还暴露了两个 Headless App 同时修改 Avalonia 全局注册表的问题；将实际 UI 测试置于同一串行测试集合，业务/协议测试仍可并行。

## 3. 自动验证

实施前登录基线门禁：92 项通过、0 跳过，Debug 零警告；证据 `TestResults/NetEaseLogin/20260922-233553-f1ecbd3d/`。

首次全量 M1 门禁：152 项通过、0 失败、0 跳过；证据 `TestResults/NetEaseM1/20260923-002840-72c2bc77/`。其后补充了系统选择器迟到返回、布局和门禁证据校验；最终复核结果登记在收口段。

实际套件与方法：[维护矩阵](../../../maintenance/netease-v2-m1-playback-verification.md)及[方法映射](../../../../tools/m1-test-map.json)。映射含旧登录回归方法，验证 TRX 的真实执行和参数化条数；P/A/C/B/L/E/T/U 编号表示覆盖对应可自动部分，不能替代每种真实设备/系统条件。

离线夹具为测试现场生成的 440 Hz、48 kHz、单声道、PCM16 WAV，0.4 秒；SHA-256：`A828F64EA8C8CD3EBE5C82F9D37A898A5D95044E6A4D8B6B98B193CD61168827`。生产适配连续 12 次打开，每次得到 19200 PCM 帧，样本含非零值；每次停止后均能独占打开并删除文件。坏/空媒体受控失败。

首次门禁原生记录中，预热后三至十轮进程句柄为 632，线程为 35，末轮句柄 634；这是含其他并行单测的进程观测，不是纯 Host 的资源泄漏证明。详细每轮数值保留在 `libvlc-offline.json`；R13 的真实 Host 趋势仍待执行。

Headless 直接渲染真实 View，完成 20 次播放态重挂、暂停态重挂、Tool View 重建、草稿保留、选择器防重入及 detach/rebind/草稿变化后的迟到结果拒绝。渲染图为 `m1-music-and-settings.png`（1080×880）和 `m1-compact.png`（760×720），人工检查了换行、占位与滚动可达性。Headless 不代替真实 Dock。

## 4. 真实账号与共享目录

显式运行 `MusicNetEasePlugin.LoginProbe --music`，关键词“晴天”，读取既有受保护会话；刷新仅在探针内存保留。账号标识、Cookie、签名 URL 和音频未写入报告或仓库。

| 证据 | UTC 时间 | 观察 |
| --- | --- | --- |
| `TestResults/music-online-initial.json` | 2026-09-23 00:28:37 | 搜索 30 条，详情身份匹配，MP3 / standard / 非试听，52677 PCM 帧，停止成功；实际来源 BuiltIn，原生 3.0.23 |
| `TestResults/music-online-shared-video.json` | 2026-09-23 00:31:00 | 相同业务闭环及帧数，实际来源 Configured，使用 `D:/data/avalonia/Controls/VideoSecurityPlayer/native/win-x64/libvlc`，原生 3.0.23 |

两次都使用生产音频适配的 PCM 回调，`actualDeviceOutput=false`、`hostVerified=false`。它们证明真实资源下载/解码和指定来源，不证明扬声器可听输出，也没有让两个插件在同一进程同时播放。

## 5. 开发产物与未执行项

通过 Debug 的 `DeployManagedPlugin` 目标生成暂存目录；没有构建发布 ZIP、运行 Windows CI 或发布门禁，也没有替换正在运行的 `D:/data/avalonia` Host。

| 布局 | 暂存根 | 初次核对 |
| --- | --- | --- |
| 默认内置 | `artifacts/m1-development-20260923/builtin/Controls/MusicNetEasePlugin` | 437 个文件，含完整原生目录 |
| 指定共享目录 | `artifacts/m1-development-20260923/external-only/Controls/MusicNetEasePlugin` | 12 个文件，无 native 目录；保留 LibVLCSharp、BouncyCastle 等私有托管依赖 |

两者均未混入 Host 共享的 SDK/Avalonia/Dock 程序集。`IncludeLibVlcRuntime=false` 关闭复制/交付，不改变 NuGet 锁定依赖。完整步骤见[快速开始](../../../quick-start/netease-playback.md)。

仍待人工验收：

- R02–R05：真实扬声器听感、暂停/继续/静音/自然结束和 Standalone 关闭；探针程序控制不代替这些观察。
- R06–R08：真实 Host 加载、多页面与退出、账号退出时的真实输出、实际设备/网络失败样本。
- R09–R11：已证明两种来源可解码；真实 Tool 重启生效、两插件同时运行及两种加载顺序尚未验证。
- R12–R13：真实 Dock Playing/Paused 各 20 次停靠/浮动/隐藏，以及 Host 原生资源趋势。
- Linux/macOS 的运行库目录加载、受保护存储、设备与 Host 适配未实施/未验证。

因此 S1/S2/S3 核心实现完成，S0/S4/S5 的真实环境验收保持待办；不把整个 M1 标为已验收完成。

## 6. 最终收口

最终执行 `pwsh -NoProfile -File tools/verify-development.ps1 -Milestone M1`：

- locked restore 成功，四个项目 Debug 构建 0 警告、0 错误。
- 全量 152 项通过、0 失败、0 跳过；包含原登录 92 项及新增音乐 60 项。
- 门禁失败注入自测 29 项通过；映射校验 95 个方法、58 个自动场景，实际执行 152 个参数化用例。
- 文档检查 25 个 Markdown、14 个邻仓参考单列；`git diff --check` 通过。
- 最终证据：`TestResults/NetEaseM1/20260923-005233-19d10eb0/`。`verification.json` 的 revision 为实施起点 `cb061f3`、workingTreeDirty=true，准确描述提交前的实现工作区；自动门禁的真实账号/听感/Host/发布标记均为 false，联网证据另列于第 4 节。

收口时补充验证：停止后再退出也清除曲目/时长，加载时账号撤销不会启动旧音源；重复 Dispose 等待同一停止任务；已登录时恢复按钮保持原有幂等语义。中途新增测试曾误把恢复入口当成重新核验，已修正测试前提并重新通过完整门禁。

本记录与实现一并提交，提交标题为 `feat: 实现 M1 搜索播放与 LibVLC 共享目录设置`，正文记录功能、验证和剩余人工边界。只提交本仓源码、测试、脚本与文档，原生二进制、账号、媒体缓冲和 TestResults 不进入 Git；未推送远端。
