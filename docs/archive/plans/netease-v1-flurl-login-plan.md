# 网易云音乐 V1：Flurl 接入与扫码登录实施计划

> 归档收口：2026-09-23，现有 V1–V4 已实现并由用户确认手工验收完成，见[验收收口记录](../records/netease-v1-v4-acceptance-20260923.md)。
> 下文保留各阶段设计、当时状态与待办快照；不作为当前待实施清单。现行行为以[播放契约](../../reference/netease-music-playback.md)、[日常播放器](../../reference/netease-daily-player.md)和[文档导航](../../README.md)为准；后续候选由[路线图](../../roadmap/netease-capability-roadmap.md)承接。

## 历史方案快照

> 归档状态：2026-09-22 已归档。用户确认登录可用，原 P4–P5 由[能力路线图](../../roadmap/netease-capability-roadmap.md)承接；剩余人工验证由[维护矩阵](../../maintenance/netease-login-verification.md)跟踪。
> 下文保留原计划及当时的状态、约束和待办。现行行为以[HTTP 与会话契约](../../reference/netease-http-session.md)为准；阶段证据见[归档索引](../README.md)。
> 2026-09-23 后续设计入口：[V2 / M1 实施方案](netease-v2-m1-playback-plan.md)已纳入 LibVLC 内置优先/指定共享目录、账号设置 Tool 和 Dock 生命周期；详见[专项设计](netease-v2-libvlc-tool-and-dock-design.md)。本页 V1 历史实现状态不因此改变。

> 用途：以 api-enhanced 为主要协议上游，为 MusicNetEasePlugin 建立原生 C# 网络接入，先完成扫码登录最短闭环，再扩展播放器。
> 状态：P0–P3 及微信默认登录已实现；2026-09-22 微信真实扫码、网易回调和账号核验通过，Host/重启恢复仍待验证。见[初期记录](../records/netease-v1/login-implementation-20260922.md)和[微信专项记录](../records/netease-v1/wechat-login-implementation-20260922.md)。P4–P5 保留为后续计划。
> 本仓调研基线：`1d58e97a4f65a915f3dabc0318837b491a645bc5`；编写前工作树干净。实施前重新检查工作树。
> 配套：[专用开发验证计划](../../maintenance/netease-login-verification.md)、[上游能力清单](../../reference/netease-api-enhanced-capabilities.md)。

V1 是本计划的阶段标识，不修改产品、SDK 或插件包版本。“上游有接口”“C# 已实现”“自动测试通过”“真实账号验证通过”分别记录，不能相互替代。

## 1. 目标与首要约束

1. **SOLID 是首要规定。** 按变化原因分开接口协议、HTTP 传输、登录编排、会话保存与界面状态；明确依赖方向、生命周期和失败责任。
2. **所有插件主动发起的 HTTP 请求统一使用 Flurl。** 参照邻仓已有的命名 Client、集中配置、适配器和异常转换方式；不在 ViewModel、二维码生成或图片加载中散落另一套网络调用。
3. **设计模式朴素使用。** 使用普通类、记录、枚举、构造注入和少量窄接口；仅在网络、会话存储、时间等真实替换边界引入抽象。不引入通用 API 平台、消息总线或状态机框架。
4. **注释使用详细中文。** 解释设计思路、协议与编码差异、Cookie 所有权、取消和迟到响应处理、保存失败及资源释放；公开契约写清输入、结果和异常。避免只把代码逐行翻译成中文。
5. **单元测试与本地开发门禁齐全。** 每项关键行为能映射到配套矩阵，验证失败分支、并发边界和资源释放，不能只断言 mock 调用次数。
6. **同步文档并保留专用验证文档。** 采用 host 的 roadmap、当前契约、maintenance 与 archive 分类，规则见第 9 节。
7. **不使用 AIFLOW。不新增或运行 Windows CI、发布门禁。** 本阶段不执行发布 Windows Smoke、seal、发布覆盖率、发布重复性检查、打包发布或自动部署。正式发布阶段再使用相应发布门禁，不改变原发布规则。

项目所有者已于 2026-09-22 授权按本计划实施并分阶段提交 Git。P0–P3 是本轮登录里程碑；P4–P5 仍作为后续扩展，阶段结论以记录为准。

