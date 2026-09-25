# V8 · M4 音乐发现当前契约

更新日期：2026-09-25。推荐、榜单、歌手 / 专辑、私人 FM 和远端记录已经接入生产页面。[实施记录](../archive/records/netease-v8/m4-implementation-20260925.md)记录本地检查，[专用矩阵](../maintenance/netease-v8-m4-music-discovery-verification-plan.md)维护回归范围，[人工验收](../archive/records/netease-v8/acceptance-20260925.md)仍待执行。插件包版本不因 V8 实施序号改变。

## 用户入口与行为

| 入口 | 行为和范围 |
| --- | --- |
| 发现 → 每日推荐 | 读取当前账号的一批歌曲，展示来源、抓取时间、加载数量；刷新失败保留旧内容并提示 |
| 个性歌单 / 通用歌单 | 两个独立来源；个性读取失败不会自动拿通用结果冒充；点击真实歌单 ID 进入既有歌单详情 |
| 榜单 | 读取一个榜单目录，显示接口提供的更新频率；打开既有歌单详情；不把摘要当作完整榜单 |
| 歌曲行或播放条 → 更多 → 查看歌手 / 专辑 | 明确读取一次歌曲详情，展示多个歌手及专辑的真实身份；缺少身份时没有可跳转卡片，不按名字反查 |
| 歌手 → 歌曲 / 专辑 | 歌曲按热门 / 时间读取，每页 50；更多由用户触发；歌手资料失败仍保留已取得歌曲 |
| 专辑 | 保留服务端曲序；只有 `album.size` 与收到的歌曲数相符且未截断，才显示“播放全部” |
| 最近播放 → 网易最近 / 一周排行 / 全部排行 | 本机历史保持原算法；网易记录进入独立发现页面，范围及延迟提示明确，未知时间 / 次数不编造 |
| 发现 → 私人 FM | 先展示影响说明；用户点击开始才读取并在首批成功后替换队列；下一首和“不喜欢这首”是两个独立意图 |

普通发现歌曲复用立即播放、下一首、追加及喜欢 / 添加歌单入口；追加空队列不出声。歌曲 ID 表示内容，EntryId 表示队列中的一次安排，允许用户主动追加重复歌曲。推荐 / 专辑完整时播放全部；歌手、远端历史或截断数据仅播放已加载范围。推荐歌单和榜单收藏仍由 V7 歌单详情处理。

作品返回最多保留 8 个页面快照、选择和滚动位置，5 分钟内复用，过期重新读取。每个 Document 有独立的浏览模型；只有 FM / 队列 / 播放器与喜欢状态在同容器中共享。列表虚拟化，封面使用既有图片租约和占位，不创建第二套下载缓存。

## SOLID 与代码职责

| 职责 | 代码与设计理由 |
| --- | --- |
| 消费者端口 | [DiscoveryContracts](../../src/MusicNetEasePlugin.Plugin/Application/Discovery/DiscoveryContracts.cs)、[FM 契约](../../src/MusicNetEasePlugin.Plugin/Application/Discovery/PrivateFmContracts.cs)、[远端历史契约](../../src/MusicNetEasePlugin.Plugin/Application/Discovery/RemoteHistoryContracts.cs)：读取、内容供应及偏好写入分开；只读消费者不拿到写能力 |
| 协议适配 | `Infrastructure/Http/NeteaseDiscoveryApi`、`NeteaseArtistAlbumApi`、`NeteasePrivateFmApi`、`NeteaseRemoteHistoryApi`：只负责参数、解析和会话保护，应用层不读 JSON |
| FM 编排 | [PrivateFmCoordinator](../../src/MusicNetEasePlugin.Plugin/Application/Discovery/PrivateFmCoordinator.cs)：会话、单读取槽、预算、去重、一次反馈；没有音频或队列依赖 |
| 原子接纳 | [PlaybackQueueFm](../../src/MusicNetEasePlugin.Plugin/Application/Playback/PlaybackQueueFm.cs)：原队列协调器的部分文件，共用唯一写入锁；HTTP 在锁外执行，接纳时重新核验身份 |
| 页面投影 | [DiscoveryWorkspace](../../src/MusicNetEasePlugin.Plugin/Features/Discovery/DiscoveryWorkspace.cs) 管理有界导航、分页和取消；[PrivateFmWorkspace](../../src/MusicNetEasePlugin.Plugin/Features/Discovery/PrivateFmWorkspace.cs) 只订阅共享 FM / 播放快照 |
| 组合与寿命 | [组合根](../../src/MusicNetEasePlugin.Plugin/Plugin/MusicNetEasePluginServices.cs)：共享 FM、IPlayerSession 与 IPrivateFmPlayer 指向同一队列；页面 Scoped；原播放租约拥有最后页关闭策略 |

