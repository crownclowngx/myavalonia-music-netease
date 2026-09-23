# V2 / M1 搜索与单曲播放

V4.1 增加“我的歌单”入口：登录后点击该入口，选中歌单并点击“打开所选歌单”，在曲目页分页浏览或重试资料。此阶段先交付只读浏览，队列与播放全部将在 V4.2 接入；当前增量见[日常播放器说明](../reference/netease-daily-player.md)。

> 当前支持 Windows x64。功能已经实现；真实账号与两种运行库来源已解码验证，真实 Host/Dock 和扬声器听感待验收。完整边界见[当前播放契约](../reference/netease-music-playback.md)。

## 开始播放

1. 从 Host“新建”打开网易云音乐，按[登录步骤](netease-login.md)登录或恢复账号。
2. 输入歌名/歌手，按搜索按钮或回车。每页 30 条，用上一页/下一页切换；修改输入框后重新搜索才更换关键词。
3. 选中结果，点击“播放所选”。加载阶段会补齐详情并完整缓冲本次音频，随后在插件内播放。
4. 使用暂停、继续、停止和 0–100 音量。进度仅展示，本阶段不支持拖动。试听会明确标记，无地址会给出可恢复提示。

每个页面可独立搜索，所有页面观察同一个播放器。切歌替换当前单曲，不建立队列。关闭发起播放的页面、退出账号或关闭 Host 会停止；切换标签或临时重挂视图不停止。

## 配置多个插件共用的 LibVLC

打开“网易云音乐 · 账号与播放设置”Tool。它不要求先打开音乐页面，不会因显示 Tool 自动启动音频引擎。

选择包含 `libvlc.dll`、`libvlccore.dll`、完整 `plugins` 的 **Windows x64 目录**，可指向 VideoSecurityPlayer 已部署目录。先检测，再保存。检测只读文件元数据；保存的路径不会进入插件 manifest，也不会修改外部目录。

有效内置库始终优先；如果希望音乐插件复用外部库，使用下面的不携带原生库构建。内置缺失或不完整时才使用配置目录。Tool 分别显示已保存、候选和当前实际目录；首次原生加载后修改设置需要重启 Host。清空配置不删除共享库，退出账号不删除路径设置。

Tool 关闭只隐藏，再打开保留草稿。扫码入口仍在“新建”的网易云音乐 Document 中；当前 SDK 没有让 Tool 直接打开 Document 的公开端口。

## V3 界面与动效

Document 和 Tool 跟随所在 Host 窗口的深浅主题。结果列表独立滚动，底部播放条固定；窄窗口自动减少显示列。Tool 底部“界面 → 减少动态效果”立即关闭本插件额外过渡并保存偏好，账号和播放配置不受影响。完整说明见[当前界面契约](../reference/netease-desktop-ui.md)。

## 开发验证与运行

```powershell
pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V3
dotnet run --project src/MusicNetEasePlugin.Standalone -c Debug --no-build
```

Standalone 支持主题切换和并排/窄 Document/窄 Tool/底部 Tool 预览，复用生产模型和 View，账号数据使用独立目录。V3 门禁在 M1 基础上增加界面专项、17 张主题截图及动效观察；执行 locked restore、Debug 零警告构建、全部登录/音乐测试、真实无声离线解码、TRX/场景映射、生产 View 渲染及文档检查；不连接网易、不读取用户会话、不启动 Host 或系统音频输出。

需要显式验证真实账号和 MP3 解码时：

```powershell
$accountRoot = Join-Path $env:LOCALAPPDATA 'MyAvaloniaManagement/Plugins/myavalonia.plugin.music.netease'
dotnet run --project tools/MusicNetEasePlugin.LoginProbe -- --music `
  --session-directory $accountRoot --keyword '晴天' `
  --output TestResults/music-online.json
```

可追加 `--runtime-directory <LibVLC完整目录>` 单独验证指定来源。探针把受保护会话复制到独占临时目录，刷新只在内存中保存；不会覆盖 Host 会话。只输出结果摘要，不输出账号、Cookie 或签名 URL；结束清理本次缓冲及会话副本。它使用生产适配的 PCM 回调，**不证明扬声器出声，也不证明真实 Dock**。

## 不携带原生库的开发产物

在仓库根目录使用独立输出，避免普通 bin 中曾复制的内置库残留：

```powershell
$outputDir = [IO.Path]::GetFullPath('./artifacts/shared-libvlc/bin/') + [IO.Path]::DirectorySeparatorChar
$controlsDir = [IO.Path]::GetFullPath('./artifacts/shared-libvlc/Controls')
dotnet build src/MusicNetEasePlugin.Plugin/MusicNetEasePlugin.Plugin.csproj `
  -c Debug --no-restore -warnaserror -p:IncludeLibVlcRuntime=false -p:OutputPath=$outputDir
dotnet msbuild src/MusicNetEasePlugin.Plugin/MusicNetEasePlugin.Plugin.csproj `
  -t:DeployManagedPlugin -p:Configuration=Debug -p:IncludeLibVlcRuntime=false `
  -p:OutputPath=$outputDir -p:ManagedPluginDeployRoot=$controlsDir
```

该配置仍锁定 NuGet 原生包版本，关闭的是原生文件复制与交付，不需要重写 lock 文件。默认 `IncludeLibVlcRuntime=true`。正式安装到 Host 前按[开发部署说明](../maintenance/deployment-and-release.md)完整退出 Host、整体替换对应插件目录；本轮暂存产物不是正式发布包。

## 常见反馈

| 表现 | 处理 |
| --- | --- |
| 无运行库 / 模块 / 架构不匹配 | 在 Tool 检测完整 x64 目录；不要选单个 DLL 或 x86 目录 |
| 原生初始化失败 | 检查配套文件与版本，修复后重启 Host，不能在已加载进程内热换 |
| 加载等待较久 | 本阶段先完整缓冲，最大 64 MiB、总预算 120 秒；可停止或换曲 |
| 无地址 / 暂不可播 | 按当前账号返回值提示，可换歌或稍后重试，不自动请求更高权限 |
| 设置保存成功但来源仍是内置 | 符合内置优先规则；共享目录模式使用不携带原生库的产物 |
| 窗口较窄 | Document 自动收缩列并保留播放条；Tool 分组纵向滚动，宽度充足时并排 |
