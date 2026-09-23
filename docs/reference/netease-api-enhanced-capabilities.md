# api-enhanced 上游能力清单与移植边界

> 用途：为[能力路线图](../roadmap/netease-capability-roadmap.md)提供选型依据、能力范围和固定源码索引；本文属于上游调研参考。
> 状态：上游登录子集已实现并离线验证；独立补充的微信登录已通过真实账号核验，其他模块未移植。核对日期：2026-09-22。
> 基线：[a8c781fd64faab17fedfd46e0615a2609307f163](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/tree/a8c781fd64faab17fedfd46e0615a2609307f163)，提交日期 2026-09-11 UTC。
> 统计：该提交 `module/` 下共有 **440 个直接子级 JavaScript 模块**。包括业务接口、本地包装和工具；不等于 440 个独立网易 HTTP 接口，更不代表 440 项当前可用能力。

主要事实源为[固定提交的接口文档](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/public/docs/home.md)、[module 源码目录](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/tree/a8c781fd64faab17fedfd46e0615a2609307f163/module)、[请求核心](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/util/request.js)与[加密实现](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/util/crypto.js)。下述分组及接入优先级由本项目整理；实际权限、字段和可用性以接入时逐项验证为准。

## 1. 对我们项目的接入分层

微信登录是本插件按网易 SNS/微信网站授权链补充的能力，不属于下列 440 个上游模块。
默认微信、备用网易云 App；真实微信验证不能外推上游 App 二维码或其他模块的真实可用性，见[微信记录](../archive/records/netease-v1/wechat-login-implementation-20260922.md)。

| 能力层 | 范围 | 当前承接 |
| --- | --- | --- |
| 登录与会话 | QR key、本地二维码、扫码状态、账号检查、Cookie 会话、退出 | 已实现；微信真实授权及账号核验通过，App 授权等验证边界见当前契约 |
| 基础播放器 | 搜索、歌曲详情、播放地址、歌词、用户歌单、歌单曲目 | M1 与 M2 功能已接入，见[播放契约](netease-music-playback.md)和[日常播放器](netease-daily-player.md)；真实听感/Host 验收仍单列 |
| 日常扩展 | 收藏、歌单编辑、推荐、FM、远端历史、歌手/专辑 | 路线图 M3–M4；尚未实现 |
| 场景扩展 | 云盘、下载、评论、视频、播客、广播、一起听等 | 路线图 M5 候选，按需求选择 |
| 专项能力 | 账号管理、会员/广告/积分任务、音乐人、UGC、购买等 | 只记录上游范围；需具体产品需求与独立验证 |

## 2. 能力与边界

“代表模块”用于定位源码，不是本插件已公开的方法。多个版本可能使用不同协议和返回结构；不能只替换方法名。

