# 历史计划与实施记录

> 归档整理日期：2026-09-23。这里保留当时的方案、观察结果和交付证据。
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

V1 最后一次部署见第 4 项，后续部署见下面的 V2 记录。各记录中的测试数量、摘要和未验证项只描述其对应操作；需要新增验证时另建记录，并更新维护矩阵。

| 日期 | V2 实施记录 | 证明范围 |
| --- | --- | --- |
| 2026-09-23 | [M1 搜索、单曲播放与 LibVLC 设置实现](records/netease-v2/m1-implementation-20260923.md) | 自动回归、真实 MP3 解码、视频目录复用、内置/精简开发产物；真实听感及 Host/Dock 待验收 |
| 2026-09-23 | [M1 公共 LibVLC 配置与开发部署](records/netease-v2/shared-libvlc-deployment-20260923.md) | 指定 Controls 部署、Common 路径配置、公共库解码及 Host 实际加载；标准清理与自动审批阻止的剩余项 |

| 日期 | V3 实施记录 | 证明范围 |
| --- | --- | --- |
| 2026-09-23 | [紧凑桌面界面、主题与轻量动效](records/netease-v3/ui-implementation-20260923.md) | 本地自动门禁、真实 View 截图、同款基础主题组合与有限动效回收；真实 Host/Dock、缩放和硬件帧时间待验收，无部署 |
| 2026-09-23 | [V3 复用 Common LibVLC 开发部署](records/netease-v3/common-deployment-20260923.md) | 无原生库构建、指定 Controls 整体替换、12 文件摘要一致，公共库与用户配置不变；未启动 Host |