采用构造注入、普通 MVVM、记录类型和枚举。没有通用状态机、消息总线、仓储、工作流框架，也没有第二个队列写入者。关键取消、预算、回执、锁和寿命边界使用中文注释说明设计原因。Fake 和真实端口遵循相同的空值、异常、取消与未知结果语义。

MusicTrack 保留原显示字符串并增加可选 ArtistRefs / AlbumRef；公共歌曲解析器统一填充有效 ID，保留多歌手顺序。持久化仍使用原 StoredEntry 白名单，不改 schema、不持久保存作品身份或 FM 资格；旧资料恢复不联网。

## 固定上游协议登记

参考提交固定为 `a8c781fd64faab17fedfd46e0615a2609307f163`。下列链接对应已核对的模块源码，协议默认行为另见[请求核心](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/util/request.js)。无显式 crypto 的目录端点选择其 eapi 路径，不依赖 Node 外壳或运行时配置。原生请求定义集中于[DiscoveryRequests](../../src/MusicNetEasePlugin.Plugin/Infrastructure/Http/DiscoveryRequests.cs)。

| 模块 | 原生路径 / 协议 | 参数及解析依据 |
| --- | --- | --- |
| [recommend_songs](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/recommend_songs.js) | `/api/v3/discovery/recommend/songs` / weapi | 无必需业务参数；`data.dailySongs`；不启用额外刷新模式 |
| [recommend_resource](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/recommend_resource.js) | `/api/v1/discovery/recommend/resource` / weapi | 当前账号；`recommend` |
| [personalized](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/personalized.js) | `/api/personalized/playlist` / weapi | limit=30、total=true、n=1000；`result` |
| [toplist](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/toplist.js) | `/api/toplist` / eapi | `list`，ID、封面、可选 updateFrequency；未提供具体更新时间就不伪造 |
| [artist_detail](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/artist_detail.js) | `/api/artist/head/info/get` / eapi | id；`data.artist` 身份必须相符 |
| [artist_songs](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/artist_songs.js) | `/api/v1/artist/songs` / eapi | id、private_cloud="true"、work_type=1、order=hot/time、offset、limit=50；`songs` / `more` |
| [artist_album](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/artist_album.js) | `/api/artist/albums/{id}` / weapi | offset、limit=50、total=true；`hotAlbums` / `more` |
| [album](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/album.js) | `/api/v1/album/{id}` / weapi | `album` 与 `songs`；ID 相符，size 用于完整性核验 |
| [personal_fm](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/personal_fm.js) | `/api/v1/radio/get` / weapi | 当前账号；`data` 歌曲数组 |
| [fm_trash](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/fm_trash.js) | `/api/radio/trash/add` / weapi | songId、alg="RT"、time=25（模块默认参数，**不表示测得听了 25 秒**）；一次发送、无查询回执接口 |
| [record_recent_song](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/record_recent_song.js) | `/api/play-record/song/list` / weapi | limit=1000；`data.list[].data` 为歌曲，playTime 按毫秒解释；不自创分页 |
| [user_record](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/user_record.js) | `/api/v1/play/record` / weapi | 当前 uid；type=1 最近一周 / 0 所有时间；weekData / allData 的 song、可选 playCount / score |

全部入口使用已登录会话，尚未实现游客产品路径。ID 必须为正 long，非法 offset / enum 在发送前拒绝。必需容器缺失或作品 ID 不匹配是协议失败；合法空数组是空成功。可选封面、描述、播放时间、次数和分值缺失保持未知。排行保留服务端顺序及重复记录；“所有时间”是排行范围，不等于完整播放流水。

响应夹具位于[协议测试](../../tests/MusicNetEasePlugin.Tests/DiscoveryProtocolTests.cs)，由上述固定模块、已有歌曲模型和预期容器构造，使用虚构 ID / 内容；**没有把合成夹具标成真实账号响应样本**。线上容器、字段及权限仍须 M01～M04 实测。协议测试不能证明这些端点当前在线可用或账号具有播放权益。

读取复用 MusicRequestExecutor / NeteaseTransport 的会话代次、加密、15 秒 / 1 MiB 限制和错误分类：200 后解析，301/401 撤销当前会话，限制 / 验证 / 429 提示局部失败，未知码不算成功。旧会话不能提交新 Cookie；记录不含 Cookie、签名 URL 或原始个人响应。

