# V5 · 完成度重新核查与待决事项（2026-09-23）

> 结论：**V5 尚未正式完成，不能关闭整个 V5。** 主体功能已有实现，但存在核心体验差项，D 阶段验收尚未收口。原先“核心 A–D 已完成/已接入”的表述过强，已更正。
> 当前已完成用户授权的启动加载故障修复、309 项本地回归及真实 Host 启动前后复测；其他差项只审查记录，等待用户决定是否继续开发。
> 依据：[原 V5 方案](../roadmap/netease-v5-lightweight-interaction-and-ui-plan.md)、[验证矩阵](netease-v5-ui-interaction-verification-plan.md)、当前源码及实际证据。未重新调研外部产品，未以截图数量或测试总数替代逐项完成结论。

## 1. 当前可确认完成的部分

三项浏览导航、固定播放条、共享歌曲行与行菜单、立即播放保留队列、整单替换语义、宽窄队列切换、增量队列更新与清空确认、歌单直接进入及已加载筛选、歌词字号/翻译/跟随、最近播放本机分组、静默恢复底层、图片并发/缓存/租约预算和偏好迁移，均已有生产实现及自动回归。

这些是主要能力已落地，**不表示每个细节均达到方案，也不表示实际 Host 中全部流程已验收**。当前设置入口使用 Document 内的同模型设置页，独立 Tool 保留；这是已记录的 SDK 约束下的替代设计。

## 2. 启动故障现已修复

根因是私有 DI 重复注册 Host 拥有的 MusicSettingsTool 贡献根；已移除重复注册并保留 AddTool 声明。修复前实际 Host 报 `PLUGIN_CONTRIBUTION_SERVICE_REGISTRATION_FORBIDDEN`，修复后同一 EXE 就绪并正常退出，诊断 0 条。309 项测试、249 项门禁自测通过，已重新部署且不携带原生 libVLC，见[专项修复记录](../archive/records/netease-v5/startup-fix-20260923.md)。

这项故障已关闭，不再列为待开发项。新增注册契约测试修补了原自动门禁遗漏；仍不能因此推导其他未测的 Host 操作全部通过。

## 3. 明确未完整实现的体验

以下根据方案与当前源码逐项比较，**本轮没有擅自补做**。建议优先完成 C01–C04；C05–C08 可由用户选择实现或明确接受较简方案，不能默认为已全部实现。

| 编号 / 优先级 | 方案要求 | 当前事实与未完成点 | 依据 |
| --- | --- | --- | --- |
| C01 / P1 | §5.8 可收起的恢复提示，显示恢复首数、停留位置、就近继续 | 静默恢复已实现；目前只有“已恢复上次队列，点击继续播放”的播放消息，经“查看提示”弹层查看，没有独立的数量/时间/继续/收起提示条 | [恢复协调器](../../src/MusicNetEasePlugin.Plugin/Application/Playback/PlaybackQueueCoordinator.cs)、[播放条](../../src/MusicNetEasePlugin.Plugin/Features/Music/PlaybackBarView.axaml) |
| C02 / P1 | §5.2 区分“加入队列”和“将在当前歌曲后播放”的反馈 | 播放行为正确，但短提示只根据队列总数增长生成“已加入队列 · 共 N 首”；不能区分两种意图，也不报告本次新增数量 | [播放条模型](../../src/MusicNetEasePlugin.Plugin/Features/Music/PlayerBarWorkspace.cs) |
| C03 / P1 | §5.5 缓冲时主按钮位置显示明确加载态；不可定位有具体原因 | 目前 Loading 时仍显示禁用的暂停图标，旁边另有加载文字与缓冲数据；Slider 只有 CanSeek 禁用，缺少相邻、键盘可读的不可定位原因 | [播放条](../../src/MusicNetEasePlugin.Plugin/Features/Music/PlaybackBarView.axaml)、[定位模型](../../src/MusicNetEasePlugin.Plugin/Features/Music/TimelineWorkspace.cs) |
| C04 / P1 | §4.2 低矮布局压缩附加信息；大歌词封面按剩余内容宽度收纳 | 窄播放条的提示/缓冲占额外一行，已有 520×420 截图的提示态超过 96–112 DIP 建议高度；大封面开关按整个 MusicView 宽度判断，没有随右侧队列压缩后的实际歌词宽度退为单列 | [播放条布局](../../src/MusicNetEasePlugin.Plugin/Features/Music/PlaybackBarView.axaml.cs)、[音乐页布局](../../src/MusicNetEasePlugin.Plugin/Features/Music/MusicView.axaml.cs)、[520 截图](../archive/records/netease-v5/gate-20260923/v5-detail-dark-520.png) |
| C05 / P2 | §5.4 宽屏歌单详情的小封面、数量、按需描述区域 | 歌单列表有 40 DIP 缩略图；详情仍为文字标题/操作/完整性提示，未增加详情封面及描述展开。窄屏省略封面是允许的，宽屏也省略需明确接受取舍 | [歌单 View](../../src/MusicNetEasePlugin.Plugin/Features/Library/PlaylistView.axaml) |
| C06 / P2 | §8 约 150 ms 后呈现辅助加载态；空态有图标、原因和就近下一步 | 搜索和登录忙碌指示直接绑定状态，未实现延迟防闪；空队列/歌单/历史有文字与原有导航，尚未统一为方案中的图标和就近动作组合 | [音乐页](../../src/MusicNetEasePlugin.Plugin/Features/Music/MusicView.axaml)、[主页面](../../src/MusicNetEasePlugin.Plugin/Features/Main/MainView.axaml)、[队列](../../src/MusicNetEasePlugin.Plugin/Features/Music/QueueView.axaml) |
| C07 / P2 | §4.1 / §8 导航选中语义与进入页面时的焦点目标 | 当前三导航为 Button + selected 样式，未提供 Tab/选择项语义；返回浏览和设置/歌单有局部焦点恢复，进入歌词/队列后未统一把焦点送到标题或首个内容操作。菜单完整焦点路径和读屏还需实机验证，不断言框架默认行为一定错误 | [音乐页](../../src/MusicNetEasePlugin.Plugin/Features/Music/MusicView.axaml)、[输入后台](../../src/MusicNetEasePlugin.Plugin/Features/Music/MusicView.axaml.cs) |
| C08 / P2 | §5.5 / U02 曲目信息完整可读，长歌手/专辑有完整入口 | 播放条主信息显示歌名和播放状态，未显示已有 Artists 字段；宽歌曲行歌手/专辑列只有省略，没有完整文本提示，窄搜索/历史行也未提供专辑展开。歌曲标题本身已有提示，不列为缺失 | [歌曲行](../../src/MusicNetEasePlugin.Plugin/Features/Music/SongRowView.axaml)、[播放条](../../src/MusicNetEasePlugin.Plugin/Features/Music/PlaybackBarView.axaml) |