| 能力 | 上游提供的范围与代表模块 | 实际边界 / 我们的安排 |
| --- | --- | --- |
| 二维码登录 | `login_qr_key`、`login_qr_create`、`login_qr_check` | 首阶段采用；create 是本地二维码包装，key/check 请求网易；真实成功还需账号检查 |
| 手机/邮箱登录、注册 | `login_cellphone`、`login`、`captcha_sent*`、`captcha_verify`、`register_cellphone` | 涉及验证、限流和账号状态；后续单独验证，不视为 QR 的自动降级路径 |
| 游客、登录状态、刷新与退出 | `register_anonimous`、`login_status`、`login_refresh`、`logout` | 上游说明刷新不适用于二维码 Cookie；匿名注册为 xeapi，P0 已证明当前 App key/801 路径不依赖它，详见当前契约 |
| 账号资料与绑定 | `user_account`、`user_detail*`、`user_level`、`user_update`、`avatar_upload`、`user_binding*` | 首阶段只取确认账号需要的数据；改资料、绑定手机是独立写操作 |
| 设备与验证 | `device_list`、`device_kickoff`、`deviceinfo_center_upload`、`captcha_safe_sent`、`verify_*` | 可能需要安全验证；强制下线设备不进入基本退出流程 |
| 搜索与建议 | `search`、`cloudsearch`、`search_hot*`、`search_suggest*`、`search_multimatch` | 歌曲/歌手/专辑/歌单等类型由参数区分；分页、搜索结果与可播放性独立 |
| 听歌识曲与本地匹配 | `audio_match`、`search_match` | 需额外音频/文件特征与输入规范；不是一般关键词搜索 |
| 歌曲详情与音质信息 | `song_detail`、`song_music_detail`、`song_chorus`、`song_dynamic_cover`、`song_creators` | 详情、可选音质、封面或副歌时间不保证有可播放地址 |
| 播放地址与可用性 | `song_url`、`song_url_v1`、`song_url_v1_302`、`check_music` | 新版默认 xeapi；302 是服务包装；账号/版权/地区/试听/音质和临时地址有效性都需处理 |
| 下载地址与购买记录 | `song_download_url*`、`song_cloud_download`、`song_downlist`、`song_purchased` | 获取下载信息不包含本地任务管理、断点续传或转码；受远端权益约束 |
| 其他音源匹配 | `song_url_match`、`song_url_ncmget`、song_url_v1 的 unblock 分支 | 包含独立来源/辅助依赖；与网易原始音源区分，首阶段不移植 |
| 歌词与歌词摘录 | `lyric`、`lyric_new`、`song_lyrics_mark*`、`cloud_lyric_get` | 包括逐行/逐字数据及摘录；翻译、逐字轨等可能缺失，本地时间同步与渲染另做 |
| 用户歌单与歌单发现 | `user_playlist*`、`playlist_detail`、`playlist_track_all`、`playlist_catlist`、`top_playlist*` | 注意分页与曲目详情补取；私密歌单需要相应会话权限 |
| 歌单编辑与导入 | `playlist_create/delete`、`playlist_tracks`、`playlist_track_add/delete`、`playlist_*_update`、`playlist_import_*` | 有所有权与服务端约束；导入可能是异步任务，不把创建任务等同完成 |
| 喜欢与收藏 | `like*`、`likelist`、`song_like*`、`playlist_subscribe`、`artist_sub`、`album_sub`、`mv_sub` | 账号写操作；提交后应处理服务端结果与本地状态一致性 |
| 歌手与专辑 | `artist_*`、`artists`、`album*`、`top_artists`、`top_album` | 作品、介绍、粉丝、收藏和新作；分页/地区/动态信息需分别查询 |
| 推荐、首页与私人 FM | `recommend_*`、`personalized*`、`personal_fm*`、`homepage_*`、`aidj_content_rcmd`、`playmode_*` | 个性化依赖账号和历史；上游给内容与服务能力，不提供我们完整的播放控制 |
| 曲风、相似内容与榜单 | `style_*`、`simi_*`、`toplist*`、`top_list`、`chart_*` | 推荐关系、榜单摘要和完整内容是不同调用；字段及范围逐项核对 |
| 云盘 | `user_cloud*`、`cloud`、`cloud_import`、`cloud_match`、`cloud_upload_token/complete` | 有列表、详情、删除、上传/直传及匹配；需要上传事务、取消、重试与完成校验 |
| 播放记录与听歌统计 | `record_recent_*`、`user_record`、`listen_data_*`、`summary_annual`、`music_first_listen_info` | 历史、足迹、时长和报告随账号/时间范围变化；不代替本地队列和缓存 |
| 播放事件上报 | `scrobble*`、`relay_play_state_submit`、`weblog` | 属于额外写入/上报，按真实播放语义设计；新打卡接口还有 NCBL 等额外协议工作 |
| 评论与互动 | `comment_*`、`comment`、`resource_like`、`hug_comment` | 包含评论读取/楼层/点赞/回复/删除/举报；写入可能受审核、限流和权限约束 |
| 社交与动态 | `event*`、`user_event*`、`follow`、`user_follow*`、`topic_*`、`user_social_status*` | 隐私、关注关系、发布和删除独立于播放；后续产品扩展 |
| 私信、通知与分享 | `msg_*`、`send_*`、`share_resource` | 通知读取与发送属于不同权限/副作用；首阶段不接入发送 |
| MV、视频与 Mlog | `mv_*`、`video_*`、`mlog_*`、`related_allvideo` | 列表/详情/地址/收藏；本地视频解码、画面与音频同步需要播放组件 |
| 播客、声音与电台 | `dj_*`、`voice_*`、`voicelist_*`、`user_audio` | 分类、节目、详情、订阅、付费内容和声音上传；不保证付费资源可播放 |
| 广播、DIFM、助眠与跑步 | `broadcast_*`、`dj_difm_*`、`sati_*`、`radio_sport_get` | 分属不同场景与内容服务；按账号、地区和服务可用性独立验证 |
| 一起听 | `listentogether_*` | 建房/加入/状态/心跳/播放命令/队列同步；本地时钟、同步冲突与断线恢复另行设计 |
| 数字专辑与购买 | `digitalAlbum_*`、`album_list*`、`album_songsaleboard` | 销售、详情、已购与下单；支付/购买不属于播放器首阶段 |
| VIP、云贝、广告与签到 | `vip_*`、`yunbei_*`、`ad_*`、`daily_signin`、`signin_progress` | 查询和领取/任务写操作分开；服务端任务条件及反作弊规则限制可用性 |
| 音乐人、粉丝中心与创作者 | `musician_*`、`fanscenter_*`、`creator_authinfo_get` | 通常有角色和账号资格要求；普通账号不保证能使用 |
| UGC、云小编与百科 | `ugc_*`、`rep_ugc_*`、`thinktank_*`、`song_wiki_*` | 包含读取、贡献、审核/任务等；部分模块缺少完整文档，需源码与实测补充 |
| 乐谱与其他内容 | `sheet_list`、`sheet_preview`、`calendar`、`starpick_comments_summary` | 返回数据或资源不包含我们的渲染/导出/编辑功能 |
| 通用调用与辅助 | `api`、`batch`、`decrypt`、`eapi_decrypt`、`inner_version`、`register_checktoken_*` | 是调试/封装/初始化能力；不直接作为用户功能，也不意味着任意接口稳定可调 |

