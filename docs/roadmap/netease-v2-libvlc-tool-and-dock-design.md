# 网易云音乐 V2：LibVLC 路径、账号设置 Tool 与 Dock 专项设计

> 日期：2026-09-23。状态：待实施的设计补充，隶属 [V2 / M1 主方案](netease-v2-m1-playback-plan.md)，不另起里程碑。
> 依据：用户确认 LibVLCSharp + LibVLC，要求内置优先、可手动指定多个插件共用的 LibVLC 目录，并在 Tool 中展示登录信息与配置。
> 参考：VideoSecurityPlayer 工作区基线 `4549c44` 的相关源码、Dock 测试及历史记录；本次只读核对，未修改或运行视频插件。
> 验证：[V2 专用开发验证矩阵](../maintenance/netease-v2-m1-playback-verification.md)中的 E/T/U/R 场景。保持 SOLID、朴素设计和详细中文注释；不使用 AIFLOW、Windows CI 或发布门禁。

## 1. 确定的能力边界

音乐插件提供账号设置 Tool 和音乐 Document，音频引擎在 Host 进程内运行。LibVLC 原生文件允许自带或配置外部目录，不要求每个插件都附带一份；不启动外部播放器。

多个插件共用目录意味着复用同一组原生文件，不意味着共享账号、音量、播放状态、LibVLC 实例或 MediaPlayer。音乐插件仍在自己的容器中惰性创建一个 LibVLC 和一个活动播放器，换曲只替换媒体。Tool 与所有音乐 Document 观察同一套服务状态。

托管 `LibVLCSharp` 仍由音乐插件按锁定版本声明。配置项仅选择原生 LibVLC 运行库目录，不能用来注入另一套托管程序集，也不建立对 VideoSecurityPlayer 工程或内部服务的引用。

开发产物需支持携带原生库和省略原生库两种布局；资产是否携带由构建配置明确决定，运行时配置路径不进入 manifest。不带原生库时保留托管适配、登录和设置功能，等待用户配置；验收用独立开发目录验证两种布局，不移动正在被其他插件使用的运行库。

## 2. 运行库来源与配置生效

### 2.1 初始化前的选择表

| 内置目录 | 已保存的指定目录 | 选择结果与界面反馈 |
| --- | --- | --- |
| 检查有效 | 任意状态 | 使用内置；已填写的外部目录保留，显示“内置优先，配置目录未采用” |
| 不存在 | 检查有效 | 使用指定目录，显示其路径与来源 |
| 检查失败 | 检查有效 | 使用指定目录，同时保留内置检查问题，解释回退原因 |
| 不存在或失败 | 未配置或检查失败 | 播放不可用；列出问题并引导配置，登录、搜索及 Tool 仍可用 |

“检查有效”仅表示无副作用预检通过，不能提前显示“引擎已可播放”。加载原生库及首次媒体输出分别确认。选择、校验快照和首次初始化串行收口；检查 A 目录期间改成 B，A 的迟到结果不能覆盖 B。

内置路径以音乐插件入口程序集所在目录为基准，约定 `native/<rid>/libvlc/`。外部路径是用户输入或文件夹选择器返回的本机绝对目录，Windows 下应指向包含 `libvlc.dll`、`libvlccore.dll` 与 `plugins/` 的运行库根目录，而非 `vlc.exe`、单个 DLL 或 Host 根目录。不同平台按自己的原生布局验证。

目录探针检查存在性、读取能力、进程架构、主库、配套库及实际音频格式所需模块，汇总稳定问题码、检查路径和修复建议；不能只检查 `plugins/` 非空。静态无法确认的版本/依赖事实留到加载验证，不把推测写成“兼容”。从静态检查到加载之间目录可能变化，加载失败仍须受控收口。

内置不合格时的回退只发生在首次原生初始化前。若已经调用原生初始化，即便失败也可能留下进程加载状态，此后不再自动尝试另一个目录；修正并保存设置后重启 Host。普通媒体解码错误、网络错误仍可按原播放规则重试，不等同于运行库需要重启。

