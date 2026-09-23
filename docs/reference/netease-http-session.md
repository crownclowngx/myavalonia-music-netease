# 网易登录 HTTP 与会话契约

> 状态：当前登录与会话实现，默认微信扫码、网易云 App 备用；核对日期：2026-09-22。自动验证见[专用回归矩阵](../maintenance/netease-login-verification.md)，真实微信闭环见[微信专项记录](../archive/records/netease-v1/wechat-login-implementation-20260922.md)。

## 1. 接入范围与来源

原生 C# + Flurl.Http 4.0.2 直连网易 HTTPS。上游固定为 api-enhanced 提交
`a8c781fd64faab17fedfd46e0615a2609307f163`，移植许可见 [THIRD-PARTY-NOTICES](../../THIRD-PARTY-NOTICES.md)。
不运行 Node 服务，不调用 Enhanced 的 HTTP 外壳路由。下表是当前已接入事实；[440 个上游模块索引](netease-api-enhanced-capabilities.md)是后续调研范围。

| C# 入口 | 上游模块 | 原生行为 | 验证边界 |
| --- | --- | --- | --- |
| WeChatQrLoginProvider | 独立补充，非上游模块 | 网易 SNS 入口 → 微信 QR 长轮询 → 网易 `/back/weichat` | 真实微信确认、网易回调和账号核验成功 |
| NeteaseAuthApi.CreateKeyAsync | login_qr_key | eapi POST `/api/login/qrcode/unikey`，type=3 | 离线契约与无历史 Cookie 联网成功 |
| LoginQrCode.Render | login_qr_create | 本地 pc 登录链接，QRCoder 生成 PNG | 编码、图片、实际 Avalonia 渲染；无额外 HTTP |
| NeteaseAuthApi.CheckQrAsync | login_qr_check | eapi POST `/api/login/qrcode/client/login` | 800–803 离线测试；联网观测 801 |
| NeteaseAuthApi.CheckAccountAsync | login_status | weapi POST `/api/w/nuser/account/get` | 一致性离线测试、联网未登录和真实微信授权账号核验 |
| ProtectedLoginSessionStore | 本地能力 | DPAPI 保存、加载、清除 | 隔离文件测试与本机 CurrentUser 保护测试 |
| NeteaseAuthApi.LogoutAsync | logout | eapi POST `/api/logout` | 离线流程；真实账号远端退出待验证 |

eapi/weapi 的逻辑路径由编码器变成 `/eapi/…`、`/weapi/…`。P0 全新进程证明网易云 App 路径获取 QR 和 801 不需要匿名 Cookie 或 xeapi 初始化；该路径的真实 803 授权仍缺单独验证记录。微信路径已完成真实授权、回调与账号核验。
未实现短信/密码登录、扫码 Cookie 刷新和多账号。V2 已新增 xeapi、搜索、详情和单曲播放，端点、媒体与引擎契约见[音乐与播放当前契约](netease-music-playback.md)。

V4.1 已新增当前账号歌单、n=0 详情与分批曲目读取；账号 ID 在提交者锁内随 epoch 捕获，新增端点复用受控提交和响应预算。协议及浏览约定见[日常播放器当前实现](netease-daily-player.md)。

## 2. 职责与资源所有权

- `MainDocument` 负责页面状态投影和命令；依赖登录服务、UI 调度、头像端口与公开 `IDocumentLifetime`。不操作 Flurl、Cookie 或文件。
- `LoginCoordinator` 负责单账号状态、轮询、操作代次、保存和退出顺序；网络、存储和时间分别注入。
- `IQrLoginProvider` 只负责创建扫码尝试；`IQrLoginAttempt` 拥有二维码图片、私有状态与释放责任。微信与网易 App 各实现一次创建/检查，共享后续账号核验与保存；View 不接触 UUID、codekey、code 或 state。
- `NeteaseAuthApi` 解释四个端点；`NeteaseTransport` 执行 HTTP、限制响应大小并转换安全异常；`NeteaseRequestEncoder`/`NeteaseCrypto` 负责协议。
- `ProtectedLoginSessionStore` 只处理文件；`ISessionProtector` 只处理当前用户保护。平台保护失败不能降级为明文。
- `AddMusicNetEasePluginServices` 是唯一组合入口。服务容器复用九个命名 Client：web、eapi、xeapi、keys、media、images、social、wechat、wechat-poll；`NeteaseFlurlClients.Dispose` 清空私有缓存并释放 Client。没有静态 CookieJar 或全局 Flurl 修改。
- `MainView` 拥有解码后的二维码/头像 Bitmap，替换、解绑、视觉树拆卸时释放。暂时离开视觉树不代表 Document 关闭。
- Host 生命周期先异步停止播放并释放媒体，再撤销登录工作，最后由容器释放依赖；同时支持 SDK 当前使用的同步 Dispose。Standalone 使用相同服务与 View，仅提供关闭令牌及独立目录。

