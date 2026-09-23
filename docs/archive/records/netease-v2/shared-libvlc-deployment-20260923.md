# M1 公共 LibVLC 配置与开发部署记录（2026-09-23）

> 后续状态：V1–V4 已于 2026-09-23 根据用户确认完成手工验收，见[验收收口记录](../netease-v1-v4-acceptance-20260923.md)。本文结果和未验收项是当次历史快照，原始证据保持不变。

> 本记录对应用户指定的 Controls 部署、公共 LibVLC 路径配置与中间文件清理。源码基线为 `c705b76959de60518736eaed0f46e11c35367de0`；沿用本地 Debug 开发部署，不执行 Windows CI、正式 ZIP 或发布门禁。

## 1. 部署结果

2026-09-23 09:06（北京时间）部署到 `D:\data\avalonia\Controls\MusicNetEasePlugin`，保持插件版本 `1.0.0`、SDK 区间 `[3.4.1, 4.0.0)`。

先完成全解决方案 `--locked-mode` 还原，再以 `IncludeLibVlcRuntime=false` 编译 Plugin 到独立输出目录；Debug 构建 0 警告、0 错误。先用 `DeployManagedPlugin` 生成暂存目录并核对资产，再以同一输出、同一参数部署到用户指定 Controls。

执行部署前检查了目标绝对路径和重解析点，并确认 Host 安装目录没有运行中的进程。部署仅替换插件叶子目录，其他 12 个插件目录保留。

交付目录共 12 个文件：入口 DLL/PDB/deps、manifest、LibVLCSharp、BouncyCastle、Flurl、Flurl.Http、QRCoder、ProtectedData、Icons 和第三方声明。没有内置原生库目录，也没有携带 Host 统一提供的 Avalonia、Dock 或 SDK 程序集。12 个文件的 SHA256 均与暂存产物一致，清理后再次核对一致。

入口 `MusicNetEasePlugin.Plugin.dll` 的 SHA256：

```text
2747CF1D5FBF808038C95AC9DF75F763D7A25D32E58B77DB5D1D71AC133E8866
```

## 2. 公共运行库与账号设置

公共目录为 `D:\data\avalonia\Common\libvlc`，核心版本 `3.0.23`，所需 codec、demux、audio_output 模块齐全。部署前后对目录内 425 个文件逐一核对 SHA256，内容保持不变。

Host 设置文件为：

```text
C:\Users\crown\AppData\Local\MyAvaloniaManagement\Plugins\myavalonia.plugin.music.netease\playback-settings.json
```

通过同目录临时文件原子替换保存以下内容，符合生产设置存储的 schema：

```json
{
  "schemaVersion": 1,
  "customDirectory": "D:\\data\\avalonia\\Common\\libvlc"
}
```

由于有效内置库始终优先，本次特意部署不携带原生库的布局，使指定公共路径成为实际来源。只设置 Host 数据目录，未修改 Standalone 配置。

部署操作没有修改 `session.bin` 或 `device.id`，部署完成时摘要与操作前一致。09:07:48 观察到 Host 从指定安装目录重新启动，09:07:59 会话文件随后出现更新，设备标识内容保持一致；没有覆盖这次运行中的会话数据。

后续只读进程模块检查确认，该 Host 的 `libvlc.dll`、`libvlccore.dll` 均来自 `D:\data\avalonia\Common\libvlc`。这证明公共库在 Host 中实际加载；未据此将完整 Dock、双插件并发或扬声器听感验收标记为通过。

## 3. 验证范围

- 当前源码既有完整开发门禁见 [M1 实现记录](m1-implementation-20260923.md)：152 项测试、29 项门禁自测全部通过。本次没有修改业务源码，不重复完整业务门禁。
- 本次完成锁定还原，以及 Plugin 和显式探针的 Debug 构建，均为 0 警告、0 错误。
- 显式探针使用公共目录和既有受保护会话的临时副本，复用生产搜索、详情、资源解析与播放服务。09:05:06 得到 30 条搜索结果，歌曲身份一致，标准 MP3、非试听；成功解码 52677 帧，暂停、音量、恢复、停止流程完成。
- 探针运行库来源为 `Configured`，实际目录为指定 Common 路径，版本为 `3.0.23 Vetinari`。探针采用无声 PCM 回调，不代表实际声卡出声；探针自身不写 Host 会话，临时会话与媒体由探针释放清理。
- 实际部署文件、设置文件、公共库摘要和 Host 原生模块来源另行核对，证据保留在忽略目录 `TestResults/deployment-20260923-common-libvlc/`，不提交账号或测试临时数据。
- 更新部署专用记录与导航后，26 份文档的链接、锚点检查通过；14 个邻仓参考单独列出，`git diff --check` 通过。

## 4. 中间文件清理与剩余项

执行解决方案 `dotnet clean -c Debug`，并对两次独立编译输出执行对应 `OutputPath` 的标准 clean，全部退出码为 0。已清理 1,925,190,060 字节（约 1.79 GiB），保留测试证据和空目录。

自动审批拒绝了批量删除构建目录，以及按已核对清单逐文件删除的动作；仅返回 `blocked by policy`，未给出更具体原因。没有继续以其他删除工具绕过该限制。标准 clean 后仍有以下文件，因此本次清理未全部完成：

| 位置（相对仓库根目录） | 剩余文件数 | 内容 |
| --- | --- | --- |
| `src/MusicNetEasePlugin.Plugin/bin` | 1 | 构建生成的 manifest |
| 四个项目的 `obj` | 43 | 还原与构建缓存 |
| `artifacts/m1-development-20260923` | 450 | 此前内置/共享布局暂存目录及残留 manifest |
| `artifacts/m1-common-deployment-20260923` | 13 | 本次干净插件暂存目录及残留 manifest |

剩余约 119 MiB。Standalone、Tests、LoginProbe 的 `bin` 已无文件。正式部署目录、公共 LibVLC、其他插件、参考资料及验证记录均保留；清理不影响已部署插件的使用。
