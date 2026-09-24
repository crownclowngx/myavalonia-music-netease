# V5 核心交互与 UI 开发部署及清理（2026-09-23）

> 用户指定：编译并部署到 `D:\data\avalonia\Controls`，不携带 libVLC；清理中间产物及旧版本副本。
> 本次为本地开发部署，未运行 AIFLOW、Windows CI、正式 ZIP 或发布门禁。
> [实施记录](ui-interaction-implementation-20260923.md) · [门禁证据](gate-20260923/verification.json) · [部署文件摘要](assets/deployment-20260923.json) · [清理明细](assets/cleanup-20260923.json)

## 编译与部署

北京时间 17:42 完成部署。最终本地门禁 307 项测试通过、0 失败、0 跳过，249 项门禁自测通过；源码 SHA256 为 `DCA795554C7D4A5844243C41435F7E1DE7BBF8AA361F518FBF1C02CC8CAAE563`，部署前复核未变化。报告保留运行时的基础提交 `a9b85f0` 和工作树修改状态。

使用独立 `artifacts/v5-deployment-20260923/bin/`，Debug 构建 0 警告、0 错误；构建与暂存、实际部署均传入 `IncludeLibVlcRuntime=false`。`DeployManagedPlugin` 生成干净目录后，先检查并备份原目录，再部署至：

```text
D:\data\avalonia\Controls\MusicNetEasePlugin
```

12 个文件逐项名称、长度、SHA256 与暂存产物一致。目标只有一个 `myavalonia.plugin.music.netease` manifest，版本保持 `1.0.0`，入口与 SDK 区间不变。没有原生 `native`、`libvlc.dll`、`libvlccore.dll`，没有 Host 共享的 Avalonia / Dock / PluginSdk DLL；必要的托管 `LibVLCSharp.dll` 仍携带。

入口 `MusicNetEasePlugin.Plugin.dll` SHA256：

```text
A0E36616E7D53B43370C53166FC6011B99EABB2A86A35D422E02DB71A4CE149E
```

| 同配置体积 | V4 | V5 | 增量 |
| --- | ---: | ---: | ---: |
| 12 个部署文件 | 6,383,814 字节 | 6,528,114 字节 | 144,300 字节 / 2.26% |
| 入口 DLL | 648,192 字节 | 763,392 字节 | 115,200 字节 |
| 调试 PDB | 155,908 字节 | 185,008 字节 | 29,100 字节 |

其余 10 个文件的 SHA256 未变。部署前后 `D:\data\avalonia\Common` 的 425 个文件摘要相同，其他 12 个插件目录名称与修改时间相同。部署范围未包含用户数据；本轮不额外读取用户账号和设置内容。

## 清理结果与未完成项

用户进一步明确旧版本副本也不需要。已列出本仓四个项目的 `bin/obj`、历次部署构建输出、Controls 暂存及 `previous-plugin` 的绝对路径、数量和大小；校验路径均在本仓，且没有受控源码或重解析点。

递归删除上述目录的操作被自动审批审核拒绝，仅返回 **`blocked by policy`**，没有更具体原因；命令未执行，没有更换工具或拆分相同删除操作绕过限制。随后执行构建系统自己的标准清理：

```powershell
dotnet clean MusicNetEasePlugin.slnx -c Debug -v:minimal
dotnet clean src/MusicNetEasePlugin.Plugin/MusicNetEasePlugin.Plugin.csproj -c Debug -v:minimal `
  -p:IncludeLibVlcRuntime=false `
  -p:OutputPath=D:\code\local\avalonia_dock_plug_test\myavalonia-music-netease\artifacts\v5-deployment-20260923\bin\
```

两次 clean 均成功，共删除 **4,130 个文件、2,056,083,569 字节（约 1.915 GiB）**。已选清理范围仍有 **608 个文件、175,007,569 字节（约 166.90 MiB）**，包含旧插件副本、历史/本轮暂存及 NuGet 还原缓存，未达到“旧版本和中间产物全部清空”。精确路径、前后数量及大小见清理 JSON；归档报告、源码、实际 Controls 和公共库均不在清理目标中。

当前部署的 12 个文件在清理后再次按摘要核验一致。下次编译先运行 locked restore，再 build。旧备份仍在本仓 `artifacts`，不会被 Host 的 Controls 插件扫描加载；其保留由执行限制导致，并非产品需要。

## 验证边界

V5 A–D 核心代码、自动测试、资源预算、Headless 实际 View 截图和部署已完成；可选阶段 E 未纳入核心。未启动真实 Host，未做真实账号联网、物理 IME、多 DPI、声卡听音和 Dock 验收。资源观测中暂停可见 CPU 增量约 2.05 个百分点，未达到建议的 +1 个百分点；测量条件与其余结果见实施记录，不将自动测试通过等同于全部性能目标或实机验收通过。
