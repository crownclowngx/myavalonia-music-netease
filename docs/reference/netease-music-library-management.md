# V7 · M3 音乐库管理当前契约

> 更新：2026-09-24。V7-01～10 已接入；自动验证与实现依据见[专用实施记录](../archive/records/netease-v7/m3-implementation-20260924.md)。真实账号、WebView2 官方令牌链和真实 Host 交互仍按[验收记录](../archive/records/netease-v7/acceptance-20260924.md)执行，不以离线夹具证明线上可用性。

## 1. 功能与入口

| 编号 | 当前行为 | 主要入口 |
| --- | --- | --- |
| V7-01 | 全量喜欢状态区分未知、已喜欢、未喜欢；页面共享快照 | `ILikedSongsApi`、`MusicLibraryCoordinator` |
| V7-02 | 喜欢 / 取消喜欢；明确目标布尔值，重复请求不排队 | 歌曲行心形按钮、播放条“更多播放操作” |
| V7-03 | 按正整数 ID 打开尚未收藏的歌单 | 我的歌单 → 打开歌单 ID |
| V7-04 | 收藏 / 取消收藏他人歌单，使用详情或动态字段确认 | 歌单详情的收藏按钮 |
| V7-05 | 创建非私密普通歌单，返回真实 ID 后打开或选为添加目标 | 创建歌单 |
| V7-06 | 名称与描述分别保存，失败保留对应草稿 | 名称与描述 |
| V7-07 | 从搜索、歌单、队列、历史、当前播放添加歌曲 | 歌曲行“＋”、播放条“更多” |
| V7-08 | 从自有普通歌单移除选中歌曲，单独确认 | 从歌单移除选中歌曲 |
| V7-09 | 删除自有普通歌单，确认目标、数量及影响 | 删除歌单… |
| V7-10 | 同容器事实同步、页面草稿隔离、有限回查、关闭收口 | 共享协调器和 Scoped 编辑模型 |

界面以单曲操作为主；应用端口支持每次 1–50 个 ID 的有界集合。没有批量导入、封面上传、共享歌单编辑或远端撤销。音乐库增删不调用播放队列修改端口，不重开媒体、不重置位置或音量。

## 2. 职责与 SOLID

| 层 | 类型 | 职责与依赖 |
| --- | --- | --- |
| 纯规则 | `LibraryEditRules` | ID、Unicode、权限基线、差集与逐项结果；无网络和 UI |
| 应用端口 | `ILikedSongsApi`、`ILibraryPlaylistQuery`、`IPlaylistMutationApi` | 分别负责喜欢集合、可信管理详情、歌单 mutation，保留旧浏览接口 |
| 共享用例 | `MusicLibraryCoordinator` | 账号写入资格、只读回查、不可变事实、失效通知；注入上述接口及已有会话端口 |
| 协议适配 | `LibraryRequests`、`LibraryRequestExecutor`、`NeteaseLibraryApis` | 固定请求、回执解释、字段解析；复用唯一 `NeteaseTransport` |
| 令牌适配 | `ILibraryCheckTokenProvider`、`WebViewLibraryCheckTokenProvider` | 按需官方 SDK 初始化、令牌及浏览器资源回收；不参与业务权限判断 |
| 页面模型 | `PlaylistEditor` | Scoped 草稿、目标、确认、焦点所需状态，投影共享事实；不编码 HTTP |
| 控件 | `LibraryEditorView`、`SongLibraryActionsView` | 输入、呈现与焦点；不逐行读取远端状态 |

设计采用现有 MVVM、构造注入、普通事件和类型化记录。每个插件容器只有一个音乐库协调器；没有通用命令总线、事件溯源、第二套播放器或 Node 代理。关键竞态、中文输入、边界和寿命均有中文注释。

`MusicWorkspace` 只组合编辑模型；`PlaylistBrowser` 保留原导航代次、曲目分页及缓存。隐藏页仅标记失效，恢复时合并刷新并处理已删目标；可见相关页保留当前目标、页码及仍存在的选择。取消收藏后详情读取失败退回列表，提示不可读取，不将其当作删除。

