# V5 · 启动注册故障修复（2026-09-23）

> 本记录只关闭启动加载故障，不表示 V5 已正式完成。[完成度复核](completion-audit-20260923.md)列出剩余范围。
> [完整本地门禁](startup-fix-20260923/gate/verification.json) · [真实 Host 前后对照](startup-fix-20260923/host-startup-comparison.json) · [部署摘要](startup-fix-20260923/deployment.json) · [标准清理](startup-fix-20260923/cleanup.json)

## 根因与修复

用户启动程序后，Host 的 17:48/17:49 诊断均报告 `PLUGIN_CONTRIBUTION_SERVICE_REGISTRATION_FORBIDDEN`，网易云插件在容器建立前被隔离。修复前使用已部署的 `D:\data\avalonia\MyAvaloniaManagement.exe`，隔离 Host 数据目录并自动关窗，再次复现相同错误。

V5 在共享私有服务入口 `AddMusicNetEasePluginServices` 中新增了 `MusicSettingsTool` 单例注册，同时模块已有 `AddTool<MusicSettingsTool, MusicSettingsView>` 声明。SDK 3.4.1 明确由 Host 在 Configure 结束后追加 Tool 单例，私有服务集合不得再次登记贡献根。普通 DI 容器允许多个同类型描述符，所以之前仅运行 Standalone/模型测试没有发现这一边界。

修复移除私有集合里的重复描述符，保留模块的 AddTool；MainDocument 继续注入由 Host 创建的同一个设置模型。Standalone 已在自己的组合根登记 Tool，仅补充中文注释说明所有权，未更改 Host、SDK、清单版本或放宽宿主校验。

## 验证

- 新增 `PluginRegistrationTests.真实模块只声明贡献根而不在私有服务中重复登记`：真实模块 Configure 后分别核对私有服务与贡献声明，修复前明确匹配到重复的 MusicSettingsTool 描述符并失败，修复后通过。
- 新增 `宿主追加贡献根后不同Document共用设置且关闭页面保留草稿`：验证公开 SDK 约定的 scope/singleton 组合、不同 Document 实例与共享 Tool、关页重开后草稿仍在。测试数据目录独立，容器按异步关闭链释放。
- 两项用例进入 V5 的 P03/U03 场景映射；最终 **309 项测试通过，0 失败、0 跳过，249 项门禁自测通过**；locked restore、Debug 构建 0 警告和 0 错误，运行编号 `20260923-095639-88e63d4d`。
- 门禁源码 SHA256：`B85FDBAF6046A402E947AA8259F9BDCB439CF2CD47E480025AD36FE6C21FF1A2`。门禁报告的 `hostVerified=false` / `deployed=false` 保留其生成时事实；后续启动与部署使用独立证据，未篡改原报告。
- 修复版部署后，同一个 Host EXE 本地启动进入 ready，自动正常退出，退出码 **0**，诊断记录 **0 条**。前后 Host EXE SHA256 相同，修复前会触发的注册错误消失。

启动复测使用 Host 已有的隔离数据目录与自动关窗开关，仅运行本地真实程序；未运行 Windows CI、发布门禁或正式 ZIP。它验证发现、注册、初始化和工作台启动，**不代表 Dock 操作、物理输入法、DPI、真实账号听音或整体易用性验收通过**。

## 部署与清理

北京时间 17:59，将独立 Debug 输出经 `DeployManagedPlugin` 部署到 `D:\data\avalonia\Controls\MusicNetEasePlugin`。12 个文件与暂存的名称、长度、SHA256 一致，不包含原生 libVLC；保留必要的托管 LibVLCSharp。按照用户要求没有新增旧版本备份。

入口程序集 SHA256：`C28533D1D4993C94D1EBF652292B95545CBF26DD901B8EDED3D33BFB3965FE24`。

完成后使用标准 `dotnet clean` 清理解决方案与本轮独立输出，移除 4,130 个文件、2,056,110,497 字节，清理后实际部署摘要仍一致。未重试此前被自动审批拒绝的递归删除；历史旧副本/暂存残留状态仍见此前清理记录，不能称为全部清空。