### 2.2 保存值与当前值

配置使用插件数据目录中的 `playback-settings.json`（拟定），最小字段为 `schemaVersion` 与可空的 `customLibVlcDirectory`。不包含 Cookie、账号密钥或播放地址；注销账号保留该设置，不与加密会话文件混存。

Tool 分别呈现：

- 正在编辑的目录草稿及对应检测结果；
- 已保存的配置目录；
- 按内置优先规则得到的下次初始化候选来源；
- 本进程已加载的实际来源、路径和可确认版本；
- 配置待重启生效、保存失败或运行库不可用状态。

“检测目录”只读检查草稿，不初始化、不出声、不写配置；无效草稿不能保存为已验证配置。保存采用原子替换，失败时保留上次值。清空配置只移除保存的外部路径，不删除目录；若内置可用，继续以内置为候选。已保存目录在以后启动时失效，应保留原路径并显示问题，便于修复。

首次原生加载前，成功保存可影响下一次初始化；加载开始后，当前路径冻结到 Host 进程结束。配置变化不停止当前歌曲，也不卸载/重新加载 DLL。若候选仍是同一个有效内置库，明确显示外部配置尚未参与选择，不误报已切到外部。

### 2.3 多插件共用目录

允许显式填写 VideoSecurityPlayer 实际使用的 `native/win-x64/libvlc` 目录，或由用户统一维护的 LibVLC 目录。前者的实际路径由用户提供，不扫描、推算或复制视频插件目录；后者便于减少对某个插件安装位置的依赖。

音乐插件只读取运行库，不更新、覆盖、移动或清理共享目录；自身卸载、缓存清理及开发部署只处理自己的文件。共享目录中库的升级需在使用它的 Host 进程退出后进行。若目录被移动或不再兼容，下次使用时明确失败，保留修正入口。

同进程两个托管加载上下文不保证可以任意混用不同版本的原生 LibVLC。S0 分别验证：

| 场景 | 必须证据 |
| --- | --- |
| 音乐无内置库，指定视频插件的实际原生目录 | 两种加载顺序均成功；实际原生来源符合配置，音乐关闭不释放视频的对象 |
| 两插件分别使用各自内置目录 | 检查真正加载的库和模块，不以目录不同推断已经隔离 |
| 版本或架构不兼容 | 可诊断地阻止音乐初始化；不以已加载的另一套库静默冒充成功 |
| 音乐运行中保存不同目录 | 当前音频和视频不受影响，Tool 显示待重启；重启后重新选择和验证 |

本阶段不修改视频插件以实现全局配置中心，也不建设共享的跨插件 LibVLC 服务。若 S0 发现现有 Host/原生加载机制无法满足某种组合，保留限制并修正接入方案，不能以覆盖兄弟插件文件作为修复。

## 3. 账号与设置 Tool

拟定 `MusicSettingsTool` + `MusicSettingsView`，名称“网易云音乐 · 账号与播放设置”，稳定 ID 为 `myavalonia.plugin.music.netease.tool.account-settings`。通过公开 SDK 的 AddTool/ToolDescriptor 声明，默认右侧、关闭时 Hide；根模型生命周期由 Host 登记。

| 区域 | 内容与操作 |
| --- | --- |
| 账号摘要 | 昵称、账号标识、登录/恢复/失效状态、凭据是否已保存；从 LoginSnapshot 投影 |
| 账号操作 | 打开既有登录 Document、明确的退出账号操作；二维码和恢复/重试保存继续复用原登录界面与协调器 |
| 运行库设置 | 手输/选择目录、检测、保存、清空配置；未登录时同样可操作 |
| 当前状态 | 内置/指定来源、配置目录与实际目录、检测问题、重启待生效；不显示协议密钥或 Cookie |

