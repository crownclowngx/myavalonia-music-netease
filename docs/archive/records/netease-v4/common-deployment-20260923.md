# V4 / M2 开发部署与中间产物清理（2026-09-23）

> 用户指定目标：`D:\data\avalonia\Controls`，复用 Common 中的 LibVLC。
> 源码基线：`f2654b248e920874227dc1129abe06856de18d46`。
> [M2 实施记录](m2-implementation-20260923.md) · [部署说明](../../../maintenance/deployment-and-release.md) · [部署文件摘要](assets/common-deployment.json) · [清理结果与绝对路径](assets/cleanup-result.json)。

## 部署结果

完成 locked restore，以 `IncludeLibVlcRuntime=false` 编译 Plugin，独立输出位于 `artifacts/v4-common-deployment-20260923-140214/bin`。Debug 构建 0 警告、0 错误；随后通过 `DeployManagedPlugin` 生成干净暂存目录，核对后整体替换 `D:\data\avalonia\Controls\MusicNetEasePlugin`。

部署前确认目标 Host 没有运行、插件文件未被占用，并核对路径、重解析点和唯一插件身份。旧部署的 12 个文件已备份并逐个核对摘要，备份位于本仓 `artifacts/v4-common-deployment-20260923-140214/previous-plugin`，没有放入 Controls。

目标共 12 个文件，逐个 SHA256 与暂存产物一致。没有原生 `native` 目录、`libvlc.dll` 或 `libvlccore.dll`；保留必需的托管桥接库 `LibVLCSharp.dll`。不携带 Host 共享的 Avalonia、Dock 或 SDK 程序集。插件版本仍为 `1.0.0`，SDK 区间 `[3.4.1, 4.0.0)`，manifest 由构建生成。

入口 `MusicNetEasePlugin.Plugin.dll` 的 SHA256：

```text
FE03242C6DF6D8EC40B433023AD1476F86459D96C83ACB1C7D2DFF36AF76FBBE
```

现有播放设置已经指向 `D:\data\avalonia\Common\libvlc`，无需重写。部署前后 Common 全部 425 个文件、插件用户目录 3 个文件的数量和 SHA256 保持不变；另外 12 个插件目录的名称、数量及修改时间保持不变。核对脚本首次将 JSON 日期自动解析成 DateTime，导致目录时间字符串比较误报；保持 ISO 日期为字符串后重新比较通过，没有因此重复部署。

## 验证范围

本轮未修改业务代码。源码基线此前通过完整 V4 本地开发门禁：276/276 项测试、0 跳过、166 项门禁自测，详见 [M2 实施记录](m2-implementation-20260923.md)。本轮追加 locked restore、无原生库的 Debug 编译、干净部署和文件摘要核对。

没有启动 Host；文件部署成功不代表真实声卡、Host/Dock 或重启恢复已经验收。这些项目仍由 [V4 专用验证矩阵](../../../maintenance/netease-v4-m2-daily-player-verification.md)跟踪。本次为目录开发部署，没有使用 AIFLOW、Windows CI、正式 ZIP 或发布门禁，也没有推送远端。

## 已完成的清理

清理范围限定在本插件仓库的四个项目 `bin/obj`、M1/V3 历史构建暂存、本轮独立输出及部署暂存，以及旧提交消息临时文件。清理前核对绝对路径、版本控制文件和重解析点，保存了逐目录文件数量与大小。

批量递归删除被自动审批审核拒绝，工具仅返回 `blocked by policy`，未给出更具体原因。没有换工具或拆分删除重试。随后执行标准构建清理：

```powershell
dotnet clean MusicNetEasePlugin.slnx -c Debug -v:minimal
dotnet clean src/MusicNetEasePlugin.Plugin/MusicNetEasePlugin.Plugin.csproj -c Debug -v:minimal `
  -p:IncludeLibVlcRuntime=false `
  -p:OutputPath=artifacts/v4-common-deployment-20260923-140214/bin/
```