## 3. 固定协议依据

请求依据固定上游 [a8c781fd](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/tree/a8c781fd64faab17fedfd46e0615a2609307f163)。模块的请求参数是移植依据，返回夹具是本地解析契约；它们不代替真实账号响应核验。

| 能力 / 上游模块 | 原生路径 / 协议 | 关键参数 |
| --- | --- | --- |
| `likelist` | `/api/song/like/get` · eapi | `uid` |
| `like` | `/api/radio/like` · weapi | `trackId`、明确 `like` 布尔、`alg=itembased`、`time=3` |
| `playlist_detail` | `/api/v6/playlist/detail` · eapi | 沿用 M2 详情请求 |
| `playlist_detail_dynamic` | `/api/playlist/detail/dynamic` · eapi | `id`、`n=0`、`s=0` |
| `playlist_subscribe` | `/api/playlist/subscribe` 或 `/api/playlist/unsubscribe` · eapi | `id`；v2 临时令牌置于协议头 / Cookie，收藏另带 `checkToken` |
| `playlist_create` | `/api/playlist/create` · weapi | `name`、`privacy=0`、`type=NORMAL` |
| `playlist_name_update` | `/api/playlist/update/name` · eapi | `id`、`name` |
| `playlist_desc_update` | `/api/playlist/desc/update` · eapi | `id`、`desc`，允许空串 |
| `playlist_tracks` | `/api/playlist/manipulate/tracks` · eapi | `pid`、`op=add/del`、JSON 字符串 ID 数组、`imme=true` |
| `playlist_delete` | `/api/playlist/remove` · weapi | `ids` JSON 数组字符串 |

不复制上游 `512` 后复制曲目 ID 重试的分支，不自动切换写端点。eapi / weapi 编码、会话与连接仍由现有基础设施负责。

### 收藏令牌依赖

上游 [register_checktoken_v2](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/register_checktoken_v2.js)依赖官方 Watchman 浏览器 SDK。当前使用锁定的 WebView2 Core **1.0.4191.47**，只在明确收藏 / 取消收藏时创建隐藏控制器，加载 `https://acstatic-dun.126.net/tool.min.js`，取得一次新令牌后关闭。不使用固定样本令牌，不改写浏览器指纹，也不读取用户浏览器配置或账号 Cookie。

浏览器使用插件数据目录下 `library-browser` 专用目录和 InPrivate profile；顶部页面只允许固定本地生成的网易源地址，拒绝新窗口和权限请求。初始化在 Avalonia STA UI 消息循环异步执行；准备最多 45 秒，取消后归还控制器 / 隐藏 HWND，迟到创建也单独关闭。准备失败为 `NotSent`，没有网易收藏写请求。

系统需要安装 Windows x64 Microsoft Edge WebView2 Runtime；NuGet 中的 Core / Loader 不是浏览器 Runtime。插件声明私有 Core、原生 Loader 与许可资产，排除 WPF / WinForms 引用。自动测试使用令牌替身，不下载或执行线上 SDK；实际可用性列入 H02。

## 4. 写入与回查语义

1. 先验证输入与账号代次，冻结本次意图和确认基线；读取最新权限 / 成员关系。
2. 同账号同代次只接纳一个库写操作；同时点击返回 `Busy`，没有排队或持久写任务。
3. 原本已是目标状态时返回 `NoChange`，不发写请求。增删曲目只发送必要差集。
4. 一次意图最多发送一次 mutation。HTTP 超时、发送后取消、损坏响应、未知业务码均可能已生效，不能用“重试”重发。
5. 写后最多 3 轮只读回查：立即、等待 500 ms、再等待 1,500 ms；整个回查窗口最多 20 秒。网络请求沿用 15 秒 / 1 MiB 预算。20 秒不含令牌准备、前置读取和 mutation 自身。
6. 429、额外验证或会话失效停止自动回查；有 `Retry-After` 时手动回查也遵守等待时间。手动“重新读取结果”只有读取请求。
7. 以回读确认 `Confirmed`；成员不全或不能确认目标时为 `NeedsRecheck` / `PartiallyConfirmed`。逐项分类为已添加、原已存在、已移除、原不存在、被拒绝或待核实；没有逐项拒绝依据时保留待核实。