后续明确追加：默认微信扫码，保留网易云 App 备用；部署到 `D:\data\avalonia\Controls\` 并清理中间文件。
微信采用独立 Provider，经网易官方 SNS 入口和回调取得会话，复用账号验证、存储与生命周期，不复用 App codekey。
下文原始 P0–P3 的 App 协议选型作为实施历史保留，现行协议和入口以[当前契约](../../reference/netease-http-session.md)为准。

## 2. 实施前事实与上游基线

下表的“模板/未引入”是原始调研基线；现行实现见[HTTP 与会话契约](../../reference/netease-http-session.md)，不将历史基线当作当前状态。

| 项目 | 核对结果 |
| --- | --- |
| 插件身份 | `myavalonia.plugin.music.netease`，沿用既有身份 |
| 项目结构 | Plugin 为业务实现，Standalone 复用同一对象图，Tests 引用 Plugin |
| 当前代码 | 模板 MainDocument、示例命令与测试；服务注册入口尚无业务注册 |
| 目标框架 | 当前为 net10.0 / C# 14；现有插件交付目标 win-x64 |
| 依赖 | 当前未引入 Flurl、二维码与网易协议实现 |
| HTTP 参考 | 百度网盘插件集中管理命名 Flurl Client，并在适配器转换业务错误；百度和 Bili 插件均固定 Flurl.Http 4.0.2 |
| 上游 | [NeteaseCloudMusicApiEnhanced/api-enhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) |
| 固定源码提交 | [a8c781fd64faab17fedfd46e0615a2609307f163](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/tree/a8c781fd64faab17fedfd46e0615a2609307f163)，提交时间 2026-09-11 UTC |
| 调研方式 | 阅读固定提交源码与随仓文档；尚未用真实账号实测登录或播放 |

网络约定的本地参考：

- [百度 Flurl 基础设施](../../../../myavalonia-baidu-netdisk/src/BaiduDiskPlugin.Plugin/Infrastructure/Http/BaiduHttpInfrastructure.cs)：Client 所有权和错误摘要。
- [百度 OAuth 适配器](../../../../myavalonia-baidu-netdisk/src/BaiduDiskPlugin.Plugin/Infrastructure/Http/BaiduOAuthApi.cs)：依赖注入、DTO 转换与取消传递。
- [百度集中依赖版本](../../../../myavalonia-baidu-netdisk/Directory.Packages.props)。
- [当前插件服务注册](../../../src/MusicNetEasePlugin.Plugin/Plugin/MusicNetEasePluginServices.cs)。

上述是请求组织方式的参考；网易二维码授权不是百度 OAuth，不能复用百度的授权码、refresh token 或错误码语义。第一版只在本插件内部统一，不先修改 host 或抽取跨插件公共 HTTP 包。

## 3. 第一阶段：登录最短路径

### 3.1 用户可见闭环

打开网易云音乐页面 → 点击微信扫码登录 → 显示微信二维码 → 微信扫码确认 → 网易回调与账号核验 → 页面显示账号 ID、昵称和头像 → 关闭后重开可检查并恢复会话 → 主动退出后清除本地登录信息。网易云 App 扫码为独立备用入口。

范围包含单账号登录、状态反馈、取消、二维码过期重取、会话恢复和退出。短信/密码登录、多账号切换、搜索与播放、推荐、云盘、评论和写入歌单属于后续扩展。账号页面可先使用占位头像，头像获取失败不影响登录结论。

成功条件：

- 803 响应只表示二维码授权成功；必须取得可用会话，再调用账号状态接口核对有效账号。
- 账号有效且会话保存成功，才能显示可在下次启动恢复；保存失败时明确提示“本次已登录，记住登录失败”，允许重试保存或退出。
- 关闭或取消登录后不再轮询，不会被迟到响应重新登录。
- 重开时只有状态检查通过才显示已登录；网络暂时不可用与凭据失效分别反馈。
- 退出后本地不得再恢复旧账号；远端退出失败要与本地清理结果分别显示。

### 3.2 实际接口与协议映射

表中“上游入口”属于 Enhanced 的 HTTP 外壳；“网易逻辑路径”由协议层变换成最终请求地址。原生 C# 不向网易直接发送 `/login/qr/key` 之类的外壳路由。

| 顺序 | 上游入口 / 模块 | 网易逻辑路径或本地行为 | 固定基线协议 |
| --- | --- | --- | --- |
| 1 | `login_qr_key` / `/login/qr/key` | POST `/api/login/qrcode/unikey`，参数 `type=3` | 模块使用默认选项；当前全局 encrypt=true，因此为 eapi |
| 2 | `login_qr_create` / `/login/qr/create` | 本地构造 `https://music.163.com/login?codekey=...` 并生成二维码 | 第一版采用默认 pc 形式，无额外 HTTP；web 的 chainId 暂不移植 |
| 3 | `login_qr_check` / `/login/qr/check` | POST `/api/login/qrcode/client/login`，参数 `key`、`type=3` | 默认 eapi；消费业务状态与响应 Cookie |
| 4 | `login_status` / `/login/status` | POST `/api/w/nuser/account/get`，检查账号及资料 | weapi |
| 5 | 会话保存 / 恢复 | 本地保存受保护会话；恢复后复用步骤 4 验证 | 本地存储 + weapi |
| 6 | `logout` / `/logout` | 先清除本地会话，再 POST `/api/logout` 确认远端 | 默认 eapi |