采用普通类、枚举、不可变记录和少量窄接口；没有通用 API 平台、事件总线、通用仓储或状态机框架。

## 3. HTTP、编码与失败

eapi/weapi POST 使用 Flurl 表单编码，params 和 encSecKey 只编码一次；微信授权链使用 Flurl GET 与查询参数编码。
JSON 属性顺序、中文、emoji、布尔/空值和长整数与固定上游向量对照；emoji 还原不会修改字面的反斜杠转义。
weapi 使用两层 AES-CBC 与协议规定的原始 RSA 运算，不能替换成默认 RSA OAEP；eapi 使用协议规定的摘要拼接和 AES-ECB。

业务请求和响应读取共用 15 秒取消预算，响应上限 1 MiB；支持明文 JSON、eapi 加密响应及解密后的 gzip，上限同样约束解压结果。
HTTP 状态与正文 code 分开解释；未知业务码、畸形账号、身份不一致均失败。
默认禁止重定向，不缓存登录结果，不自动重放 POST。取消使用 OperationCanceledException；其余使用 AuthException，保留分类、HTTP 状态、业务码和可选 Retry-After，不保留底层异常链或原始正文。
当前不启用请求日志，也不输出二维码 key、Cookie/CSRF、二维码 URL 或账号完整响应。

头像使用独立 Flurl Client，仅允许网易图片子域、默认端口、无 userinfo 的 HTTP/HTTPS 地址；HTTP 升级为 HTTPS，禁止重定向，10 秒/2 MiB 限制。
头像不带账号 Cookie 或 CSRF；下载、取消、解码失败使用占位图，迟到结果不能覆盖新状态。

## 4. 状态、取消和提交

轮询间隔 2 秒，每次响应处理完毕后才等待下一次；整个尝试最多 3 分钟。
网络/超时最多连续重试 2 次，额外退避 2、4 秒；HTTP 429 仅在给出 Retry-After 时有限重试（最大接纳 180 秒，仍受总预算约束）。
未知业务码、验证要求和无 Retry-After 的限流停止流程，不自动无限重登。

801 等待扫码，802 等待手机确认，803 进入账号核验；必须收到 MUSIC_U，且账号 id 与 profile.userId 一致，才能保存并发布登录结果。
800 或本地预算耗尽停止轮询，等待用户重新生成二维码。

新操作原子检查当前状态、登记代次并取消旧操作；等待旧操作及磁盘补偿完成后再执行。
每个请求携带不可变 Cookie 快照，Set-Cookie 只返回新快照；无共享容器接受迟到响应。
CookieContainer 解释标准过期/删除属性，白名单只保留 MUSIC_U、MUSIC_A、__csrf、NMTID；新会话 Cookie 不继承旧过期时间。

保存前观察取消，保存后在同一锁内决定发布或补偿。显式扫码若在落盘后被取消，先删除本次会话，再让后继操作运行；补偿失败明确显示仍需清理。
已核验账号保存失败时维持内存登录，但标记未记住；“重试保存”不重新要求授权。
恢复只读取 Cookie 后检查账号，不调用 login/refresh；网络失败保留文件，远端判失效后不发布登录状态。

退出立即撤销内存账号与旧代次，等待旧操作收口，清除本地文件，再请求远端退出。
即使退出令牌已取消，本地清除仍执行；本地清除失败和远端未确认分别报告。
关闭发起页面只取消它拥有的操作，其他页面关闭不能取消它；已经提交的共享账号不会因页面关闭而退出。

音乐访问经 `LoginMusicSession` 获得带账号 epoch、凭据版本和撤销 Token 的内部快照；没有第二份账号存储。Cookie 更新复用此处的提交/补偿顺序，退出会立即撤销搜索、资源解析、缓冲和播放资格；旧 epoch 的失效响应不会退出新登录账号。

## 5. 数据目录与文件格式

公开 SDK 3.4.1 当前核查未发现可用的数据目录端口，因此沿用邻仓插件的 LocalApplicationData 布局，并支持组合入口注入绝对目录：

