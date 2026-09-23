# 音乐与播放当前契约（V4 / M2）

> 更新：2026-09-23。V1–V4 已实现并由用户确认手工验收完成，见[验收收口记录](../archive/records/netease-v1-v4-acceptance-20260923.md)。历史 MP3 解码及运行库来源证据见[V2 实施记录](../archive/records/netease-v2/m1-implementation-20260923.md)，当前增量见[日常播放器](netease-daily-player.md)。

## 已实现的边界

V3 已调整 Document / Tool 的紧凑布局与深浅主题，详见[界面当前契约](netease-desktop-ui.md)。本页的播放、账号和运行库语义继续有效。

歌曲搜索每页 30 条，关键词去除两端空白，长度 1–200，offset 0–30000。V4 已增加歌单、共享队列、四模式、进度定位、有限下载恢复、逐行/翻译/逐字歌词、本地历史与静默恢复，见[日常播放器当前实现](netease-daily-player.md)；下载管理、收藏写操作和更高音质请求尚未接入。结果展示名称、歌手、专辑；播放详情展示封面、时长、试听标记和状态。封面失败显示音符，不影响歌曲。

一个插件容器共享一个账号、队列和单曲执行器，每个 Document 独立搜索。V4.2 起播放由账号/插件寿命拥有，关闭任意页面继续播放，再打开订阅同一会话；退出或插件关闭才停止。Tool 是右侧 singleton，关闭行为为 Hide。暂时卸载 View 只释放图片和订阅，不取消音乐。

## SOLID 分工与协议

| 组件 | 唯一职责与实际替换边界 |
| --- | --- |
| `LoginMusicSession` / `LoginCoordinator` | 受控账号快照、账号代次和凭据版本；唯一会话提交者 |
| `NeteaseMusicApi` | 搜索、详情和播放资源的类型化语义 |
| `NeteaseTransport` / `XeapiTransport` | 共同 HTTP 预算、错误、Cookie；容器内串行协议握手 |
| `FlurlMediaBuffer` | 无凭据媒体请求、有界临时下载及文件清理 |
| `LibVlcRuntimeResolver` / `LibVlcRuntime` | 只读候选检查、惰性加载、实际运行库状态与容器引擎 |
| `LibVlcAudioOutput` | 串行原生控制，MediaPlayer/Media 所有权和事实事件 |
| `PlaybackCoordinator` | 单曲意图、代次、旧工作收口、控制和资源释放顺序 |
| `QueueNavigator` / `PlaybackQueueCoordinator` | 纯导航规则与共享队列写入；按条目身份和尝试身份消费终态 |
| `MediaLoader` / `LyricsCoordinator` | 分别处理有限媒体恢复与共享歌词请求/时间轴；互不控制对方 |
| `PlaybackPersistence` / `PlaybackStateStore` | 值快照保存策略与原子文件格式；不反向持有播放器 |
| `PlayerAccountCoordinator` | 核验账号后的恢复资格、退出清理及会话失效保存桥接 |
| `MusicWorkspace` / `MusicSettingsTool` | 页面搜索投影与设置草稿；依赖端口，不接触 LibVLC/Flurl |

没有事件总线、通用仓储或状态机框架。业务层只依赖窄接口和记录，平台文件/原生差异留在 Infrastructure；共享服务由组合入口通过构造函数注入。

| 能力 | 逻辑端点 | 实际协议 |
| --- | --- | --- |
| 搜索 | `/api/cloudsearch/pc` | eapi，type=1、limit=30 |
| 详情 | `/api/v3/song/detail` | weapi，c 为包含 long ID 的数组字符串 |
| 资源 | `/api/song/enhance/player/url/v1` | xeapi，ids 数组字符串、level=standard、encodeType=flac；最终以响应格式为准 |
| 公钥 | `/api/gorilla/anti/crawler/security/key/get` | HTTPS 表单，校验 nonce 签名后解密公钥状态 |

固定上游 `a8c781fd64faab17fedfd46e0615a2609307f163`。xeapi 使用 BouncyCastle 的 X25519 和 .NET AES/HMAC；首次 B/S/R 与会话复用均有[独立上游向量](../../tests/MusicNetEasePlugin.Tests/Fixtures/xeapi-vectors.json)。[生成器](../../tools/generate-xeapi-vectors.cjs)执行原上游文件并核对 SHA-256，只替换熵源；Node 仅用于开发向量生成。

API 响应共用 15 秒/1 MiB 预算，支持明文、eapi/xeapi 加密及解密后 gzip，解压仍有上限。HTTP/正文错误区分，MusicException 保留安全分类和 Retry-After，不保留含凭据的 InnerException。POST 不自动重放。只有明确账号失效撤销会话，CDN 403、无地址和试听不退出账号。

## 会话、并发与清理

音乐会话包含原子捕获的 AccountId、账号 epoch、credentialVersion、AuthContext 和撤销 Token，仅在业务内部传递。重登即使是同一个账号也产生新 epoch；重试保存不撤销已核验账号。音乐响应的 Cookie 只能通过登录协调器按 epoch/version 提交；旧响应不能覆盖新凭据。保存与退出共用既有尾任务顺序，退出最终清理不会被迟到写盘复活。