表中如 `playlist_create/delete` 表示两个相关模块的简写，精确文件名以第 5 节为准；星号表示同前缀家族，不是通配路由。

## 3. 需要明确保留的边界

1. **传输是 HTTP，协议实现不只是 URL。** eapi/weapi/xeapi/linuxapi/api、设备字段、签名、Cookie、加密响应及个别额外协议共同决定能否调用。只移植 module 文件会遗漏 request/crypto/初始化依赖。
2. **Node 服务能力与网易业务能力分开。** GET/POST 外壳、CORS、缓存、302、二维码图片、代理以及部署配置由 Enhanced 提供；原生 C# 按实际应用需要保留相应行为。
3. **账号状态不是永久凭据。** 二维码成功、账号有效、会话已保存分别确认；会话过期和验证要求需要用户重新完成登录。
4. **内容元数据不代表播放授权。** 无地址、试听、权限不足、地区限制及 URL 过期都要作为正常业务结果处理；不能从请求成功推断全部音乐可播。
5. **仓库有模块不代表当前可用。** 随仓文档仍保留“云村热评已下架、暂不可用”的历史说明；该类文档条目不作为候选可用承诺。新增或文档不全模块也仅标记源码存在。
6. **播放器仍需自身实现。** 音频/视频解码、缓存、队列、进度、后台播放、设备输出、歌词呈现、桌面交互和失败恢复不由接口库交付。
7. **扩展依赖单独核对。** 上传、识曲、音源匹配、一起听及新版上报有额外协议和依赖，不从“核心登录可用”外推这些能力已可用。

## 4. 接入登记方式

当前已接入端点、C# 入口、协议和真实验证边界统一维护在[HTTP 与会话契约](netease-http-session.md)第 1 节。本清单保留固定上游范围，不再复制一份容易过时的登录接入状态表。

新增能力时，在相应当前契约中登记上游模块、原生端点、C# 方法、请求协议与账号范围；自动测试和真实账号结果分别关联维护矩阵及当次记录。未实现的模块只作为调研候选，不登记为可用能力。

升级基线时按使用模块及共享依赖差异审查，不盲目覆盖 C# 实现；人工记录保留日期、账号权益条件的概括、网络环境、实际返回分类和未覆盖范围，不记录凭据。

## 5. 固定提交完整模块索引

以下 **440 项**直接来自固定提交的 Git tree，按本项目的 15 类归组，每个模块仅出现一次。文件路径为 `module/<名称>.js`，可在[固定源码目录](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/tree/a8c781fd64faab17fedfd46e0615a2609307f163/module)定位。仅是模块名称索引，不根据文件名猜测完整的请求参数、协议或返回 DTO；同一模块可能包含本地封装、多个调用或其他依赖。

