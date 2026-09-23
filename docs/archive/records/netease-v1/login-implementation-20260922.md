# 网易扫码登录实施与开发验证记录（2026-09-22）

> 后续状态：V1–V4 已于 2026-09-23 根据用户确认完成手工验收，见[验收收口记录](../netease-v1-v4-acceptance-20260923.md)。本文结果和未验收项是当次历史快照，原始证据保持不变。

> 范围：P1–P3；P0 独立证据见 [P0 记录](p0-protocol-verification-20260922.md)。本记录所属 Git 提交固化实现、测试和当前文档，不代表已发布。

## 1. 基线与实施

起始代码为协议提交 `83911a0`；上游仍固定在 `a8c781fd64faab17fedfd46e0615a2609307f163`。
本轮添加登录协调器、当前用户保护存储、头像隔离、完整登录页面、Host/Standalone 共用组合入口和生命周期收口。
后续搜索/播放/歌词/歌单未实施。使用范围见[现行契约](../../../reference/netease-http-session.md)。

## 2. 自动验证证据

执行入口：`pwsh -NoProfile -File tools/verify-development.ps1`。

| 检查 | 本轮结果 |
| --- | --- |
| 环境 | Windows、本机 .NET SDK 10.0.302、PowerShell 7.6.5；net10.0 / C# 14 |
| locked restore | 全部 4 个项目通过 |
| Debug build -warnaserror | 0 警告、0 错误 |
| 自动测试 | 67 项通过，0 失败、0 跳过；包括真实 CurrentUser DPAPI 与 Headless + Skia |
| 门禁自测 | 14 项判定通过：完整报告、零测试、失败、跳过、取消、旧/缺/截断报告、统计不符、进程失败/超时、坏链接/锚点 |
| 文档检查 | 16 个本仓 Markdown，通过；5 个邻仓参考单列 |
| git diff --check | 通过 |

运行 ID：`20260922-081400-72413532`；测试开始/结束、系统信息及 Git 基线见本机生成的 verification.json。
基线为 `83911a0` 加本记录所属提交的工作树改动（workingTreeDirty=true）；提交后可按相同门禁重现。
证据目录：`TestResults/NetEaseLogin/20260922-081400-72413532/`，包含 `netease-login-development.trx`、`verification.json` 与 `login-page.png`。
生成物已按 Git 忽略规则保留在本机；页面图使用虚构 key，不含真实账号。已人工查看图像，900×820 下二维码、说明、操作按钮和状态完整可见。

验证中先修复了新会话 Cookie 错继承旧期限、畸形账号字段异常、操作前置检查与登记的竞态，以及取消后的本地清理问题。
UI 测试显式等待 Dispatcher 消费状态投影，避免把后台任务完成误当作控件更新完成。门禁的子进程文本统一 UTF-8。

## 3. 设计与回归审查

- A01：应用层只依赖网络、存储、时间端口；MainDocument 不引用 Flurl、文件或 Host 内部类型。账号与请求快照不使用静态全局状态。只在可替换边界定义接口。
- A02：中文注释覆盖编码差异、Cookie 更新、状态锁、代次、原子提交/补偿、UI 调度与同步释放原因。前置判断和新操作登记在同一锁内，网络/文件 await 不持有该锁。
- A03：Flurl.Http、QRCoder、DPAPI 包的集中版本、PackageReference、私有包声明和锁文件一致；DI/TimeProvider/Headless/Skia 按运行或测试用途引入。无 Node 运行时，开发向量生成器保留来源与哈希。
- A04：UI 区分内存登录、已保存、清理失败和远端退出未确认；头像不是账号成功条件。未显示播放等未实现功能。账号、Host、部署与发布不混为自动通过。
- 原 MainDocument 的消息/模板 Command 测试由四项登录页面行为测试接替：保留激活标题、已取消初始化不执行；新增多页面所有者取消、关闭不退出共享账号。模板 ApplyWorkbenchMessage 的身份、菜单与实现一起移除，命令说明明确标为历史参考。
- 资源审查：每个响应/流及时释放；View 拥有 Bitmap；Document 拥有头像任务/订阅/取消源；协调器跟踪所有操作及补偿；容器最终释放命名 Client。

## 4. 非自动验证边界

| 项目 | 本轮事实 |
| --- | --- |
| 无历史 Cookie 联网 | P0 已观测 key/801/未登录；详见单独记录 |
| 真实账号 M01–M07 | 未执行；需要本人手机扫码确认、恢复/退出与异常观察 |
| 实际生产 View | Headless + Skia 渲染和绑定自动验证；不等同于原生窗口人工点击 |
| 原生 Standalone 手动交互 | 未执行；可按快速开始启动同一页面 |
| 真实 Host M08 | 未执行；公开 SDK 契约、DI 组合与生命周期代码已接入 |
| 安装部署、打包发布 | 未执行 |
| Windows CI、发布门禁、AIFLOW | 未使用 |

Windows CurrentUser 凭据保护的本机测试属于本地单元测试，不是 Windows CI 或发布门禁。
当前不保证多进程共享数据目录；Standalone 与 Host 默认目录隔离。协议变化、真实账号权限和风险验证可能需要后续调整，不能由离线向量预测。