Tool 不持有另一份 AuthContext，也不从磁盘自行恢复账号。打开登录页使用公开 SDK 的文档打开能力；入口 ID 复用既有主 Document，具体调用按项目锁定 SDK 核对。Tool 发起退出时使用自己的稳定操作所有者，并沿用共享账号撤销逻辑。

Tool 模型在插件容器内单例，View 可以因 Dock 而多次创建。关闭按钮或浮动 Tool 窗口关闭按 Hide 处理，不 Dispose 模型，不退出账号，不停止歌曲，也不丢弃已编辑草稿。只有明确账号退出或插件生命周期结束才收口相应共享工作。

Standalone 继续复用 Module 中真实 Document/Tool 模型和 View，可用简易侧栏或页签承载；缺少 Host 文档打开能力时由显式开发 Stub 转到同一登录 View。不得复制第二套账号设置实现，也不能把普通侧栏测试当作真实 Dock 验收。

## 4. 朴素分工与线程约定

| 对象 | 职责和生命周期 |
| --- | --- |
| `ILibVlcSettingsStore` | 本机配置读取和原子保存的文件边界；不负责原生加载 |
| `LibVlcRuntimeResolver` 与目录探针 | 普通具体类；选择内置/配置候选，返回不可变检查结果，不维护播放器 |
| `LibVlcRuntime` | 插件容器拥有；按快照惰性加载，记录有效来源，最终释放自己的 LibVLC 实例 |
| `LibVlcAudioOutput` | 管理单个活动播放器、Media 和事件；将原生库类型留在 Infrastructure |
| `PlaybackCoordinator` | 继续管理播放意图、账号/曲目代次、媒体文件及错误；不操作 Tool/View |
| `MusicSettingsTool` | 组合账号摘要和设置状态；共享服务才是真实数据所有者 |

只在文件、音频、UI 调度等确实需要替换的边界使用接口。目录选择用普通顺序判断，不建立策略注册表；不为这一个 Tool 增加通用设置框架。

账号与运行库检测可以异步执行，结果都携带请求/配置版本。原生操作通过单消费者或等效串行执行路径离开 UI 线程，最新意图淘汰排队的过时工作；队列或入口必须有背压/合并，不能为每次点击堆积无界 Task.Run。

原生事件只投递事实，不在回调中同步 Stop、Dispose 或等待 UI。停止完成后解除 Media 引用、释放媒体句柄，再删临时音频；容器最终结束才释放播放器及引擎。普通停止不释放共享库文件，更不调用跨插件 DLL 卸载。

音频适配明确关闭自身媒体的视频输出和可视化窗口，实际参数按锁定版本验证；即使输入含封面或其他轨道，也不能弹出视频窗口。该设置仅作用于音乐插件自己的引擎/媒体，不改变视频插件行为。

## 5. VideoSecurityPlayer 的 Dock 与资源经验

以下区分“视频插件的现有事实”和“音乐方案采用方式”。历史门禁和通过数不作为音乐插件已验证的证据。

| 已核对的问题与事实 | 音乐方案的处理 |
| --- | --- |
| 原生布局按入口 Assembly.Location 定位；探针只检查，不加载 DLL；原生初始化失败后需重启 | 保留先探测后初始化；增加内置/指定来源。模块登记和打开 Tool 均不要求音频库就绪 |
| Dock 销毁旧视频表面时若仍引用旧 HWND，会黑屏、出现独立窗口或原生崩溃 | M1 纯音频不创建 VideoView/HWND；Dock 变化无需 Stop/Seek/重新播放，音频仅随真实业务生命周期改变 |
| 表面、媒体与用户意图有不同代次，旧恢复可能覆盖新操作 | 保留账号/播放/配置/View 请求各自必要的代次，拒绝迟到通知；不添加音乐不需要的视频表面代次 |
| DataContext 切换需先解绑旧对象，重复设置同一对象需幂等；Dispose 要退订 | View 只管理自己的订阅与 UI 资源；detach 不销毁单例模型或共享播放器，重新 attach 读取最新快照 |
| 文件选择器异步返回时，原 ViewModel 或窗口可能已改变 | 在 View 调 StorageProvider，防重入；返回后检查挂载、DataContext 和请求代次，过期结果丢弃 |
| Play/Stop/Media setter 等即使分别 Task.Run 仍会并发争用原生状态；回调内 Stop 可能自等待 | 后台串行原生命令；回调投递后返回，保持原始失败优先，避免 UI 或原生线程互相等待 |
| Pause/Stop 不等于释放媒体文件句柄；注入服务可能被 ViewModel 和 DI 双重 Dispose | 明确 Media 与播放器各自所有者，真正解绑/释放后才清文件；UI 模型不再次释放容器拥有的服务 |
| R1 历史实验记录反复 libvlc_new/release 仍出现 Semaphore 增长；共享引擎修复当时尚未实施 | 音乐从一开始按容器惰性复用引擎，Dock/换曲不重建；真实资源趋势仍需本地验证，不能声称复用已证明解决全部泄漏 |