### 5.1 登录、账号与设备（34 项）

- `activate_init_profile`、`captcha_safe_sent`、`captcha_sent`、`captcha_sent_v1`、`captcha_verify`
- `cellphone_existence_check`、`countries_code_list`、`device_kickoff`、`device_list`、`deviceinfo_center_upload`
- `get_userids`、`login`、`login_cellphone`、`login_qr_check`、`login_qr_create`
- `login_qr_key`、`login_refresh`、`login_status`、`logout`、`nickname_check`
- `rebind`、`register_anonimous`、`register_cellphone`、`register_checktoken_v2`、`register_checktoken_v3`
- `register_xeapikey`、`setting`、`user_account`、`user_binding`、`user_bindingcellphone`
- `user_replacephone`、`user_update`、`verify_getQr`、`verify_qrcodestatus`

### 5.2 用户资料、关注与动态（27 项）

- `avatar_upload`、`event`、`event_del`、`event_forward`、`event_privacy`
- `follow`、`hot_topic`、`lbs_city_code`、`topic_detail`、`topic_detail_event_hot`
- `topic_sublist`、`user_comment_history`、`user_detail`、`user_detail_new`、`user_event`
- `user_event_all`、`user_follow_mixed`、`user_followeds`、`user_follows`、`user_level`
- `user_medal`、`user_mutualfollow_get`、`user_social_status`、`user_social_status_edit`、`user_social_status_rcmd`
- `user_social_status_support`、`user_subcount`

### 5.3 搜索与识别（10 项）

- `audio_match`、`cloudsearch`、`search`、`search_default`、`search_hot`
- `search_hot_detail`、`search_match`、`search_multimatch`、`search_suggest`、`search_suggest_pc`

### 5.4 歌曲、歌词与播放地址（37 项）

- `check_music`、`like`、`like_v1`、`likelist`、`lyric`
- `lyric_new`、`sheet_list`、`sheet_preview`、`song_chorus`、`song_cloud_download`
- `song_copyright_rcmd`、`song_creators`、`song_detail`、`song_downlist`、`song_download_url`
- `song_download_url_v1`、`song_dynamic_cover`、`song_like`、`song_like_check`、`song_lyrics_mark`
- `song_lyrics_mark_add`、`song_lyrics_mark_del`、`song_lyrics_mark_user_page`、`song_monthdownlist`、`song_music_detail`
- `song_order_update`、`song_purchased`、`song_red_count`、`song_simi_get`、`song_singledownlist`
- `song_url`、`song_url_match`、`song_url_ncmget`、`song_url_v1`、`song_url_v1_302`
- `song_wiki_info`、`song_wiki_summary`

### 5.5 歌单（34 项）

- `pl_count`、`playlist_category_list`、`playlist_catlist`、`playlist_cover_update`、`playlist_create`
- `playlist_delete`、`playlist_desc_update`、`playlist_detail`、`playlist_detail_dynamic`、`playlist_detail_rcmd_get`
- `playlist_highquality_tags`、`playlist_hot`、`playlist_import_name_task_create`、`playlist_import_task_status`、`playlist_mylike`
- `playlist_name_update`、`playlist_order_update`、`playlist_privacy`、`playlist_subscribe`、`playlist_subscribers`
- `playlist_tags_update`、`playlist_track_add`、`playlist_track_all`、`playlist_track_delete`、`playlist_tracks`
- `playlist_update`、`playlist_update_playcount`、`playlist_video_recent`、`related_playlist`、`top_playlist`
- `top_playlist_highquality`、`user_playlist`、`user_playlist_collect`、`user_playlist_create`

### 5.6 歌手与专辑（35 项）

- `album`、`album_detail`、`album_detail_dynamic`、`album_list`、`album_list_style`
- `album_new`、`album_newest`、`album_privilege`、`album_songsaleboard`、`album_sub`
- `album_sublist`、`artist_album`、`artist_desc`、`artist_detail`、`artist_detail_dynamic`
- `artist_fans`、`artist_follow_count`、`artist_list`、`artist_mv`、`artist_new_mv`
- `artist_new_song`、`artist_new_song_mv_list_v2`、`artist_new_song_playall`、`artist_songs`、`artist_sub`
- `artist_sublist`、`artist_top_song`、`artist_video`、`artists`、`digitalAlbum_detail`
- `digitalAlbum_ordering`、`digitalAlbum_purchased`、`digitalAlbum_sales`、`top_album`、`top_artists`