依据：[二维码 key][qr-key]、[二维码生成][qr-create]、[二维码检查][qr-check]、[账号状态][login-status]、[退出][logout]、[协议配置][config]及[请求核心][request]。

### 3.3 登录状态与轮询

| 远端业务码 / 本地事件 | 页面状态 | 行为 |
| --- | --- | --- |
| 创建 key 中 | 正在准备二维码 | 阻止重复创建，允许取消 |
| 801 | 等待扫码 | 继续轮询 |
| 802 | 已扫码，等待手机确认 | 继续轮询；此时不标记已登录 |
| 803 | 正在确认账号 | 停止轮询，合并本轮会话，检查账号，保存后发布登录结果 |
| 800 | 二维码已过期 | 停止轮询，由用户点击重新生成 |
| 用户取消 / 所有者页面关闭 | 已取消 | 取消本轮在途操作，旧结果不得提交 |
| 未知业务码 / 验证要求 | 登录失败或需要验证 | 显示受控错误，停止自动登录，保留重新尝试入口 |
| 请求超时 / 短暂断网 | 网络异常 | 允许有限退避；超过预算后停止并提示重试 |

800–803 是响应正文的业务状态，不是 HTTP 状态。建议初始轮询间隔 2 秒、单次 HTTP 超时 15 秒、本地尝试预算 3 分钟；这些已成为当前默认值，是仍待真实账号观察的客户端策略，不是网易承诺的二维码有效期。800 立即优先终止；不使用重叠定时回调；一次响应处理完毕后才等待下一轮。

每个插件会话同时只拥有一个登录尝试。尝试携带递增代次及取消令牌；刷新二维码、取消或退出后，旧代次不能更新页面、内存会话或磁盘。注入 TimeProvider，使超时与轮询测试不等待真实分钟。

### 3.4 源码差异与最小依赖核查

- 上游文档说明 `login/refresh` **不支持刷新扫码登录 Cookie**。第一版恢复已有 Cookie 后检查账号，失效时重新扫码，不承诺自动续期。[上游调用与登录说明][upstream-doc]
- 上游二维码图片生成是本地操作。C# 使用一个二维码生成组件，业务层只输出二维码内容，View 不拼接协议参数。
- 上游要求时间戳主要用于绕过自身 HTTP 外壳缓存。原生请求层不缓存登录、轮询或账号验证结果；协议需要的时间字段仍按固定基线构造。
- `noCookie` 在当前 server.js 中控制 Enhanced 对调用方输出的 Set-Cookie，不应直接等同于“C# 不向网易发送 Cookie”。移植以实际请求与会话行为为准。[HTTP 外壳][server]
- 当前 QR 流程直接依赖 eapi 与 weapi。上游匿名注册模块已使用 xeapi，初始化又涉及游客 Cookie、公钥；不能机械照搬整套 Node 启动顺序。P0 在全新数据目录验证 QR 路径是否需要匿名初始化；若确实需要，再加入该依赖与 xeapi 公钥初始化验证，不能用开发机已有 Cookie 掩盖依赖。[匿名注册][anonymous]、[初始化][generate-config]
- 当前播放地址 `song_url_v1` 默认采用 xeapi，因此第二阶段的播放链路必须单独验证 X25519、HMAC、AES-GCM、公钥与会话响应。它不自动成为 QR-only 阶段的前置条件。[播放地址][song-url]、[加密实现][crypto]
- 对照协议意图而非逐行机械翻译。比如上游 QR 检查的异常分支存在引用 try 内局部变量的问题；C# 用明确错误返回并测试，不复制异常分支缺陷。[二维码检查][qr-check]

## 4. Flurl 统一请求方式

