# V5 / V6 文档分类与验收承接（2026-09-24）

> 范围仅为 `myavalonia-music-netease` 的 README 与 `docs`。承接工作区已有的[前轮整理](reorganization-20260924.md)，保留其未提交修改；本轮按文档用途继续分类，不变更功能或验收结论。

## 文件归位

| 原位置（相对 `docs`） | 新位置 | 分类依据 |
| --- | --- | --- |
| `roadmap/netease-v5-lightweight-interaction-and-ui-plan.md` | [V5 历史方案](../../plans/netease-v5-lightweight-interaction-and-ui-plan.md) | 主体已实现，旧交互差项由 V6 承接；原设计与当时状态作为历史保留 |
| `roadmap/netease-v6-drawer-and-interaction-change-plan.md` | [V6 历史方案](../../plans/netease-v6-drawer-and-interaction-change-plan.md) | 18 项范围已实施；当前行为由 reference 维护 |
| `roadmap/netease-v6-interaction-candidates-evaluation.md` | [V6 历史评估](../../plans/netease-v6-interaction-candidates-evaluation.md) | 候选决策已完成，保留原成本、依赖及取舍依据 |

前轮已归位的 V5 / V6 实施与复核继续放在各版本 `archive/records` 下；原始截图、TRX、JSON 和日志保持原位置与原字节。

## 当前文档分工

- `quick-start` 保留当前使用步骤；`reference` 保留实际行为、架构与开发参考。
- `roadmap` 保留未实施的能力候选，并明确封面取色是可选未实施项。
- `maintenance` 保留可复用验证矩阵，新增[V5 / V6 待验清单](../../../maintenance/netease-v5-v6-acceptance.md)，集中跟踪 D01–D05 及 V6 新增交互的实机验收。
- `archive/plans` 保存历史方案与评估，`archive/records` 保存带日期事实；归档不等于验收完成。

同步根 README、总导航、归档索引、验证 / 部署指南、维护约定及入站链接，重算三份迁移文档的出站相对路径。原设计正文、历史测量值和当次验证结论保留；迁移说明与当前状态入口单独补在文首。

## 验证与边界

检查结果：

- `Assert-MarkdownLinks` 通过：55 个 Markdown 文件的仓库内链接与标题锚点有效；16 个邻仓参考单列。
- 导航覆盖通过：55 个 Markdown 文件均可从根 README 经链接逐层到达。
- 三份迁移方案正文比对通过：除文首分类说明与相对链接重算外，设计正文保持一致。
- 整理前后 418 个原始证据文件的路径与 SHA256 一致。
- `git diff --check` 通过；本轮变更限于本插件 Markdown 文档，源码、测试、工具、依赖与其他插件未修改。

本轮只运行文档链接、锚点、导航可达性、迁移正文与原始证据摘要检查，以及 Git 空白和变更范围检查。完整业务检查已在此前实现复核中执行，文档分类不新增业务测试、实机验收或部署结果。
