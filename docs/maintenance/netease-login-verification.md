# 网易云音乐扫码登录专用开发验证

> 用途：维护[当前登录实现](../reference/netease-http-session.md)的测试矩阵、本地开发门禁和人工验证范围；原方案见[归档计划](../archive/plans/netease-v1-flurl-login-plan.md)。
> 状态：当前维护矩阵；核对日期：2026-09-22。原场景映射见第 10 节，微信矩阵见第 12 节；真实微信扫码已验证，其他人工项以[微信专项记录](../archive/records/netease-v1/wechat-login-implementation-20260922.md)的边界为准。
> 约束：SOLID 优先、模式朴素、详细中文注释；不使用 AIFLOW、Windows CI、seal、发布 Windows Smoke、发布覆盖率或发布重复性门禁。
> 后续扩展：[V2 / M1 验证矩阵](netease-v2-m1-playback-verification.md)规划音乐能力测试与同一开发门禁入口的扩展；本页继续作为登录回归依据，当前脚本行为不因方案编写而改变。

## 1. 测试分层与夹具原则

| 层次 | 验证内容 | 运行方式 |
| --- | --- | --- |
| 协议单测 | eapi/weapi 的序列化、参数编码、加密及响应解释 | 固定输入与独立上游向量，不联网 |
| Flurl 契约测试 | 最终 URL、方法、头、表单、Cookie、HTTP/业务错误与取消 | Flurl.Http.Testing.HttpTest；不只 mock Auth API |
| 登录用例测试 | 轮询、状态、代次、账号验证及会话提交 | 受控 API 替身 + TimeProvider/异步信号 |
| 存储测试 | 加密读写、原子提交、清除失败、坏文件和恢复 | 测试拥有的真实隔离目录；小型存储故障注入 |
| UI 模型与组合测试 | 状态投影、命令、订阅、服务注册、生命周期 | 现有 Tests 项目；需要验证实际控件行为时再引入最小 Headless 夹具 |
| 人工联网与桌面观察 | 手机扫码、账号恢复、头像、桌面关闭与重开 | 手动使用开发产物；独立于默认单测和门禁 |
| 文档与设计审查 | 状态、链接、接口映射、中文注释、SOLID 和资源所有权 | 本地文档检查与针对性代码审查 |

现有测试项目是 [MusicNetEasePlugin.Tests](../../tests/MusicNetEasePlugin.Tests/MusicNetEasePlugin.Tests.csproj)。优先在此按 Authentication、Protocol、Http、Session、Presentation 分组，不为每个边界新增一个项目。

规则：

- 默认测试完全离线，不访问网易、不读取真实 Cookie、不扫描个人数据目录，不向外部服务发送验证码或消息。
- HttpTest 同步建立并在同一测试作用域内释放；未预期请求应失败，不能落到真实网络。涉及原始响应头或二进制解密而 HttpTest 无法充分模拟时，使用最小受控 handler/本机夹具补齐，仍经过生产 Flurl 适配器。
- 随机源与时间只在测试中可控；不能把测试密钥或固定随机数带入生产配置。向量来自固定上游提交且注明生成方式。
- 不以“加密后自己解密成功”作为唯一密码学正确性依据；必须与上游固定输入/输出交叉验证。
- 并发测试用屏障、TaskCompletionSource 和可控时间推进，不靠固定 sleep 碰运气；每个等待都有超时保护。
- 文件测试只清理自己创建的目录；每个测试隔离 ClientCache、会话和设备状态，释放拥有的网络响应、二维码位图和取消资源。
- 测试文件名和方法名在实施时落定；本矩阵编号映射到真实测试，保留未覆盖项，不能只增加同名空测试。

## 2. P：协议与请求构造