创建回执丢失 ID 时不认领同名歌单；阻止再次创建，提示刷新目录核对。用户明确点击“已核对歌单目录，结束创建结果提示”后，后续创建才是新的意图。返回了服务端名称时以该名称回查；改名只有回读与目标或明确回执名称一致才确认，未获依据的规范化差异保持待核实。

删除须明确成功回执且重新读取为不存在 / 不可访问，才确认本次删除。只有查询失败、403、目录缺项或发送后超时，均不表示删除完成。成功删除使相关页面返回目录；播放队列及已缓冲媒体保留。

Cookie 持久化失败保留已收到的远端回执，反馈存储问题并继续核实；不会再次提交。待核实操作只存内存，最多 64 个不同资源，达到上限要求先核实；进程重启不重放写操作。

## 5. 完整性、权限与输入

- 喜欢集合 `null` 表示未知，真实空集合才表示无喜欢；最多 50,000 个 ID 且仍受 1 MiB 响应限制。损坏 / 超额读取不清空既有可靠状态。
- 管理详情保留 creator、类型、描述是否已知、可缺失 subscribed、完整曲目 ID 与计数。他人歌单缺 subscribed 时再读动态字段；目录第一页缺项不等于未收藏。
- 仅 creator 与当前账号相符且 `specialType=0`、类型为普通时允许编辑；内置喜欢 `specialType=5`、共享标记和未知类型拒绝编辑。共享字段的实际线上组合仍需 H03 核验，缺失依据不会放宽权限。
- 完整成员快照最多 10,000 条，必须与声明计数一致；不以当前 50 首展示页或截断集合做增删差集和不存在确认。
- ID 只接受可解析为正 `Int64` 的十进制文本。名称 trim 后 1–100 个 Unicode 标量，禁止控制字符；描述 0–1,000 个标量，换行统一 LF，允许换行和 Tab。无效代理项拒绝。
- 名称 / 描述分别保存，外部修改比较该字段基线；删除比较目标、名称、更新标识与成员顺序。后台读取不覆盖草稿，只有明确重新载入才替换。

## 6. 账号、页面与关闭

会话使用现有 AccountId + Epoch；喜欢读取另用 Revision / 读取代次，旧集合不能覆盖写后确认。旧操作只释放自己持有的写入资格，不清理新账号忙碌状态。UI 回调再检查账号、页面寿命和表单版本；关闭后重开表单不会被旧保存结果替换。

关闭 Document 解除订阅、取消草稿读取，已接纳 mutation 由共享协调器收口；最后一个页面关闭不停止播放。插件 Shutdown 先关闭库服务，再关闭账号 / 播放依赖。取消资格与异步完成任务保证正常收口；即使外部测试适配忽略取消，也不阻塞协调器关闭，迟到结果不能发布。

远端操作接受后关闭面板不表示撤销。删除与移除确认默认聚焦取消，Enter 不隐式提交表单；Esc 先处理编辑层，输入法预编辑时不关闭；有未保存内容时提示保留或放弃。窄窗口表单纵向滚动，关键按钮可达，关闭后还原有效焦点。

## 7. 开发验证

执行 `pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V7`，详见[验证指南](../maintenance/verification.md)与[46 场景矩阵](../maintenance/netease-v7-m3-music-library-verification-plan.md)。默认离线本地门禁继承 M1 / V3 / M2 / V5 / V6，并核验 V7 TRX 方法映射、一致性、寿命、12 布局组合及 16 张截图的身份、时间、尺寸和摘要。只有实际断言成功后写证据。

真实账号、官方令牌、Host / Dock、DPI、物理 IME、读屏和声卡另行验收。开发过程不使用 AIFLOW、Windows CI 或发布门禁；包版本仍为 1.0.0，本轮不部署或发布。