### 5.7 发现、推荐、曲风与榜单（42 项）

- `aidj_content_rcmd`、`banner`、`calendar`、`chart_detail`、`chart_song_detail`
- `fm_trash`、`history_recommend_songs`、`history_recommend_songs_detail`、`homepage_block_page`、`homepage_dragon_ball`
- `personal_fm`、`personal_fm_mode`、`personalized`、`personalized_djprogram`、`personalized_mv`
- `personalized_newsong`、`personalized_privatecontent`、`personalized_privatecontent_list`、`playmode_intelligence_list`、`playmode_song_vector`
- `program_recommend`、`recommend_resource`、`recommend_songs`、`recommend_songs_dislike`、`simi_artist`
- `simi_mv`、`simi_playlist`、`simi_song`、`simi_user`、`style_album`
- `style_artist`、`style_detail`、`style_list`、`style_playlist`、`style_preference`
- `style_song`、`top_list`、`top_song`、`toplist`、`toplist_artist`
- `toplist_detail`、`toplist_detail_v2`

### 5.8 云盘与文件上传（9 项）

- `cloud`、`cloud_import`、`cloud_lyric_get`、`cloud_match`、`cloud_upload_complete`
- `cloud_upload_token`、`user_cloud`、`user_cloud_del`、`user_cloud_detail`

### 5.9 评论与互动（21 项）

- `comment`、`comment_add`、`comment_album`、`comment_delete`、`comment_dj`
- `comment_event`、`comment_floor`、`comment_hot`、`comment_hug_list`、`comment_info_list`
- `comment_like`、`comment_music`、`comment_mv`、`comment_new`、`comment_playlist`
- `comment_reply`、`comment_report`、`comment_video`、`hug_comment`、`resource_like`
- `starpick_comments_summary`

### 5.10 MV、视频与 Mlog（22 项）

- `mlog_music_rcmd`、`mlog_to_video`、`mlog_url`、`mv_all`、`mv_detail`
- `mv_detail_info`、`mv_exclusive_rcmd`、`mv_first`、`mv_sub`、`mv_sublist`
- `mv_url`、`related_allvideo`、`top_mv`、`video_category_list`、`video_detail`
- `video_detail_info`、`video_group`、`video_group_list`、`video_sub`、`video_timeline_all`
- `video_timeline_recommend`、`video_url`

### 5.11 播客、电台、广播与声音（54 项）

- `broadcast_category_region_get`、`broadcast_channel_collect_list`、`broadcast_channel_currentinfo`、`broadcast_channel_list`、`broadcast_sub`
- `djRadio_top`、`dj_banner`、`dj_category_excludehot`、`dj_category_recommend`、`dj_catelist`
- `dj_detail`、`dj_difm_all_style_channel`、`dj_difm_channel_subscribe`、`dj_difm_channel_unsubscribe`、`dj_difm_playing_tracks_list`
- `dj_difm_subscribe_channels_get`、`dj_hot`、`dj_paygift`、`dj_personalize_recommend`、`dj_program`
- `dj_program_detail`、`dj_program_toplist`、`dj_program_toplist_hours`、`dj_radio_hot`、`dj_recommend`
- `dj_recommend_type`、`dj_sub`、`dj_sublist`、`dj_subscriber`、`dj_today_perfered`
- `dj_toplist`、`dj_toplist_hours`、`dj_toplist_newcomer`、`dj_toplist_pay`、`dj_toplist_popular`
- `radio_sport_get`、`sati_resource_list`、`sati_resource_list_more`、`sati_resource_sub`、`sati_resource_sub_list`
- `sati_tag_list`、`sati_timescene_resources_get`、`user_audio`、`user_dj`、`voice_delete`
- `voice_detail`、`voice_lyric`、`voice_upload`、`voicelist_detail`、`voicelist_list`
- `voicelist_list_search`、`voicelist_my_created`、`voicelist_search`、`voicelist_trans`

### 5.12 听歌记录、上报与一起听（29 项）

