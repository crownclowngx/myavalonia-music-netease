# V1–V4 验收收口与文档整理记录

> 记录日期：2026-09-23。源码核对基线：`5ab947037476fef7d779b9f16868fc8083f0f4b5`；整理前工作区干净。
> [文档导航](../../README.md) · [归档索引](../README.md) · [能力路线图](../../roadmap/netease-capability-roadmap.md)。

## 完成依据与范围

用户在本次任务中明确确认：“现在的 V+编号的部分都已经实现……且已经手工验收完毕”。据此将现有 V1–V4 统一登记为**已实现、手工验收完成**，并收口此前维护文档中的待验收状态。

| 实施编号 | 已完成范围 | 对应维护文档 |
| --- | --- | --- |
| V1 / M0 | 微信/App 登录、账号核验、受保护会话、恢复、退出与登录生命周期 | [登录矩阵](../../maintenance/netease-login-verification.md) |
| V2 / M1 | 搜索与单曲播放、LibVLC 内置/共享目录、账号设置 Tool 与 Host/Dock 接入 | [播放矩阵](../../maintenance/netease-v2-m1-playback-verification.md) |
| V3 | Document / Tool 紧凑布局、深浅主题、轻量动效与偏好 | [界面矩阵](../../maintenance/netease-v3-ui-verification.md) |
| V4 / M2 | 歌单、共享队列、四模式、定位、歌词、本地历史、静默恢复与页面连续播放 | [日常播放器矩阵](../../maintenance/netease-v4-m2-daily-player-verification.md) |

这是用户对现有实现范围的整体手工验收确认。本次没有重新操作账号、声卡或 Host，也没有新增逐场景操作日志、截图或硬件测量数据；不把整体确认扩写为具体样本、耗时或性能数值。旧自动报告中的 `hostVerified=false` 等字段仍描述该次自动运行，保持不变。

当前范围为 Windows x64；本次完成结论不扩展为 Linux/macOS 已适配、所有上游模块已接入或正式发布已完成。M3–M5 尚未实施，下一步在本轮整理完成后另行讨论。

## 文档归位与漂移修正

- V2 两份方案、V3 与 V4 方案移入 `archive/plans`，与已归档 V1 统一管理；方案保留历史设计和适用基线，当前契约由 `reference` 承接。
- 重整项目首页、唯一总导航和归档索引，统一 V1–V4 / M0–M2 完成状态，路线图仅保留后续候选为待办。
- 保留四份维护矩阵作为回归依据；手工验收结论引用本记录，历史实施和部署记录增加后续状态入口，原始结果不改写。
- 将 M1 维护矩阵的页面关闭规则同步为 V4 的“关闭页面继续播放，退出账号或关闭插件/进程停止”；区分单曲终态与队列续播。
- 修正默认检查入口为 V4、Standalone 布局说明、Host 停止与持久化顺序，以及上游能力清单仍称“其他模块未移植”的旧表述。
- 同步迁移文档、本仓源码与邻仓参考的相对链接。

## 核对与验证

对照插件服务注册、生命周期、Document 关闭处理、Standalone 窗口、HTTP 适配、依赖配置及 `verify-development.ps1` / 场景映射检查现行说明。本次仅修改 Markdown，不改实现、测试、构建脚本、历史证据或部署目录。

文档检查命令：

```powershell
. ./tools/DevelopmentChecks.ps1
Assert-MarkdownLinks (Get-Location).Path
git diff --check
```

验证结果：37 个 Markdown 文件的本仓链接与锚点检查通过，16 个邻仓参考按既有规则单列；`git diff --check` 通过。另核对 5 份归档方案与 10 份历史记录：除新增状态入口和迁移链接外，正文及链接实际目标保持原样；原始资产未修改。变更范围检查确认仅涉及 Markdown。

本次未重复运行构建、业务测试、联网探针或部署。