两次 clean 均成功，共清理 **4,130 个文件、2,054,897,221 字节（约 1.91 GiB）**。清理后再次验证部署的 12 个文件、Common 的 425 个文件和用户目录 3 个文件，摘要全部一致。

标准 clean 未覆盖的中间文件共 **538 个、137,323,933 字节（约 130.96 MiB）**。三个非 Plugin 项目的 `bin` 已无文件；剩余主要是旧 M1 暂存、V3/V4 干净插件暂存和 NuGet 还原缓存。完整逐路径结果见[清理摘要](assets/cleanup-result.json)。

## 剩余目录及手工清理规则

以下路径均相对于仓库根目录：

```text
D:\code\local\avalonia_dock_plug_test\myavalonia-music-netease
```

| 可删除的相对路径 | 剩余文件数 | 剩余大小 |
| --- | ---: | ---: |
| `src/MusicNetEasePlugin.Plugin/bin` | 1 | 298 B |
| `src/MusicNetEasePlugin.Plugin/obj` | 13 | 230.65 KiB |
| `src/MusicNetEasePlugin.Standalone/obj` | 11 | 375.84 KiB |
| `tests/MusicNetEasePlugin.Tests/obj` | 10 | 282.49 KiB |
| `tools/MusicNetEasePlugin.LoginProbe/obj` | 10 | 293.75 KiB |
| `artifacts/m1-common-deployment-20260923` | 13 | 5.70 MiB |
| `artifacts/m1-development-20260923` | 450 | 112.26 MiB |
| `artifacts/v3-common-deployment-20260923-104516/bin` | 1 | 298 B |
| `artifacts/v3-common-deployment-20260923-104516/Controls` | 12 | 5.76 MiB |
| `artifacts/v4-common-deployment-20260923-140214/bin` | 1 | 298 B |
| `artifacts/v4-common-deployment-20260923-140214/Controls` | 12 | 6.09 MiB |

还可删除四个小型临时文件：`artifacts/current-v3-deployment.txt`、`artifacts/deployment-commit-message.txt`、`artifacts/login-commit-message.txt`、`TestResults/m1-commit-message.txt`，合计 3,404 字节。旧 V3 指针文件已过时，当前部署以本记录为准。

手工操作规则：

1. 停止本插件的调试、构建、测试和 Standalone；如果提示占用，先正常退出相关程序再重试。
2. 在资源管理器中打开上述仓库根目录，只删除表中列出的完整目录和四个临时文件。三个非 Plugin 项目的空 `bin` 也可删除。不要扩大到整个 `src`、`tests`、`tools`、`artifacts` 或 `TestResults`。
3. V3/V4 部署记录根目录只删除其 `bin` 和 `Controls` 子目录，保留 `previous-plugin` 回滚副本及 JSON、日志。表中的 `Controls` 均为仓库内暂存目录，不是已部署的 `D:\data\avalonia\Controls`。
4. 保留实际部署目录、`D:\data\avalonia\Common`、插件用户数据、测试证据、`artifacts/ui-review`、`artifacts/upstream-reference` 和诊断脚本。此次没有清理全局 NuGet 缓存。
5. 删除 `obj` 后，下次编译先执行 `dotnet restore MusicNetEasePlugin.slnx --locked-mode`，再按正常开发步骤构建；不要直接使用 `--no-restore`。

需要使用 PowerShell 时，对表中每一个确认过的绝对路径分别执行；例如删除体积最大的历史 M1 暂存目录：

```powershell
Remove-Item -LiteralPath 'D:\code\local\avalonia_dock_plug_test\myavalonia-music-netease\artifacts\m1-development-20260923' -Recurse -Force
```

该命令仅作为用户手工操作示例，本轮未执行。原始构建、部署、标准 clean 日志及清理计划保存在本仓 `artifacts/v4-common-deployment-20260923-140214`；仓库归档只保存不含账号内容的部署和清理摘要。
