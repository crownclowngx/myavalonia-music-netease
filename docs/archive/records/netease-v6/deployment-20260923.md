# V6 · 指定 Controls 部署与清理（2026-09-23）

> 用户要求：发布到 `D:\data\avalonia\Controls`，不部署 libVLC，清理中间文件，不需要备份。
> [部署摘要](deployment-20260923/deployment.json) · [本轮门禁报告](deployment-20260923/gate/verification.json) · [实际 Host 启动](deployment-20260923/host-startup.json) · [清理结果与剩余目录](deployment-20260923/cleanup.json)

## 部署结果

北京时间 20:18，使用现有开发部署流程，将 V6 完整实现部署到 `D:\data\avalonia\Controls\MusicNetEasePlugin`。源码提交为 `f46672a1093afe67cd2aa46934e7c6f389d03429`，本次操作未修改业务源码、依赖或插件版本。

- 独立 Debug 构建、暂存与实际部署均显式传入 `IncludeLibVlcRuntime=false`，共用本轮绝对 `OutputPath`。
- 通过 Build 包的 `DeployManagedPlugin` 筛选私有资产，只替换该插件目录；12 个文件的名称、长度和 SHA256 与干净暂存逐项一致。
- 插件目录中没有 `native`、`libvlc.dll` 或 `libvlccore.dll`；保留必要的托管 `LibVLCSharp.dll`。既有 `Common` 公共运行库未改动。
- 清单身份仍为 `myavalonia.plugin.music.netease`、版本 `1.0.0`，Controls 中该身份仅出现一次。
- 未创建旧版本备份。部署与隔离启动后，12 个兄弟插件目录、Common、网易云用户数据共 14 个目录的摘要均与操作前一致。

入口 DLL：841,728 字节，SHA256 为 `FC5CB37573653602B9AB4AC9A4B3EB5DE26A14F2F73B67E935AA93936E28805F`。完整文件表见部署摘要。

## 构建与验证依据

执行过程中再次检查时，工作树仍干净、HEAD 仍为 `f46672a`，但文件字节指纹已与实施阶段归档不同；现场分支为 `master`。因此本轮重新执行完整 V6 本地门禁，以最新实际文件为依据，未沿用旧指纹的结果。

| 项目 | 本轮结果 |
| --- | --- |
| runId | `20260923-121550-d3e724d6` |
| src/tests/tools 指纹 | `515199B9CBE13DF57F7FB20C6101BDDCB0787DFA839176C5431B81616A82AE19` |
| 锁定还原、解决方案构建 | 通过；0 警告、0 错误 |
| 单元及组件测试 | 326 通过，0 失败，0 跳过 |
| 门禁自测 | 310 项判定通过 |
| 回归 | 适用 V1–V5、V6 15 场景/22 方法/25 参数化用例、离线原生解码与资源检查通过 |
| 无原生 libVLC 独立构建 | 通过；0 警告、0 错误 |
| 原始门禁归档 | 99 文件、2,789,165 字节，含 TRX、截图及来源摘要；复制后逐项核对原字节 |

[完整执行日志](deployment-20260923/development-gate.log)、[独立构建日志](deployment-20260923/build.log)、[暂存日志](deployment-20260923/stage.log)与[实际部署日志](deployment-20260923/deploy.log)一并保存。归档设置 `-text`，防止 Git 换行转换改变证据摘要。原报告中 `hostVerified=false`、`deployed=false` 保留生成时事实，后续部署和启动由独立 JSON 承接。

实际启动 `D:\data\avalonia\MyAvaloniaManagement.exe`，使用本轮隔离 Host 数据目录与已有自动关窗开关：`firstFrameMs=884`、`readyMs=2254`，退出码 0，stderr 为空，诊断文件存在且记录为 0 条。此结果验证启动路径，不作为性能达标、Dock 操作、跨屏 DPI、物理 IME、读屏或真实出声验收。

本次为指定本机 Controls 部署；没有执行 AIFLOW、Windows CI、正式 ZIP 发布或发布门禁。

## 清理结果及限制

清理前核验 22 个绝对目录：四个项目的 bin/obj、TestResults、历次及本轮编译/暂存/观察目录。检查路径均在插件仓库内，没有跟踪源码、独立 Git 仓库或重解析链接；[清理清单](deployment-20260923/cleanup-plan.json)保留检查范围。正式归档、部署目录及上游研究参考 `artifacts/upstream-reference` 不在清理范围。

自动审批审查拒绝了这些目录的递归删除，原因为 `blocked by policy`；未通过更换删除方法绕过限制。随后执行项目标准清理：

```powershell
dotnet clean MusicNetEasePlugin.slnx -c Debug --nologo -v:minimal
dotnet clean src/MusicNetEasePlugin.Plugin/MusicNetEasePlugin.Plugin.csproj -c Debug --nologo -v:minimal -p:IncludeLibVlcRuntime=false -p:OutputPath=D:\code\local\avalonia_dock_plug_test\myavalonia-music-netease\artifacts\v6-deployment-20260923\bin\
```

两条命令均成功，共清理 **4,130 个文件、2,056,871,517 字节，约 1.92 GiB**。检查范围内仍有 **1,944 个文件、235,379,125 字节，约 224.48 MiB**，主要为暂存插件、历史副本、测试报告和 obj 元数据。目录级前后数量见清理 JSON；不能称为全部中间文件已清空。本轮没有新增备份，历史副本也未删除。

清理后再次核对实际部署 12 个文件、Common 和 12 个兄弟插件目录、Host EXE 以及源码指纹，全部一致。第二次扫描用户数据时，另一个实际 Host 已运行，媒体缓冲 `.owner` 被占用；因此不重复扫描活动用户数据，用户数据的“未变化”结论仅对应前述部署和隔离启动后的核对时点，未干预该运行进程。

README、文档导航、维护部署说明及 V6 专用实施记录同步指向本记录。V6 实机体验验收和既有 V5 性能待验范围继续按各自文档跟踪。
