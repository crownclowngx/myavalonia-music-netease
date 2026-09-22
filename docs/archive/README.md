# 登录阶段历史计划与记录

> 归档整理日期：2026-09-22。这里保留当时的方案、观察结果和交付证据。
> 当前入口：[文档总导航](../README.md) · [能力路线图](../roadmap/netease-capability-roadmap.md) · [HTTP 与会话契约](../reference/netease-http-session.md)。

## 归档计划与剩余事项

| 文档 | 归档原因 | 当前承接位置 |
| --- | --- | --- |
| [V1：Flurl 接入与扫码登录计划](plans/netease-v1-flurl-login-plan.md) | 登录已有实现，用户确认可用；原 P4–P5 已展开为能力路线图 | 后续功能见[能力路线图](../roadmap/netease-capability-roadmap.md)，实际协议见[当前契约](../reference/netease-http-session.md)，验证覆盖与剩余人工项见[维护矩阵](../maintenance/netease-login-verification.md) |

原计划中的阶段标识、授权背景和待办是历史快照，不作为新的执行指令。归档不表示尚未记录的 Host、重启恢复、远端退出或正式发布验证已经完成。

## 实施与验证记录

以下记录均为 2026-09-22，按该日实施先后排列：

| 顺序 | 记录 | 证明范围 |
| --- | --- | --- |
| 1 | [P0 协议与 Flurl 最小验证](records/netease-v1/p0-protocol-verification-20260922.md) | 原始协议向量、无历史 Cookie 的 key/801/未登录联网观察；不含真实手机授权 |
| 2 | [App 扫码登录实施与开发验证](records/netease-v1/login-implementation-20260922.md) | 登录编排、会话保护、界面与本地自动验证；真实账号和 Host 边界按该次记录 |
| 3 | [早期 App 版开发部署](records/netease-v1/development-deploy-20260922.md) | 微信入口加入前的插件开发部署和文件核对，保留为历史 |
| 4 | [微信默认登录实现、实测与部署](records/netease-v1/wechat-login-implementation-20260922.md) | 微信授权、网易回调、账号核验、自动验证以及后续开发部署；探针未持久保存真实会话 |

最近一次有完整记录的部署见第 4 项。各记录中的测试数量、摘要和未验证项只描述其对应操作；需要新增验证时另建记录，并更新维护矩阵。