| 编号 | 场景 | 关键断言 |
| --- | --- | --- |
| P01 | eapi 固定路径与 JSON 向量 | 摘要、明文拼接、AES 模式/填充、输出大小写和上游向量一致 |
| P02 | weapi 固定随机输入向量 | 两层 AES、密钥反转与 RSA 原始运算语义一致；不误用默认 OAEP/PKCS1 加密 |
| P03 | 中文、特殊字符、空值、布尔值、长整数和属性顺序 | 请求 JSON 字节符合协议；表单只编码一次，响应 ID 不丢失精度 |
| P04 | QR key 与 check 的逻辑路径转换 | 使用配置中的 eapi 主机、最终路径、POST、type=3；key 仅出现在协议所需字段 |
| P05 | 登录状态请求 | 使用 weapi 路径、CSRF 和当前候选会话；不能把外壳 /login/status 发往网易 |
| P06 | 未登录/已登录 Cookie 与设备字段 | 设备标识稳定；游客、账号和协议字段按来源区分，避免请求间串用 |
| P07 | 明文 JSON、开启的加密响应、压缩响应、畸形密文 | 按实际协议配置解码；错误可诊断，不把乱码或空正文当正常账号 |
| P08 | 二维码生成与 key 中的保留字符 | 生成默认 pc 链接，内容正确编码，本地生成图片且零额外 HTTP |
| P09 | 二维码尺寸/空 key/生成失败与旧图片替换 | 非法输入有明确错误；位图资源释放；生成失败不开始无意义轮询 |
| P10 | 独立进程/全新目录，无匿名 Cookie 或公钥缓存 | 记录 QR 实际依赖；P0 若证实需要匿名/xeapi，补齐公钥获取、缓存失效与匿名初始化测试后才能完成 |
| P11 | 模块返回包装与直连响应不同 | C# 依据网易真实响应建模；不要求原生响应具备 Enhanced 人工包出的 body/data/cookie 层 |
| P12 | 未知字段、缺失账号字段、code 为数字或字符串 | 忽略不影响契约的新增字段；必要字段缺失失败；未知业务码不映射为成功 |

P10 是显式验证项；不能使用已有登录环境绕过。xeapi 若不在第一阶段运行路径，不提前为全部上游协议铺设测试；进入播放或匿名依赖后再补齐 X25519/HMAC/AES-GCM、密钥更新和会话响应专项。

## 3. H：Flurl、错误和网络边界

| 编号 | 场景 | 关键断言 |
| --- | --- | --- |
| H01 | 多次请求、多个适配器、容器释放 | 复用所属命名 Client，不创建每请求客户端，不污染全局 Flurl 配置，资源由容器释放 |
| H02 | HTTP 200 + 800/801/802/803 | 正确解释业务状态，HTTP 成功不直接判定账号已登录 |
| H03 | HTTP 4xx/5xx、限流及正文业务失败 | HTTP 状态与业务码分别保存；301 正文不误认作 HTTP 重定向；响应为空也有受控错误 |
| H04 | 请求超时、用户取消、连接失败 | 分类不同，取消不提示“密码错误”或重试；令牌贯穿真实 Flurl 调用 |
| H05 | 轮询有限退避、Retry-After、重试预算耗尽 | 无并发重叠、无无限重试；关键创建/提交/退出不被底层通用 POST 重放 |
| H06 | 会话失败/限流/需要验证/460 等异常 | 不通过高频重登解决；保留业务码与可操作提示，不误报已退出或已登录 |
| H07 | 多个 Set-Cookie、同名更新、失效及删除 | 正确处理 Cookie 属性，不按逗号拆分 Expires；候选与正式会话隔离 |
| H08 | 退出后旧请求返回 Set-Cookie | 旧代次不能经共享 CookieJar 回写账号，也不能落盘 |
| H09 | 重定向、不同域名、头像下载 | 默认 API 重定向政策生效；头像不带账号 Cookie/CSRF，失败不改变账号状态 |
| H10 | 日志、异常 Message/ToString/InnerException | Cookie、CSRF、二维码 key/URL、原始正文均不泄漏；保留接口、状态、耗时 |
| H11 | 同一 URL 的轮询连续返回不同状态 | 无登录缓存，801 能推进到 802/803；不依赖外壳时间戳绕过缓存 |
| H12 | noCookie 外壳参数 | 不把关闭 Enhanced Set-Cookie 输出误写成直连请求清空凭据；以实际适配规则断言 |

Flurl 替身覆盖网络契约，Auth API 替身覆盖状态编排，不能用后一种替代前一种。开发日志和测试结果使用虚构凭据；人工证据只保存脱敏状态。

## 4. L：登录状态、并发与取消

