# 不携带 libVLC 的本地 ZIP 打包

在 `myavalonia-music-netease` 项目根目录执行，要求 PowerShell 7、.NET SDK 10：

```powershell
pwsh -NoProfile -File .\build-zip.ps1
```

默认生成 **Debug、win-x64、不含 libVLC 原生运行库** 的本地插件包。无需传项目路径、版本号或关闭 libVLC 的参数。也可以在 PowerShell 7 中直接执行 `./build-zip.ps1`。

每次输出到独立目录，不覆盖旧包：

```text
artifacts/zip/Debug-no-libvlc-<时间>-<随机标识>/
├─ MusicNetEasePlugin.Plugin-<PluginVersion>-win-x64.zip
└─ MusicNetEasePlugin.Plugin-<PluginVersion>-win-x64.manifest.json
```

ZIP 内保持 `Controls/MusicNetEasePlugin/` 布局。`PluginVersion` 来自插件项目，当前为 `1.0.0`，脚本不修改版本。配套外置清单记录 ZIP 及各文件摘要，和 ZIP 一起保留。

需要不同配置或输出位置时：

```powershell
pwsh -NoProfile -File .\build-zip.ps1 -Configuration Release
pwsh -NoProfile -File .\build-zip.ps1 -OutputDirectory 'D:\Packages\NetEase'
```

相对输出路径以脚本所在项目根目录为准，绝对路径按原值使用；含空格的路径加引号。两种配置均不携带 libVLC 原生库。安装后使用插件设置中的共享 LibVLC 目录，参见[共享库配置](../quick-start/netease-playback.md#配置多个插件共用的-libvlc)。

## 实现与验证边界

[脚本](../../build-zip.ps1)先锁定还原，再调用现有 Build 包的 `BuildManagedPluginPackage`，由它在独立暂存目录编译、按私有资产声明筛选文件、打包并校验摘要；不会直接压缩普通 bin 或手工删除其中的文件。任何 dotnet 非零退出立即终止。

打包 Target 会启动新的 PowerShell / dotnet 进程，外层 `-p:IncludeLibVlcRuntime=false` 不会自动传入这些子构建。因此脚本同时设置进程环境变量 `IncludeLibVlcRuntime=false`，结束或失败时恢复原值。现有项目按该值关闭 VideoLAN 原生库复制与目录资产声明。

完成后重新打开实际 ZIP，拒绝 `libvlc.dll`、`libvlccore.dll` 和 `libvlc/` 目录，确认插件清单及入口、LibVLCSharp、BouncyCastle 存在，并核对 ZIP 与配套清单的 SHA-256。LibVLCSharp 是运行时必需的托管适配器，不属于此次排除的原生运行库；WebView2 等其他私有依赖按既有声明保留。

此脚本只生成本地包，不调用 Windows CI、AIFLOW、单元测试或发布门禁，不安装到 Host，也不上传。完整开发验证仍使用 `tools/verify-development.ps1`；真正正式发布时另按[发布流程](deployment-and-release.md#正式发布-zip)完成验收。

## 2026-09-26 本地验证

实际执行默认命令成功，Debug 构建零警告、零错误。生成目录为 `artifacts/zip/Debug-no-libvlc-20260926-165221-1e6dfa69/`；ZIP 包含 15 个文件、3,620,667 字节，SHA-256 为 `2E441ACC50C24C0D2555FD2B1D524B6556C4D39C757FECF1D64E56BDF0F231AE`，与配套清单一致。

已读取真实 ZIP 条目确认没有 libVLC 原生库，保留 LibVLCSharp、BouncyCastle、WebView2 和许可文件。以受控 dotnet 退出码 17 验证失败会向上传播，调用者环境变量和工作目录均恢复。以上为本地打包验证，没有执行安装、Host 验收或正式发布；产物位于被 Git 忽略的 artifacts，可随本地清理移除。

另行执行完整本地 V8 开发门禁，通过 541 项测试、645 项门禁自测和 66 个 Markdown 链接检查，Debug 构建零警告；运行 ID 为 `20260926-085324-c4d6e11a`。根目录脚本已纳入开发检查源码指纹，打包脚本本身不自动触发这些检查。