- `listen_data_realtime_report`、`listen_data_report`、`listen_data_song_play_rank`、`listen_data_today_song`、`listen_data_total`
- `listen_data_year_report`、`listentogether_accept`、`listentogether_end`、`listentogether_heatbeat`、`listentogether_play_command`
- `listentogether_room_check`、`listentogether_room_create`、`listentogether_status`、`listentogether_sync_list_command`、`listentogether_sync_playlist_get`
- `music_first_listen_info`、`recent_listen_list`、`record_recent_album`、`record_recent_dj`、`record_recent_playlist`
- `record_recent_song`、`record_recent_video`、`record_recent_voice`、`relay_play_state_submit`、`scrobble`
- `scrobble_v1`、`summary_annual`、`user_record`、`weblog`

### 5.13 私信、通知与分享（11 项）

- `msg_comments`、`msg_forwards`、`msg_notices`、`msg_private`、`msg_private_history`
- `msg_recentcontact`、`send_album`、`send_playlist`、`send_song`、`send_text`
- `share_resource`

### 5.14 会员、任务、音乐人与贡献（70 项）

- `ad_get`、`ad_listening_rights`、`ad_listening_rights_gain`、`creator_authinfo_get`、`daily_signin`
- `fanscenter_basicinfo_age_get`、`fanscenter_basicinfo_gender_get`、`fanscenter_basicinfo_province_get`、`fanscenter_overview_get`、`fanscenter_trend_list`
- `middle_play_do_lottery`、`middle_play_lottery_remain_chance`、`musician_cloudbean`、`musician_cloudbean_obtain`、`musician_data_overview`
- `musician_play_trend`、`musician_sign`、`musician_tasks`、`musician_tasks_new`、`musician_vip_tasks`
- `rep_ugc_activity_collect`、`rep_ugc_activity_get`、`rep_ugc_exam_info_get`、`rep_ugc_exam_question_single_get`、`rep_ugc_exam_result_get`
- `rep_ugc_exam_start`、`rep_ugc_exam_submit`、`rep_ugc_user_collect-vip`、`rep_ugc_user_get`、`rep_ugc_user_sign`
- `rep_ugc_user_vip`、`sign_happy_info`、`signin_progress`、`thinktank_audit_resource_detail`、`thinktank_audit_resource_update`
- `threshold_detail_get`、`ugc_album_get`、`ugc_artist_get`、`ugc_artist_search`、`ugc_detail`
- `ugc_mv_get`、`ugc_song_get`、`ugc_user_devote`、`vip_growthpoint`、`vip_growthpoint_details`
- `vip_growthpoint_get`、`vip_growthpoint_getall`、`vip_info`、`vip_info_v2`、`vip_sign`
- `vip_sign_detail`、`vip_sign_history`、`vip_sign_info`、`vip_tasks`、`vip_tasks_v1`
- `vip_timemachine`、`yunbei`、`yunbei_expense`、`yunbei_info`、`yunbei_rcmd_song`
- `yunbei_rcmd_song_history`、`yunbei_receipt`、`yunbei_sign`、`yunbei_task_finish`、`yunbei_task_finish_v1`
- `yunbei_task_list_v1`、`yunbei_task_recommend_song`、`yunbei_tasks`、`yunbei_tasks_todo`、`yunbei_today`

### 5.15 通用协议与辅助（5 项）

- `api`、`batch`、`decrypt`、`eapi_decrypt`、`inner_version`

## 6. 来源与维护

- [上游仓库](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced)与[固定源码提交](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/tree/a8c781fd64faab17fedfd46e0615a2609307f163)：模块及共享依赖的身份。
- [随仓接口文档](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/public/docs/home.md)：能力说明与已知限制；与模块实现不一致时逐项注明并实测。
- [二维码状态模块](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/login_qr_check.js)、[登录状态模块](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/login_status.js)：本阶段关键行为。
- [新版播放地址模块](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/song_url_v1.js)：xeapi 与独立音源匹配分支。
- [项目许可](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/LICENSE)：移植时保留适用版权和许可说明，依赖包另查各自许可。

本清单不随 main 自动变化。每次计划接入或上游升级时重新核对选中模块、共享协议与实测记录，并同步[能力路线图](../roadmap/netease-capability-roadmap.md)、相应当前契约和验证矩阵；登录回归继续使用[维护矩阵](../maintenance/netease-login-verification.md)。归档的 V1 计划保留历史，不再承担当前接入登记。