| 编号 | 场景 | 关键断言 |
| --- | --- | --- |
| L01 | 801 → 802 → 803 → 账号检查有效 | 状态顺序准确；轮询停止；验证完成后只提交一次登录结果 |
| L02 | 803 缺失关键凭据、账号/profile 为空或账号验证失败 | 不发布成功，未保存伪会话；可重新扫码 |
| L03 | 连续等待直至 800 或本地预算结束 | 停止轮询；区分服务端过期与本地等待结束；不会自动无限生成二维码 |
| L04 | 创建中、轮询中、等待间隔、验证中取消 | 及时终止；不再发下一次请求；无迟到 UI 或磁盘提交 |
| L05 | 连点登录/刷新、多个 Document 同时发起 | 同一插件最多一个活动尝试；替换有明确规则，旧代次结果失效 |
| L06 | 旧二维码结果晚于新二维码返回 | 新状态与会话不被覆盖；包括旧 803 与旧失败结果 |
| L07 | 授权后保存失败 | 有效内存登录与持久化失败分开显示，重试保存不要求重复授权 |
| L08 | 退出与验证/保存竞争 | 退出使旧代次失效；磁盘提交前再次检查；不会退出后重登 |
| L09 | 退出后网络失败/清除失败 | 远端和本地结果分别表达；本地未清理时不能声称重启后绝不恢复 |
| L10 | 未知业务码、账号检查需要二次验证 | 终止当前自动流程，受控提示；不继续消费异常正文或快速重试 |
| L11 | 两个插件服务容器/测试实例 | 状态、设备上下文、Cookie 和尝试代次不串用 |
| L12 | 当前流程重复完成/重复取消/重复退出 | 结果幂等、资源只释放一次、无重复通知或未观察任务异常 |

## 5. S：会话存储与恢复

| 编号 | 场景 | 关键断言 |
| --- | --- | --- |
| S01 | 首次无数据文件 | 未登录可用，生成稳定设备上下文；未开始登录时不强制远端注册，登录初始化依赖按 P10 验证结论执行 |
| S02 | 保存后重建全部服务并读取 | 恢复所需信息完整，持久化载荷受当前用户保护；不保存密码、验证码、二维码 key |
| S03 | 有效 Cookie 启动验证 | 验证通过才显示已登录，不调用 login/refresh 假装续期 |
| S04 | 会话存在但网络不可用 | 保留凭据，显示待验证/网络异常；不静默清空并要求扫码 |
| S05 | 已失效或被远端撤销 | 停止使用失效会话，进入重新扫码流程；不循环刷新二维码 Cookie |
| S06 | 损坏、解密失败、未知格式版本 | 不崩溃、不误报成功；提示重新登录/清理，保留明确故障分类 |
| S07 | 临时写入失败、替换失败、目录只读 | 原文件保持可读；不半写；内存登录和保存结果分别报告 |
| S08 | 本地退出、删除拒绝、退出期间旧写入 | 正常退出清空当前凭据；失败不报告完整成功；旧代次无法重新创建有效会话 |
| S09 | 服务/Document 释放后读取和回调 | 已结束服务不提交状态；订阅移除，取消资源释放，任务异常被观察 |
| S10 | 数据目录与并行测试隔离 | 不写源码/Controls/真实用户目录；清理限定在测试拥有路径 |
| S11 | 平台凭据保护不可用或用户上下文改变 | 明确报告无法恢复；不降级成明文保存；保护实现有独立真实平台测试 |

S11 中平台相关测试在支持的本地开发环境执行；这不构成 Windows CI 或发布门禁。平台测试未运行时单独列出，不以替身通过冒充真实凭据保护验收。

## 6. U/A：界面、组合与设计

| 编号 | 场景 | 关键断言 |
| --- | --- | --- |
| U01 | 各登录状态及按钮 | 用户看到二维码/等待确认/过期/失败/已登录；按钮只允许当前有效操作 |
| U02 | 头像不可达、解码失败、慢响应 | 使用占位图；不阻塞登录和退出；无跨会话旧头像覆盖 |
| U03 | 多 Document、关闭发起页面、其他页面仍开 | 发起页面关闭取消其尝试；已提交账号仍共享；关闭不等于退出 |
| U04 | Standalone 关闭/Host 生命周期取消 | 取消并观察在途操作，释放订阅和图片；不访问已释放视图 |
| U05 | 同一 DI 组合入口 | Plugin 与 Standalone 复用同一服务、用例和 View；不各写一套网络逻辑 |
| U06 | 既有 MainDocument/命令测试调整 | 对保留行为继续回归；模板示例若替换，记录测试去向与新业务断言，不能仅删测试消除失败 |
| A01 | 依赖方向与资源所有权审查 | ViewModel 不依赖 Flurl/文件/host 内部类型；会话不挂在静态全局；具体类不强制同名接口 |
| A02 | 中文注释与接口契约审查 | 说明 why、编码、取消、并发、提交和失败语义；无与实际实现矛盾的注释 |
| A03 | 协议和包依赖审查 | 新依赖有明确用途，集中版本/私有包声明/lock 同步，不引入 Node 运行时 |
| A04 | 错误与事实审查 | 未支持功能不显示可用；没有把账号验证、持久化、部署和发布混为一个成功状态 |