播放失败已有持续“播放失败/查看提示”和重试入口，歌词失败也有重试；这些不能笼统写成未实现。歌单不完整提示、队列操作的核心语义及隐藏资源回收同样已有实现，剩余的是上述具体差项和验收证据。

## 4. 性能与正式验收仍未收口

| 编号 | 状态 | 尚需完成的工作 |
| --- | --- | --- |
| D01 | 有指标未达建议值 | 同条件 Headless 暂停可见 CPU 中位数 4.60% → 6.65%，增量约 2.05 个百分点，高于 +1 建议；需要优化或以真正 Host 空闲测量明确成本并由用户接受。不能直接写“性能已达标” |
| D02 | 有限测量，非完整性能验收 | 已有强制布局/绘制条件下三轮采样、内存增量约 3.06 MiB、热态导航 p95 约 14.65 ms、隐藏分配下降；仍缺真实 Host 的空闲/播放/隐藏同音频对照、首次反馈、连续滚动帧 p95、峰值和长时间趋势。未实测 GPU/帧耗时，不以估计值填表 |
| D03 | 启动通过，交互实机待验 | 真实 Host 的 Dock 拖出/回停、浮窗、多 Document 及关闭最后页面继续播放，需用 V5 实机核验；Headless 的模型/挂载结果不能替代，V1–V4 用户验收也不自动延续 |
| D04 | 待实机验证 | 100%/125%/150%/200% DPI、长中文、深浅主题切换、各 Tool 尺寸；物理中文输入法、仅键盘完整任务和屏幕阅读器状态/焦点/名称；最终主题下对比度及核心命中区域 |
| D05 | 待真实业务与用户确认 | V5 实际账号搜索→播放→下一首→歌单返回→恢复播放，以及真实设备出声和故障恢复；仍需用户对易用性与视觉进行验收确认 |

资源硬预算的自动检查（下载并发、压缩/解码缓存、图片引用、静态资产）已通过。上表不能被解释为它们完全没有验证；缺口在测量覆盖与实机确认。[原始性能与限制](netease-v5-ui-interaction-implementation.md)保留不改写。

## 5. 已记录取舍与可选项

以下不默认算作必须新增的功能：

- 设置：SDK 3.4.1 没有公开 Tool 激活端口，采用 Document 内展示同一设置模型；独立 Tool 继续注册。此次修复只纠正注册所有权。
- 模式：使用常驻、四种明确名称的 ComboBox，替代图标菜单，避免猜测图标；属于明确取舍。
- 图片：继续 CDN 160 档、最长边解码到 256，低于 512 上限；高 DPI 清晰度需验证，但不为此默认提高下载尺寸。
- E 阶段 **封面一次性取色、平滑歌词居中、队列拖拽、带版本校验的撤销**均未实现，原方案本就可选；不影响选择只交付 A–D。
- “从这句播放”、密度偏好、Ctrl+F 为候选项，未默认纳入核心。喜欢/收藏写入、推荐、下载、桌面歌词、系统媒体键属于后续能力路线，不是 V5 欠项。
- Windows CI、正式 ZIP、签名/发布门禁按用户要求未执行，不算本次开发必须补的验收。

## 6. 阶段判定与继续建议

| 阶段 | 本次复核结论 |
| --- | --- |
| A | 视觉骨架和可复现 Headless 基线已有；真实 Host 性能基线未齐，不能笼统标为全部完成 |
| B | 高频操作、立即播放语义、统一行和播放条主体已实现；C02/C03/C08 等细节未对齐 |
| C | 导航、队列、歌词、历史、设置和有界图片主体已实现；C01/C04–C07 等仍待处理或接受简化 |
| D | **未完成**。启动故障已关闭，其余性能、Host 交互、DPI、输入与易用性仍有未通过/未测项 |
| E | 未实施，可选；不建议在 A–D 收口之前投入 |

建议下一步只选择“补齐核心交互并完成 D 验收”，先 C01–C04 和 D01–D05；C05–C08 逐项选择实现或记录接受简化，暂不扩展 E。若用户选择停止，当前状态应记为“V5 主体实现，启动修复完成，整体未验收”，不能标记正式完成或把计划移入已验收归档。

本次没有继续实现上述差项，等待用户决定。历次中间产物/旧副本清理仍有自动审批限制导致的残留，属于交付环境事项，单列在清理记录中，不伪装成 V5 功能缺项。