现行源码的表面销毁流程是同步发送失效通知、清空输出关系，把可能阻塞的 Stop 排入后台串行路径，再让新表面恢复等待旧 Stop 完成。早期 G3 文档中的同步停止要求不能机械照搬。音乐无视频输出，不增加这套表面恢复流程。

来源入口：

- [VideoSecurityPlayer 组合与生命周期注册](../../../myavalonia-video-security-player/src/VideoSecurityPlayer.Plugin/Plugin/VideoSecurityPlayerPluginModule.cs)、[目录探针](../../../myavalonia-video-security-player/src/VideoSecurityPlayer.Plugin/Business/SecretVideoPlayer/Playback/PlaybackDeployment.cs)、[LibVLC 初始化](../../../myavalonia-video-security-player/src/VideoSecurityPlayer.Plugin/Business/SecretVideoPlayer/Playback/LibVlcRuntime.cs)。
- [原生命令串行调度](../../../myavalonia-video-security-player/src/VideoSecurityPlayer.Plugin/Business/SecretVideoPlayer/Playback/PlaybackNativeDispatcher.cs)、[视频表面绑定协调](../../../myavalonia-video-security-player/src/VideoSecurityPlayer.Plugin/Views/SecretVideoPlayer/Playback/PlaybackSurfaceCoordinator.cs)、[真实表面失效通知](../../../myavalonia-video-security-player/src/VideoSecurityPlayer.Plugin/Views/SecretVideoPlayer/EmbeddedVideoSurface.cs)。
- [Dock/恢复策略测试](../../../myavalonia-video-security-player/tests/VideoSecurityPlayer.Tests/VideoToolStabilityTests.cs)、[接入与踩坑记录](../../../myavalonia-video-security-player/docs/secret-video-player/troubleshooting/integration-and-conventions.md)、[R1 资源实验与未完成边界](../../../myavalonia-video-security-player/docs/secret-video-player/reference/R1-IMPLEMENTATION-AND-VALIDATION.md)。

视频插件当前注册四个 Document，`VideoToolStabilityTests` 的历史名称不能作为它已实现账号设置 Tool 的证据。本次 Tool 依据音乐插件现有 SDK 边界新增。

## 6. 实施与验收顺序

S0 先验证两种运行库来源、原生加载路径及与视频插件共存；S3 落实目录配置、冻结生效与音频资源所有权；S4 接入账号设置 Tool 和 Dock 行为；S5 完成场景映射、文档和真实开发记录。

自动测试覆盖优先级、失败分类、原子保存、配置代次、Tool 单例和订阅；真实 Host 验证 Dock 停靠/浮动、自动隐藏/重开、关闭窗口、账号退出和共享运行库。实际音频、实际加载路径、原生资源趋势分别留证；不借用视频插件的 Windows CI 或发布门禁。

本次仅编写设计和静态源码对照；运行库选择、Tool、Dock 联调及新增测试均未实施或执行。