架构审查主要依赖依赖图和具体实现，不堆积仅匹配源码字符串的脆弱测试。适合自动验证的注册边界与实例隔离用行为测试覆盖。

## 7. M：人工验证清单

2026-09-22 通过显式微信探针完成 M01 的微信授权及账号核验部分；探针未保存会话，未完成 M04/M06。原网易 App 真实 803 及 M02–M08 仍待人工执行。不把凭据、二维码原图或账号完整响应提交到仓库。

| 编号 | 操作 | 观察与留证 |
| --- | --- | --- |
| M01 | 全新数据目录启动，获取二维码，手机扫码并确认 | 等待扫码、等待确认、账号验证、已登录顺序；记录是否需要匿名初始化 |
| M02 | 不扫码，等待过期，手动重取 | 无无穷轮询；新二维码有效；旧二维码迟到结果不起作用 |
| M03 | 扫码中关闭页面/应用，再重新打开 | 无后台登录残留；取消状态准确 |
| M04 | 登录成功后关闭并重开 | 已保存会话检查后恢复；不要求每次重新扫码 |
| M05 | 断网打开已有会话；恢复网络重试 | 网络失败不伪装成账号失效，不丢失可恢复会话 |
| M06 | 主动退出、重开；有条件时远端撤销登录再重开 | 本地退出不恢复旧账号；远端失效有明确重新扫码提示 |
| M07 | 头像慢/失败、保存目录不可写、退出清理失败 | 登录与图片、内存与持久化、远端与本地退出结果分别显示 |
| M08 | 真实 Host 内打开/关闭/重开页面及 Host 退出 | 使用公开 SDK 生命周期；没有后台轮询或已释放视图回调 |

M08 属于手动开发联调观察；文件已按用户指定路径进行开发部署，不运行 Windows CI、发布 Smoke 或其他发布门禁。部署通过不代表真实 Host 已验收；尚无 Host 联调证据时记录“未验证”。

## 8. 本地开发门禁入口与判定

已提供薄的 [verify-development.ps1](../../tools/verify-development.ps1) 入口，复用以下标准命令，并检查每个退出码及 TRX 完整性。入口只做本仓开发验证，不调 host 发布工具。

首选在本仓根目录执行 `pwsh -NoProfile -File tools/verify-development.ps1`。其核心命令如下：

```powershell
dotnet restore MusicNetEasePlugin.slnx --locked-mode
dotnet build MusicNetEasePlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/MusicNetEasePlugin.Tests/MusicNetEasePlugin.Tests.csproj -c Debug --no-build --no-restore --logger "trx;LogFileName=netease-login-development.trx" --results-directory TestResults/NetEaseLogin
git diff --check
```

新增包时先正常 restore 更新并审查 lock，再执行 locked-mode 验证。每次测试使用独立结果目录或在受控范围确保无旧报告混入；上例为人工单次命令，脚本应附带运行身份。

| 编号 | 检查 | 通过条件 |
| --- | --- | --- |
| G01 | Debug 构建 | 全解决方案成功，无新增警告，依赖与 lock 一致 |
| G02 | 自动测试 | 本轮 TRX 完整，实际发现并执行测试，失败为零；登录关键场景无跳过，不能只有退出码 |
| G03 | 场景映射 | P/H/L/S/U 各适用编号映射到实际用例；条件项说明适用性，未覆盖项明确保留 |
| G04 | 门禁可靠性 | 构建失败、零发现、测试取消/超时、缺 TRX、旧/截断报告不得通过；只为这些有意义的编排分支写门禁自测 |
| G05 | 文档 | 所有本仓 Markdown 链接/新增标题锚点有效；计划状态、导航、接口映射与实现同步 |
| G06 | 设计审查 | A01–A04 有实际结论；无跨层 HTTP、全局 Cookie、遗漏取消和危险诊断输出 |
| G07 | 验证记录 | 记录 revision、命令、运行环境与 TRX 路径；自动测试、真实账号、Host、部署与发布分别填写 |

