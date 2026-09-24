# 网易云音乐插件文档整理（2026-09-24）

> 后续分类：同日的[V5 / V6 分类整理](classification-20260924.md)已将三份原方案 / 评估移入 `archive/plans`，待验事项由维护清单承接。下文保留前一轮整理时的目录决策和检查结果。

> 范围：`myavalonia-music-netease` 的 README 与项目文档。起点为干净工作树，HEAD `5edd91b`；本次不改变功能、依赖、插件版本或已记录的验收结论。

## 整理结果

- 精简[项目首页](../../../../README.md)，保留项目简介、当前状态和最短开发入口。
- 重整[文档总导航](../../../README.md)，按使用、开发、验证、部署和历史查询组织，统一列出当前状态及待验范围。
- 新增[验证指南](../../../maintenance/verification.md)与[文档维护约定](../../../maintenance/documentation.md)，明确纯文档检查、默认 V6 完整验证、专项映射和实机边界。
- 将单次实施 / 复核记录移出维护目录，为旧记录补后续状态入口，并完整收录 V5 / V6 归档导航。
- 修正播放指南仍将 V5 部署称为“最新”的描述；当前入口指向 2026-09-23 的 V6 部署记录。
- 在当前入口明确 V5 C01–C08 已由 V6 接续实现，D01–D05 性能与实机待验仍保留；不改写旧复核的表格和测量结论。

## 记录归位

| 原位置（相对 `docs`） | 新位置 |
| --- | --- |
| `maintenance/netease-v5-ui-interaction-implementation.md` | [V5 首轮实施](../netease-v5/ui-interaction-implementation-20260923.md) |
| `maintenance/netease-v5-completion-audit-20260923.md` | [V5 完成度复核](../netease-v5/completion-audit-20260923.md) |
| `maintenance/netease-v6-drawer-and-interaction-implementation.md` | [V6 实施与验证](../netease-v6/drawer-and-interaction-implementation-20260923.md) |

入站链接和迁移文件的出站相对路径同步调整。V5 / V6 方案仍有验收事项，继续保留在 `roadmap`；其可复用验证矩阵继续保留在 `maintenance`。归档实施记录不代表完整验收通过。

## 验证与边界

验证结果：

- `Assert-MarkdownLinks` 通过：53 个 Markdown 文件的仓库内链接与标题锚点有效；16 个邻仓参考单列。
- 导航覆盖检查通过：53 个 Markdown 文件均可从根 README 经链接逐层到达。
- 3 份迁移记录与 HEAD 原文比对通过：除新增的后续导航说明与相对链接重算外，正文一致。
- `git diff --check` 通过；变更范围核对仅包含 Markdown，源码、依赖及原始证据未改动。

本次没有运行完整业务测试、联网探针、Host 或部署 / 发布流程；原有业务验证仅引用各自带日期记录。原始 JSON、TRX、日志、截图、源码及依赖文件不在修改范围。
