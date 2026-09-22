# 网易登录 P0：协议与 Flurl 最小验证

> 日期：2026-09-22；状态：首批自动验证与未登录联网探针已完成，授权后真实账号验证未执行。
> 实施起点：38d77ff6ccfea145448450c80e70a7779346f40a；上游固定提交：a8c781fd64faab17fedfd46e0615a2609307f163。
> 配套：[实施计划](../../../roadmap/netease-v1-flurl-login-plan.md)、[专用矩阵](../../../roadmap/netease-v1-login-verification.md)。

## 已实现

- 原生 eapi/weapi、固定端点请求组装、Flurl 命名客户端、类型化登录适配器。
- 不可变会话快照、响应 Cookie 合并、业务/HTTP 错误分类、完整读取超时和响应大小限制。
- 本地二维码生成；手动联网 LoginProbe；第三方许可原文随插件输出。

## 自动与联网证据

本机 SDK：10.0.302。默认单测完全离线；29 项通过、0 失败、0 跳过（含原模板 4 项），命令为 dotnet test tests/MusicNetEasePlugin.Tests -c Debug --no-restore。

密码学向量由固定上游原始 crypto.js 生成，文件 SHA-256 为 192556e34ed897e367be23349b633db245dd65751ac45a5c055f765bba62e462；4 组输入覆盖普通参数、中文/符号/补充平面字符与空值。发现并修正 System.Text.Json 对 emoji 的转义差异；向量保存在 Tests/Fixtures，不依赖运行期 Node。

手动运行 dotnet run --project tools/MusicNetEasePlugin.LoginProbe -c Debug --no-restore，得到：

```json
{"mode":"fresh-anonymous-free-qr-probe","keyCreated":true,"qrStatus":801,"unauthenticatedAccountRecognized":true,"historicalCookiesLoaded":false}
```

探针直接创建全新内存上下文，不读任何历史 Cookie/公钥/账号文件。一次 key、一次轮询、一次未登录账号检查成功，当前环境无需匿名初始化即可进入扫码等待。没有把二维码 key、Cookie 或原始账号正文保存为证据。

## 尚未完成

P1 会话编排/受保护存储、P2 界面、P3 最终门禁正在后续阶段实现。803 后账号确认、持久化后恢复及真实 Host 生命周期尚未实测；本记录不能代替这些结果。未部署、未发布，未执行 Windows CI 或发布门禁。