不增加开发覆盖率硬阈值或重复构建来模拟发布要求。测试通过后，仅当代码再次改变、出现失败或有具体未解问题时补跑相关检查。

## 9. 文档专用检查与本次状态

仅修改文档时：

1. 检查本仓 Markdown 文件链接与新增标题锚点；邻仓参考作为可选外部材料单列。
2. 检查能力索引与固定上游模块清单对应，不遗漏、不重复，不把模块数当可用率。
3. 检查现行登录实现、后续计划和验证状态的表述一致。
4. 运行 `git diff --check`，确认修改仅涉及预期 Markdown。
5. 不执行应用构建、单测、账号登录、安装部署、Windows CI 或发布门禁。

本文件已从 roadmap 迁至 maintenance；当次执行结果保存于 archive/records。下表区分自动覆盖、审查与人工待验证，不用场景标签数量冒充实际验证。

## 10. 当前自动覆盖与剩余边界

| 场景 | 实际入口与关键断言 | 状态/剩余边界 |
| --- | --- | --- |
| P01–P03 | [ProtocolTests](../../tests/MusicNetEasePlugin.Tests/ProtocolTests.cs)：固定上游向量、Unicode/字面反斜杠、长 ID | 自动；原始上游生成器 SHA 固定 |
| P04–P06、P11–P12 | [HttpTests](../../tests/MusicNetEasePlugin.Tests/HttpTests.cs) 与 ProtocolTests：Flurl 最终表单/端点/设备，账号与 code | 自动；不假设 Node 包装正文 |
| P07 | ProtocolTests：明文、密文、gzip、畸形响应、解压上限 | 自动 |
| P08–P09 | ProtocolTests、[UiCompositionTests](../../tests/MusicNetEasePlugin.Tests/UiCompositionTests.cs)：本地 QR、非法 key、生产 View 重挂与图片清理 | 自动；实际扫码解析留给 M01 |
| P10 | [P0 联网记录](../archive/records/netease-v1/p0-protocol-verification-20260922.md) | 全新进程 key/801/未登录；未证明真实 803 |
| H01、L11、U05 | UiCompositionTests：重复解析复用、两个容器隔离、同步释放 Client | 自动；真实 Host 对象图待 M08 |
| H02–H04、H06–H07、H11 | HttpTests：800–803、HTTP/业务错误、超时、预取消与在途 handler 取消、Cookie 属性和期限替换 | 自动 |
| H05、L10 | [LoginFailureTests](../../tests/MusicNetEasePlugin.Tests/LoginFailureTests.cs)：定时器登记后推进时间、重试上限、Retry-After、异常码停止 | 自动；不等待真实分钟 |
| H08、L04–L06、L08、S08 | [LoginCoordinatorTests](../../tests/MusicNetEasePlugin.Tests/LoginCoordinatorTests.cs)、LoginFailureTests：迟到响应、验证与退出竞争、提交后取消补偿 | 自动；断电与跨进程不在保证范围 |
| H09、U02 | HttpTests、UiCompositionTests：头像域名/无凭据/重定向拒绝，坏图片占位 | 自动与代码审查；真实 CDN 慢响应列入 M07 |
| H10 | HttpTests、安全 ToString、传输/存储异常审查 | 不记录请求日志、不透传底层异常；当前未加入耗时遥测 |
| H12 | 编码器、外壳语义审查与 HttpTests | noCookie 不属于原生参数，不清空候选会话 |
| L01–L03、L07、L09、L12 | LoginCoordinatorTests：账号验证、缺失 Cookie、两类过期、保存重试、退出失败、重复关闭/观察者异常 | 自动 |
| S01–S02、S06–S08、S10–S11 | [SessionStoreTests](../../tests/MusicNetEasePlugin.Tests/SessionStoreTests.cs)：真实隔离文件、坏文件/版本、取消/目标冲突、真实 DPAPI | 自动；跨 Windows 用户观察另行人工验证 |
| S03–S05 | LoginCoordinatorTests：恢复合并、网络失败保留文件、失效不发布、不刷新 QR Cookie | 自动 |
| S09、U03–U04 | [MainDocumentTests](../../tests/MusicNetEasePlugin.Tests/MainDocumentTests.cs)、LoginCoordinatorTests、UiCompositionTests | 所有者取消、关闭、共享账号保留与视图重挂自动；Host 退出待 M08 |
| U01、U06 | MainDocumentTests、UiCompositionTests：标题/取消回归、按钮、QR/账号切换 | 自动；模板替换说明见阶段记录 |
| A01–A04 | [当前契约](../reference/netease-http-session.md)与阶段记录 | 代码审查，不用源码字符串测试代替 |
| G01–G07 | verify-development.ps1、自测、本矩阵、文档检查及日期记录 | 自动校验与审查结合；结论见当轮记录 |
| M01–M08 | 第 7 节人工清单及微信专项记录 | M01 微信授权与账号核验已通过；其余边界见第 7 节 |