每次 Play 先登记新代次，再取消旧工作，等待其停止和文件释放后执行新请求。详情、地址、缓冲、引擎打开均检查撤销；A→B→C 最终只允许 C 接管。暂停/定位令牌与原播放代次关联，音量继承和用户修改共用顺序。V4.3 支持定位草稿，进度事实仍来自原生事件，不用 UI 计时器伪造。

账号撤销监听独立于活动播放任务，手动停止或自然结束之后退出也会清除当前曲目与时长；重复关闭等待同一收口任务，避免容器提前释放依赖。

原生控制在后台异步门内串行；**原生事件只使用 TimeChanged/LengthChanged 的缓存，不在回调中重入 Time、Length、Stop 或 Dispose**。首次真实解码曾暴露回调内状态查询导致的启动超时，修正后连续解码与收口通过。终态事件排到后台处理；先 Stop、解绑并销毁本次 MediaPlayer/Media，再删临时文件。容器始终复用 LibVLC 引擎，换曲创建带独立代次的新 MediaPlayer，防止旧事件冒充新歌。

## 媒体边界

- 独立 `netease-media` Client，没有账号 Cookie、CSRF 或 xeapi 身份头。
- 只接受默认端口、无 userinfo 的 `.music.126.net` / `.music.163.com` HTTP(S) 地址；旧 HTTP 升级 HTTPS。每次重定向重新核验，最多 3 跳。
- 完整临时文件下载后才交给引擎：64 MiB 实际字节上限、响应头 15 秒、空闲读取 15 秒、总计 120 秒。没有播放前整曲常驻托管内存；首播有完整下载等待。
- 检查声明长度与实际长度，拒绝明显错误页及非媒体头。支持的头包括 MP3、FLAC、WAV、Ogg、MP4/M4A；真实网易本次验证 MP3，WAV 用于自生成离线夹具，其他格式不能据此宣称逐项实测。
- HTTP 410 明确过期时重新解析地址一次，网络/超时最多延迟重试一次，总媒体尝试最多三次；普通 403 不重试。无地址按队列的有限候选预算处理。
- 缓冲位于数据根 `media-buffer/instance-<GUID>`，`.partial` 成功后改名 `.media`。实例持有 `.owner` 锁；启动只回收可独占的 GUID 残留目录，拒绝目录联接和非本组件命名目录。共享运行库目录从不参与清理。
- 停止失败保留可能仍被占用的文件并提示；文件删除失败不循环阻塞 UI，残留交给后续受控回收。

## LibVLC 路径与 Tool

锁定 LibVLCSharp 3.10.0、VideoLAN.LibVLC.Windows 3.0.23.1（原生 3.0.23）、BouncyCastle.Cryptography 2.6.2。播放在插件所在进程内执行。

内置目录按插件程序集位置拼接 `native/win-x64/libvlc`。有效内置优先，其次 `playback-settings.json` 的 `customDirectory`；两者无效时搜索和账号仍可用。配置 schemaVersion=1，原子保存，与 DPAPI 会话分离；退出不删除目录设置。

目录检查不加载 DLL：绝对路径、Windows x64、libvlc/libvlccore 以及音频输出/MP3/FLAC/WAV 必需模块的 PE 架构。检查通过不等于全部依赖和设备已验证。初次播放才绑定并创建引擎；原生加载尝试后保存仅影响重启后的选择。

Windows 额外为本插件私有 LibVLCSharp 程序集绑定选定 DLL 句柄，避免同名原生库导入漂移。绑定是程序集级不可热换事实，引擎和播放状态仍由容器独立拥有；不设置 PATH、VLC_PLUGIN_PATH 或全局 DLL 搜索目录，不卸载其他插件库。共存与加载顺序保留在 M1 矩阵作为后续实机回归方法；现有范围的手工验收已由用户确认完成，单独复用视频目录的探针仍只证明该次解码。

Tool 分别显示草稿、已保存目录、候选来源/问题和实际加载路径/版本；检测不保存，保存不热切换。初始化属于单例模型，重建 View 保留草稿；目录选择结果同时检查 View、挂载和草稿版本。

公开 SDK 3.4.1 没有 Tool 主动打开指定 Document 的运行时端口。实际界面提示从“新建”打开网易云音乐，并提供恢复、重试保存和退出；扫码继续使用原 Document。这里按公开能力调整原计划，不引用 Host 内部 workspace 服务。

底层引擎跨平台；当前指定目录加载、原生资产和账号保护落地于 Windows x64。Linux/macOS 目录加载、受保护存储和 Host 尚未适配，探针明确返回平台问题。

## 本地验证入口

[使用步骤](../quick-start/netease-playback.md) · [M2 自动/人工验证矩阵](../maintenance/netease-v4-m2-daily-player-verification.md) · [M2 场景方法映射](../../tools/m2-test-map.json)。默认 `verify-development.ps1` 执行完整 V4 本地门禁，同时保留 [M1 映射](../../tools/m1-test-map.json)与 V3 检查；Login/M1/V3 入口仍运行全量测试，只按相应层次核验产物。未使用 AIFLOW、Windows CI 或发布门禁。