## 有界读取与账号隔离

- 仅加载当前可见区块；每 Document 两个物理读取槽，同一代次只提交最新结果。页面等待总预算 30 秒，以容纳歌手歌曲与资料的两个顺序读取；每次传输仍为 15 秒。取消后立即退出页面等待，忽略取消的适配器仍占槽直到实际结束。
- 歌手分页每次最多保留 50 条，按原始返回数推进 offset，集合上限 1000；空页、重复无进展或预算到达停止。推荐、专辑和记录一次快照最多 1000，不能绕过传输字节限制。
- 读取不后台重试；429 采用 Retry-After，缺失时本地等待 30 秒。发现页面按区块保留窗口；FM 按账号代次保留窗口，不能通过重新开始绕过。
- 用户切页或刷新取消旧请求，并递增请求代次；排队中的 UI 回调也检查代次、账号和关闭状态。账号撤销清空个人浏览集合和返回历史，旧成功 / 失败不能覆盖新账号。
- 搜索、歌单、歌词和普通四种播放模式继续使用既有路径；浏览本身不调用播放或存储历史。真实播放产生的本机最近记录由原 PlaybackPersistence 登记。

## FM 会话、推进与反馈

FM 是内容供应会话，不是第五种播放模式。显式开始取首批，只有账号、SessionId、QueueRevision 仍有效时才接纳；失败保留原队列。开始期间用户操作普通队列会撤销旧首批资格。FM 运行时暂用顺序播放，记住此前模式。

| 边界 | 当前规则 |
| --- | --- |
| 内容补取 | 播放中待播少于 2 首触发；全容器最大 1 个物理请求；同会话的在途调用合并 |
| 一轮预算 | 最多 3 次、20 秒；仅空批 / 全重复批允许在 500 / 1500 ms 后补取；网络 / 协议错误立即停止 |
| 保留与去重 | 每批最多接纳 10 首，待播最多 10；排除队列已有及最近 100 个供应 ID；消费队列保留当前及最近 99 个已消费项 |
| 推进 | AttemptId 终态只消费一次；Next、自然结束和补取竞争由唯一队列写入者合并；上一首、改序、选曲和移除在 FM 期间禁用 |
| 不可播放 | 连续最多 20 个候选，跨批次不清零；其他播放错误停止；用户明确 Next 才重启失败链 |
| 暂停 | 保留暂停状态，暂停时 Next 可选择后续内容但不出声；进度事件不启动预取；已发出的读取可完成接纳 |
| 普通意图 | 点播 / 追加 / 替换 / 改模式先结束 FM；End 停止供应，保留现有队列并恢复原模式；不会还原一份隐藏旧队列，也不隐式停止当前音频 |
| Stop / 清空 / 账号撤销 / 最后页关闭 | 撤销 FM 身份并停止补取，沿用原音频停止策略；只关闭非最后页不停止共享播放 |
| 进程 / 页面恢复 | 只恢复普通队列资料、原模式和位置，保持静默；不恢复 SessionId、补取资格或待重发反馈 |

“下一首”、自然结束、仅浏览或喜欢歌曲，均不会提交 fm_trash；不调用 scrobble / scrobble_v1。只有明确点击“不喜欢这首”才提交偏好写入，并不等于取消歌曲喜欢。

反馈目标由 FM SessionId + 账号 Epoch + EntryId + TrackId 固定。发送前检查，最多一个反馈在途，同一会话已接纳目标至多一次，最近目标保留 100 个。最多等待 15 秒；成功只在目标仍当前时推进，用户已 Next 后的迟到成功不跳过新曲。四种结果分别显示：未发送、明确拒绝、已接纳、结果未确认。200 回执即使随后 Cookie 保存失败仍保留成功事实；超时、取消或损坏回执保留未知，不自动重发、回查或换曲。没有可信查询接口，不复用歌单“写后回查”的承诺。

## 验证和已知范围

完整开发命令为 `pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V8`，运行全部测试并继承 V7、V6 及适用前置。50 个场景映射到实际测试方法及参数化数量，门禁核验真实来源、队列身份、FM 预算 / 竞争 / 不可播链、恢复、记录和 60 个生产布局组合、16 张截图；详见专用矩阵及当次原始证据。

本轮不使用 AIFLOW、Windows CI、发布门禁，也不部署 / 发布。真实账号、声卡、Host / Dock、DPI、物理键盘 / 读屏与用户总验收均单列待验；本地 Headless 和离线 PCM 不能代替它们。M4 开发完成与 M4 人工验收完成分别登记，路线图验收复选框保持待验。