Trait 便于检索，不能替代实际断言。默认自动测试没有真实账号，不扫描个人数据目录。

## 11. 开发门禁本身的保证

[DevelopmentChecks.ps1](../../tools/DevelopmentChecks.ps1)检查进程退出码与总耗时；restore/build 每步最多 300 秒，test 最多 180 秒。
超时终止本次进程树并失败。每轮新建 UTC 时间 + 随机 ID 的 TestResults 子目录，不复用旧报告、不删除历史结果。

TRX 必须在本轮目录、具有本轮时间与完整 XML、正常完成、total/executed/passed 相等且大于 0，逐条全部 Passed。
失败、跳过、取消、旧报告、缺报告、截断或统计不一致均失败。[门禁自测](../../tools/test-development-gate.ps1)实际注入这些输入，并验证进程非零/超时和坏文档链接/锚点。

文档检查覆盖根 README、第三方声明及 docs 下 Markdown 的本地文件链接、引用式链接与标题锚点；邻仓参考单列，不访问远程链接。
仅文档修改可执行：

```powershell
. ./tools/DevelopmentChecks.ps1
Assert-MarkdownLinks (Get-Location).Path
git diff --check
```

每轮生成 TRX、Headless 页面图（虚构 QR）和 verification.json；记录基线 revision、工作树状态、环境与测试数。
自动门禁不登录真实账号，因此其 verification.json 中 realAccountVerified/hostVerified/releaseGateExecuted 为 false；这表示该次门禁未执行对应活动。独立手机扫码证据另外记录，不用单元测试结果替代。

## 12. W：微信登录专项矩阵

网络层由 [WeChatLoginTests](../../tests/MusicNetEasePlugin.Tests/WeChatLoginTests.cs)执行真实 Flurl 编排与解析器，用固定响应隔离外网；共享状态和界面测试继续运行生产协调器。

| 编号 | 关键断言 | 实际入口 |
| --- | --- | --- |
| W01 | 默认微信，网易 App 作为独立备用，页面文案与图片匹配 | WeChatLoginTests、MainDocumentTests |
| W02 | 408/404/403/402 分别等待、确认、取消、过期；已扫描时 408 不回退 | WeChatLoginTests、LoginCoordinatorTests 手机拒绝用例 |
| W03 | 原始 state 正确编码；405 后经网易回调取得 MUSIC_U，同源重定向受限 | WeChatLoginTests |
| W04 | 不继承旧账号 Cookie，网易 Cookie 不发往微信，尝试之间隔离 | WeChatLoginTests |
| W05 | 微信确认不代表账号有效；网易账号核验前不发布、不保存 | WeChatLoginTests 真实 Provider + 协调器用例 |
| W06 | 入口/图片/回调限定域名与 HTTPS；拒绝重复 state 和任意脚本 | WeChatLoginTests |
| W07 | 缺 code、未知状态、无会话、绑定要求、回调超时均终止；一次性 code 不重放 | WeChatLoginTests |
| W08 | 取消贯穿正在等待的长轮询；释放后不再请求且丢弃图片 | WeChatLoginTests 真实取消 handler |
| W09 | 微信切换 App 先取消旧尝试；无旧图片或状态覆盖 | MainDocumentTests；共享 L04–L08 迟到响应/提交补偿用例 |

真实微信探针已观察 WaitingForScan → WaitingForConfirmation → Verifying → SignedIn，且用户确认手机显示成功。
仅记录脱敏布尔结果；长期可用性、账号绑定/风控真实分支、Host 与持久恢复不由本次成功推断。