### 4.1 请求路径与所有权

```text
MainDocument / 登录 ViewModel
    → 登录用例服务（状态、取消、会话提交）
        → INeteaseAuthApi
            → 网易接口适配器
                → 协议编码 / Flurl 请求执行 / 响应解码
                    → 网易 HTTPS 服务
        → ILoginSessionStore（受保护的本地会话）
```

`IFlurlClientCache` 和命名 Client 由插件私有服务容器拥有，统一在现有 AddMusicNetEasePluginServices 组合入口注册；容器结束时释放。客户端池可复用；登录 Cookie、设备信息与在途结果按当前会话/尝试隔离，不写入 Flurl 全局静态配置。

建议第一版命名 Client 对应 `music.163.com` 与 `interfacepc.music.163.com`；若 P0 证实需 xeapi，再加入 `interface3.music.163.com`。最终 URL 由已知端点和协议规则生成，业务层不传任意 URL。图片下载另用无账号 Cookie 的 Flurl Client，仍遵循同一配置与错误处理约定。

Flurl 的集中配置与测试机制以其[配置文档](https://flurl.dev/docs/configuration/)和[HttpTest 文档](https://flurl.dev/docs/testable-http/)为依据；实施时与所选版本 API 核对。

### 4.2 一次请求的固定顺序

1. 接口适配器创建明确 DTO、逻辑路径、协议种类与当前会话快照。
2. 协议层构造设备信息、必要 Cookie/CSRF、请求头和待加密 JSON；在加密边界明确属性顺序、空值、数值、UTF-8 与字节表示。
3. 编码后的表单由 Flurl 发送；不把需要表单传输的 params/encSecKey 当作 JSON 请求体，不重复 URL 编码。
4. 获取 HTTP 状态、响应头与原始响应体；按该协议决定解密、解压或解析 JSON。
5. 单独判断网络结果、协议结果与业务结果，映射到类型化返回。普通 HTTP 200 不代表登录成功，未知码不当成功。
6. 本轮候选会话先收集 Set-Cookie；只有代次仍有效且账号验证成功才发布。迟到请求不能通过共享 CookieJar 提前污染正式会话。

Client 级别只设置稳定的超时/代理/序列化与日志规则；会变的 Cookie、请求 ID 和设备头在请求级别设置。API 请求默认不自动跟随跨域重定向；如实测协议要求跳转，按已确认目标和凭据范围处理，并增加测试。

eapi/weapi 的加密 JSON与普通响应 DTO 的序列化配置分开管理；不能通过修改全局 JSON 选项影响密文。使用与上游独立核对的已知输入/输出向量验证，随机数与时钟在测试中受控，生产使用安全随机源。

### 4.3 错误、重试与诊断

统一错误包含分类、HTTP 状态、网易业务码、接口标识与安全摘要。至少区分取消、超时、网络失败、限流、会话失效、需要验证、协议解码失败及存储失败。保留有用分类，不向上透传包含完整 URL、Cookie 或原始正文的 Flurl 异常及 InnerException。

仅轮询等已确认可重复操作使用有限退避；不在底层无条件重放所有 POST。遇到限流或验证要求停止快速重试；可识别的 Retry-After 优先于本地间隔。创建 key、会话提交与退出不靠通用重试循环重复执行。

诊断记录接口名、耗时、状态和代次标识，不记录 Cookie、二维码 key、二维码完整 URL、CSRF、账号会话文件内容或完整密文。第一版不默认开启随机 IP、任意代理或第三方公共 API 中转。

### 4.4 依赖调整计划

已采用邻仓 Flurl.Http 4.0.2，并通过 net10.0 构建及锁文件验证。二维码、凭据保护与后续 xeapi 加密库按真实需要选择，不引入 Node 运行时。

新增依赖时同步集中版本、Plugin PackageReference、必要的 ManagedPluginPrivatePackage 和三个项目的 lock 文件。依赖声明完整性是本地开发检查；正式 ZIP、Windows Smoke 和发布验证留到发布阶段。具体打包惯例见[既有部署说明](../../maintenance/deployment-and-release.md)。

## 5. SOLID、对象寿命与会话保存

| 边界 | 职责与设计理由 |
| --- | --- |
| Auth API 窄接口 | 只提供 key、检查扫码、账号状态和退出；业务代码不依赖 Flurl 类型 |
| 协议编码器 | 只处理协议输入/输出和密码学规则；纯运算优先具体类或函数，不为每个算法建抽象层 |
| Flurl 请求执行器 | 统一传输、取消、超时及响应获取；不编排 UI 或写入账号文件 |
| 登录用例服务 | 拥有当前尝试、状态转换、代次校验和会话提交；使用枚举与普通方法即可 |
| 会话存储端口 | 负责受保护载荷的读写/清除及结果；以真实隔离目录验证提交，不模拟整个文件系统 |
| MainDocument / ViewModel | 投影账号及交互状态，转发用户意图，管理订阅与页面资源；不持有 host 内部对象 |

SOLID 落地检查：SRP 让上述职责各自变化；OCP 通过新增接口适配器扩展歌曲业务；LSP 要求替身与真实服务遵守相同取消和错误契约；ISP 保持登录接口精简；DIP 让用例依赖网络/存储端口而非具体 Flurl 或磁盘实现。具体内部类不强制配同名接口。

插件容器拥有共享账号会话和登录服务，Document 拥有自己的订阅和展示资源。登录尝试明确归属发起页面：该页面关闭时取消尝试，其他页面共享已提交账号状态；关闭页面不等于退出账号。Host 停止时取消并等待在途任务退出，再释放依赖；Standalone 使用相同组合入口和关闭逻辑。

会话策略：

- 第一版单账号；设备标识在初始化后稳定使用，不为每次轮询重新生成。数据放在插件专属可写数据目录，实施前核对 SDK 数据目录能力；不写入 Controls 安装目录或源码目录。
- 只保存恢复所需的 Cookie、设备/协议元数据与格式版本，不保存账号密码、验证码或二维码 key。Windows 目标使用当前用户级凭据保护；其他平台日后通过同一存储边界扩展，不承诺当前已支持。
- 正式会话与未完成登录尝试分离。网络过程不持有存储锁；提交前在短临界区重新核对代次，避免退出后被旧响应恢复。
- 保存使用同目录临时文件和原子替换；失败明确反馈且不破坏已提交文件。损坏、解密失败与未知格式进入可解释的恢复失败状态，不显示假登录。
- 启动检查遇到断网保留受保护凭据并标记待验证；明确账号失效时停止使用该凭据并提示重新扫码。
- 退出先使当前代次失效并阻止新的账号请求，以旧会话快照尝试远端退出，同时清除本地内存与持久化内容。清除失败必须显示未完整退出并提供重试，不能声称下次启动一定不会恢复。

## 6. 实施阶段与完成条件

| 阶段 | 工作内容 | 完成证据 |
| --- | --- | --- |
| P0：协议最小验证 | 固定上游；实现最少 eapi/weapi 运算与 Flurl 传输试验；在全新数据目录确认 key 获取、轮询、账号验证及匿名依赖 | 协议对照向量；最小接口观测记录；结论明确是否需要 xeapi 初始化。测试不依赖真实账号 |
| P1：登录用例 | 类型化 Auth API、轮询状态、取消/代次、错误映射、单账号会话与受保护存储 | 专用矩阵中的协议、请求、状态和存储单测通过 |
| P2：登录界面 | 复用现有 Main 页面；二维码、状态、重试、头像、恢复与退出；正确处理 Document/Standalone 生命周期 | UI 状态及组合测试；Standalone 本地交互；真实账号最短链人工验证单独记录 |
| P3：登录阶段收口 | 执行本地开发门禁、代码/中文注释审查、补齐当前契约与快速开始，记录尚未验证的 host 行为 | 完整 TRX、门禁结论与有日期记录；未验证项不标为通过 |
| P4：基础播放扩展 | 搜索 → 歌曲详情 → xeapi 播放地址 → 播放引擎 → 歌词；随后用户歌单 | 新的播放专项方案与验证矩阵；音频请求、解码、取消和资源寿命先做小范围验证 |
| P5：按需扩容 | 每日推荐、FM、收藏和歌单编辑、云盘、评论、视频等 | 从能力清单选择需求，每次同步协议与测试映射 |

P0–P3 构成本轮后续开发的登录里程碑。P4–P5 是扩容顺序；不把 440 个上游模块一次性移植作为登录完成条件。

播放器后续仍需独立负责解码、缓冲、播放队列、进度、音频输出与资源释放。选择播放组件时验证能否接受 Flurl 获取的流/本地缓冲，使应用主动 HTTP 保持统一；第三方引擎内部的媒体传输能力必须写明，不能悄悄引入第二套业务 HTTP 模式。

## 7. 本地开发门禁

详细要求与编号见[专用验证计划](../../maintenance/netease-login-verification.md)。当前实现与回归保持以下要求：

- 确定性协议对照、Flurl HttpTest 契约测试、登录状态与并发取消测试、真实隔离存储测试、UI 状态/组合测试和既有测试回归。
- 全解决方案 Debug 构建警告作为错误；测试完整执行、有测试发现、无失败，关键用例不得用 Skip 掩盖。失败、取消、超时、缺失或不完整报告均不算通过。
- 真实账号联网验证单独手动执行；自动单测无网络、无真实 Cookie，不使用个人数据目录。
- 文档链接、状态、接口映射、中文注释和职责审查；仅文档修改时只运行文档检查，不运行登录测试或重型发布流程。

已提供 [verify-development.ps1](../../../tools/verify-development.ps1)，编排锁定还原、Debug 构建、测试、TRX 和文档检查。不得假定 host 门禁会替本插件验证账号业务，也不引入 Windows CI 作为开发完成条件。

## 8. 上游跟进与能力边界

[能力清单](../../reference/netease-api-enhanced-capabilities.md)列出固定提交中的能力分组和 440 个模块索引。模块存在只证明上游提供相应实现入口，兼容性须逐项验证。

维护一份“C# 方法 → 上游模块 → 固定提交 → 协议 → 已验证场景”的映射。跟进上游时检查已用模块及其 request、crypto、config、初始化依赖的变化；选取必要修改，先更新协议向量和回归，再更新基线。保留移植代码所需的上游版权与许可信息。

账号、版权、地区、音质权限、试听与地址有效期仍由远端结果决定。上游其他音源匹配属于单独扩展，不能把匹配音源标记成网易账号原生权益。发布或商业使用前按当时适用许可及服务条件另行核对。

## 9. 文档同步与归档

参考 [host 文档维护规则](../../../../../avalonia_dock_simple_test/docs/maintenance/documentation.md)与 [host V22 方案结构](../../../../../avalonia_dock_simple_test/docs/roadmap/host-v22-plugin-distribution-plan.md)，只借鉴文档模式，不沿用该方案的业务或暂停状态。

| 位置 | 本项目维护约定 |
| --- | --- |
| 根 README | 项目定位、最短开发入口与本计划链接 |
| docs/README.md | 本插件唯一文档总导航，区分当前登录能力与后续计划 |
| docs/roadmap/ | 本方案、上游能力调研与待办；验证矩阵已迁至 maintenance；全部标记状态及日期 |
| docs/reference/ | 实施后新增统一 HTTP/会话契约及已接入接口映射；未实现前不写成现行契约 |
| docs/quick-start/ | 实施后新增扫码登录、恢复、退出与排错指南 |
| docs/maintenance/ | 登录落地后将可复用验证矩阵归入此处，并修复导航 |
| docs/archive/plans/ | 完成方案归档；先提取剩余待办 |
| docs/archive/records/ | 有日期的实现、自动验证、人工验收及以后独立的部署/发布证据 |

阶段记录须分开填写代码实施、自动验证、真实账号/桌面观察、host 验证、部署与发布；没有执行的项目写“未执行”。测试名称与数量、TRX 路径和源码基线放在当次记录中，不能在导航中反复复制。

## 10. 计划待办（历史快照）

- [x] P0：全新进程获取 key、观测 801 与未登录账号；此路径无需匿名初始化，真实 803 另行验证。
- [x] P1：Flurl 接入、eapi/weapi、登录状态和会话存储。
- [x] P2：登录页面与关闭/取消/恢复/退出交互。
- [x] P3：补齐专项测试、本地开发门禁和现行使用文档。
- [ ] 真实账号人工验证，并单独记录 host 与 Standalone 验证范围。
- [ ] 登录里程碑完成后，再安排 P4 播放最短路径。
- [ ] 正式发布时再执行发布门禁；当前不执行。

[qr-key]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/login_qr_key.js
[qr-create]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/login_qr_create.js
[qr-check]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/login_qr_check.js
[login-status]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/login_status.js
[logout]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/logout.js
[request]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/util/request.js
[crypto]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/util/crypto.js
[config]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/util/config.json
[anonymous]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/register_anonimous.js
[generate-config]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/generateConfig.js
[server]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/server.js
[song-url]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/module/song_url_v1.js
[upstream-doc]: https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced/blob/a8c781fd64faab17fedfd46e0615a2609307f163/public/docs/home.md
