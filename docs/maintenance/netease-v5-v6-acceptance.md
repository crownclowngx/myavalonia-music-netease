# V5 / V6 性能与实机待验清单

> 更新：2026-09-24。V5 主体已实现，历史 C01–C08 交互差项已由 V6 承接；V6-01～18 已实现并通过本地开发验证。两者的完整性能、实机和用户验收仍未收口。
> 本文持续维护尚待验证的事项；单次执行结果保存到 `archive/records`。原方案已按用途归档，归档不改变验收状态。

## 已有证据与边界

| 范围 | 已有结论 | 依据 |
| --- | --- | --- |
| V5 主体与后续交互补齐 | 固定播放条、统一歌曲行、保留队列播放、图片预算等已接入；旧 C01–C08 由 V6 补齐 | [V5 首轮实施](../archive/records/netease-v5/ui-interaction-implementation-20260923.md)、[V6 承接说明](../archive/records/netease-v6/drawer-and-interaction-implementation-20260923.md#5-实机验收及历史问题) |
| V6 全量交互与自动验证 | 18 项实现；已有全量测试、场景映射、Headless 布局、手势及资源证据 | [V6 实施记录](../archive/records/netease-v6/drawer-and-interaction-implementation-20260923.md)、[原始自动报告](../archive/records/netease-v6/gate-20260923/verification.json) |
| 实际 Host 启动 | 指定 Controls 部署及有限启动复测通过 | [V6 部署记录](../archive/records/netease-v6/deployment-20260923.md) |
| V1–V4 验收 | 用户已确认其手工验收；不自动覆盖 V5 / V6 | [既有验收记录](../archive/records/netease-v1-v4-acceptance-20260923.md) |

Headless / Skia 截图不替代真实 Host 操作，离线 LibVLC 解码不替代真实声卡出声，启动通过不替代 Dock 与多窗口验收。执行方法见[验证指南](verification.md)，自动场景继续维护在 [V5 矩阵](netease-v5-ui-interaction-verification-plan.md)与 [V6 矩阵](netease-v6-drawer-and-interaction-verification-plan.md)。

## 尚待关闭的项目

以下沿用 V5 历史编号，保留每项的证据缺口；V6 新增交互并入对应的性能、Host、输入与业务验收，不重复计算完成度。

| 编号 | 当前状态 | 待验证内容与关闭条件 |
| --- | --- | --- |
| D01：CPU 成本 | 历史观测超建议值，真实 Host 待测 | 历史同条件 Headless 暂停可见 CPU 中位数 4.60% → 6.65%，增量约 2.05 个百分点，高于 +1 建议。该场景每 200 ms 强制布局 / 绘制，不等同 Host 静止空闲。补真实 Host 同音频、同数据的可见 / 播放 / 隐藏对照；记录优化结果或明确的取舍接受依据 |
| D02：完整性能 | 有限测量，覆盖不足 | 补首次可见反馈、连续滚动帧耗时 p95、峰值与长时间内存 / 分配趋势；包含大队列、长歌词、多 Document、20 轮开关与重挂。记录机器、DPI、主题、音频、版本和测量方法，不能以缓存有界代替性能达标；无法采集的 GPU / 帧指标仍写未测 |
| D03：Host 与生命周期 | 启动通过，完整交互待验 | 实际 Dock 拖离 / 回停、浮窗、多 Document、关闭最后页面继续播放、重新打开及重启静默恢复。加入抽屉连续调整、跨尺寸切换、队列跨页面拖动 / 撤销，确认播放与共享状态一致 |
| D04：输入、显示与可访问性 | 自动布局已有，实机待验 | 100% / 125% / 150% / 200% DPI、长中文、深浅主题、Document 与 Tool 尺寸；物理中文输入法、Ctrl+F、仅键盘任务、菜单 / 抽屉焦点归还、读屏、对比度及核心命中区域。覆盖歌词定位、抽屉宽度与密度切换 |
| D05：真实业务与用户体验 | 待真实业务与用户确认 | 真实账号搜索→播放→下一首→歌单返回→恢复播放，核验真实声卡出声、暂停跳句、队列拖动 / 撤销、恢复提示和故障恢复；保存实际结果与用户对易用性、视觉的验收结论 |

来源：[V5 历史复核](../archive/records/netease-v5/completion-audit-20260923.md#4-性能与正式验收仍未收口)、[V5 资源观测](../archive/records/netease-v5/ui-interaction-implementation-20260923.md#5-同条件资源观测)、[V6 实机边界](../archive/records/netease-v6/drawer-and-interaction-implementation-20260923.md#5-实机验收及历史问题)。这些历史数值不代表本次重新测量，也不证明当前 V6 的实机成本。

## 可选增强与范围

- V5 E 阶段的歌词居中、队列拖动和单步撤销已由 V6 实现。
- 封面一次性取色仍为可选未实施项，归入[后续候选](../roadmap/netease-capability-roadmap.md#现有界面的可选增强)，不混入 D01–D05 必需验收。
- 设置入口、播放模式呈现、图片尺寸等已记录取舍以[当前界面契约](../reference/netease-desktop-ui.md)为准；V5 原页面布局已由 V6 抽屉设计承接。
- M3–M5、新平台适配、正式 ZIP / 签名 / 发布验收属于各自后续范围。本地开发检查继续按既有约定执行。

## 如何更新结果

1. 记录本轮代码提交 / 工作区状态、Host 与插件构建身份、机器和测试环境；按适用矩阵执行并区分自动、实机与用户结果。
2. 在 `archive/records/netease-v6` 新建带日期记录，保存对应原始证据；不覆盖旧报告、截图、TRX 或测量值。
3. 按编号更新上表，填写结果、剩余缺口及证据链接；未测或未达标项保持开放。
4. 同步[文档总导航](../README.md#当前状态与待验范围)。只有必需验收各有结论且完成用户确认后，才将整体状态改为验收完成。