| 运行方式 | 默认路径（相对当前用户 LocalApplicationData） |
| --- | --- |
| Host 插件 | `MyAvaloniaManagement/Plugins/myavalonia.plugin.music.netease` |
| Standalone | `MyAvaloniaManagement/Standalone/myavalonia.plugin.music.netease` |

Standalone 可用 `--data-dir` 指定独立开发目录；不要与正在运行的另一实例共享该目录。当前实现保证单容器操作顺序，不提供跨进程账号锁。
文件不会写入 Controls 或默认源码目录。

- `device.id`：非敏感、稳定的 52 位十六进制设备标识；退出保留。
- `session.bin`：Windows DPAPI CurrentUser 保护的版本 1 JSON，包含设备标识及四类必要 Cookie/有效期；最大 128 KiB。不保存密码、验证码、二维码 key 或账号完整正文。
- 同目录随机临时文件，写入并 Flush 后原子替换；提交前可取消，提交后由协调器核对代次。退出仅删除 session.bin。
- 坏文件、未知版本、保护不可用、目录/删除失败均为 Storage 错误；可以显式重新扫码修复设备元数据。会话内嵌设备标识优先，避免两文件更新中断破坏已提交会话。

当前目标仍为 Windows x64。其他系统不会明文落盘；平台扩展必须另加保护实现与验证。

## 6. 回归与上游升级

执行[本地开发门禁](../maintenance/netease-login-verification.md)。协议向量由 [generate-protocol-vectors.cjs](../../tools/generate-protocol-vectors.cjs)调用固定提交的原始 crypto 模块产生，并校验源码 SHA-256；Node 仅用于开发时重建向量。
升级先比较四个登录模块、request、crypto、config 和必要初始化逻辑，再更新向量、契约及记录，不能直接用最新版覆盖所有 C# 行为。

## 7. 微信网站登录协议

此流程以网易现有公开登录页面为入口，不是网易向第三方承诺稳定的开放平台 API，也不在上游 440 个模块中。
应用不申请另一套 AppID、不持有 AppSecret，授权码由网易回调兑换。官方页面或风控规则变化时可能需要适配。

1. GET `https://music.163.com/api/sns/authorize?snsType=10&clientType=web2&callbackType=Login&forcelogin=true`，读取网易发出的微信重定向。
2. 仅接受 `https://open.weixin.qq.com/connect/qrconnect`、`snsapi_login` 与网易 `/back/weichat` 回调；保留本次 state。
3. 读取微信页面 `/connect/qrcode/{uuid}` 图片。只解析有限 HTML 字段，轮询响应只解析简单赋值，不执行 JavaScript。
4. 按页面标记选择 `lp.open.weixin.qq.com` 或 `long.open.weixin.qq.com` 的 `/connect/l/qrconnect`。已扫描后附 `last=404`。
5. 405 带 code 才进入网易 `/back/weichat?code=…&state=…`，限制最多 5 次同源 HTTPS 请求；获得 MUSIC_U 后再复用账号核验与安全保存。

| 微信状态 | 本地行为 |
| --- | --- |
| 408 | 未扫码时等待扫码；已扫码时继续等待手机确认 |
| 404 | 已扫码，等待确认 |
| 403 | 手机取消，终止并清除二维码 |
| 402 | 二维码过期，终止等待 |
| 405 + code | 完成网易回调；不直接宣告已登录 |
| 其他状态/畸形正文 | 受控失败，提示重试或使用网易云 App |

微信长轮询及正文读取预算为 35 秒，普通请求 15 秒；整个尝试仍限 3 分钟。
授权页/回调响应上限 1 MiB，二维码 2 MiB，轮询 16 KiB；图片检查 PNG/JPEG 文件头。
每次尝试持有单独 CookieJar，按域名和路径发送，既有网易会话不会进入新授权链。
只有网易响应的白名单 Cookie 转为候选账号；微信临时 Cookie、UUID、state、code 不落盘、不写日志。
回调是一次性兑换，失败后终止，禁止被普通轮询重试重放。无 MUSIC_U 时明确提示绑定或额外验证，不一直等待。
取消、切换方式、关闭页面或超时使旧代次失效；旧回调即使迟到也不能提交账号。

协议依据：[网易 SNS 登录入口](https://music.163.com/api/sns/authorize?snsType=10&clientType=web2&callbackType=Login&forcelogin=true)与该入口当次返回的微信网站授权页（2026-09-22 观察）。不把易变页面协议宣称为官方 SDK 契约。
