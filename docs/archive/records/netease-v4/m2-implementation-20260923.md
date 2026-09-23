# V4 / M2 实施与开发验证记录（2026-09-23）

> 实施依据：[V4 方案](../../../roadmap/netease-v4-m2-daily-player-plan.md)与[专用矩阵](../../../maintenance/netease-v4-m2-daily-player-verification.md)。按阶段独立提交；本记录随当日工作追加。
> 仅本地开发验证，不使用 AIFLOW、Windows CI、发布门禁或部署。

## V4.0：接口与定位技术基线

方案基线提交 `e7869ce`，业务起点 `afb3159`。新增固定只读请求描述与协议测试、显式 `--daily-player` 探针、原生定位/指定起点能力及独立分段 PCM 测试。页面尚未接入 seek，其他 M2 功能仍待实施。

固定上游 request/config 确认默认加密为 eapi，用户歌单显式 weapi；真实只读探针验证五个新端点均返回 200。某歌单 449 个 ID：`n=0` 返回完整 449 个 ID、0 条完整资料、79,116 字节；`n=1` 返回相同 ID 数、1 条资料、81,390 字节。选择 `n=0` 再分批补资料，继续保留 1 MiB 响应上限。这个样本不代表所有大歌单均低于预算。

真实返回 MP3 在无声 PCM 模式下从 5 秒启动，并暂停定位到 10 秒，报告位置 10,000 ms、可定位、解码 31,104 帧。探针只读取既有受保护会话的临时副本，不写 Host 会话，不记录账号正文、ID、Cookie、歌词正文或签名 URL。[脱敏探针报告](assets/v4-preflight.json)。

原生技术验证发现：仅 `:start-time` 会让 WAV 先输出开头片段。修正为 `:start-paused` 加起点、在控制门内确认定位后恢复输出。自生成前正后负的 WAV 验证第一段 PCM 确实来自后段；暂停前后定位、旧代次拒绝及释放文件句柄通过。[定位证据](assets/m2-native-seek.json)。恢复初始化不打开媒体的应用行为将在 V4.5 落地。

本地入口 `pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V3`：锁定还原、Debug 0 警告、170 项全量测试、63 项门禁自测、M1/V3 映射与产物校验通过。结果目录 `TestResults/NetEaseV3/20260923-035423-727fd7f0`；[本轮自动摘要](assets/v4-preflight-verification.json)。这是现有回归加 V4.0 测试，不是完整 V4 门禁。

首次未修改业务的基线运行有 1 项既有 Headless 销毁异常，167/168 通过，保留在 `TestResults/NetEaseV3/20260923-034601-b94d41a5`。随后完整运行通过；未把首次失败覆盖为通过。上游 12.1.2 的会话启动使用闭包任务引用，存在启动先于任务字段赋值的竞争可能，本轮未修改第三方包或删除该测试，后续继续观察。

尚未验证实际声卡出声、真实 Host/Dock、双插件共存、规模上限和所有歌词轨道；它们仍按专项矩阵记录，不能从 MP3 解码或 Headless 推断完成。
