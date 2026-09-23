# V4 / M2 日常播放器快速开始

登录后打开“我的歌单”，选中歌单并点击“打开所选歌单”，可分页浏览、播放全部或从这里播放。搜索和歌单均可追加歌曲或安排下一首；“播放队列”可调整顺序和四种模式。当前增量见[日常播放器说明](../reference/netease-daily-player.md)。

> 当前支持 Windows x64。功能已经实现；真实账号与两种运行库来源已解码验证，真实 Host/Dock 和扬声器听感待验收。完整边界见[当前播放契约](../reference/netease-music-playback.md)。

## 开始播放

1. 从 Host“新建”打开网易云音乐，按[登录步骤](netease-login.md)登录或恢复账号。
2. 输入歌名/歌手，按搜索按钮或回车。每页 30 条，用上一页/下一页切换；修改输入框后重新搜索才更换关键词。
3. 选中结果，点击“播放所选”。加载阶段会补齐详情并完整缓冲本次音频，随后在插件内播放。
4. 使用上下首、暂停、继续、停止和 0–100 音量。完整缓冲且可定位后可拖动进度，也可聚焦滑块使用方向键；松手提交，暂停定位不会继续出声。加载显示已下载大小，试听会明确标记。

每个页面可独立搜索，所有页面观察同一队列和播放器。直接播放搜索结果会替换为单项队列；追加保留原队列。关闭任意页面后继续播放，再开页面可继续控制；退出账号或关闭 Host 会停止。

点击“歌词”查看同步逐行歌词；接口有相应轨道时显示翻译和逐字高亮，缺失或损坏逐字轨道降级为逐行。手动浏览后暂停跟随，点击“回到当前歌词”恢复；读取失败可重试，不影响播放。试听暂不保证整曲时间映射，会明确显示同步受限。

“最近播放”显示本机实际正常推进过的歌曲，最多 200 首，支持直接播放和追加；直接播放会替换当前队列。重启后核验同一账号可恢复队列、模式、音量及位置，保持暂停，须点击“继续”。显式退出会清除续播队列但保留该账号历史；会话失效保留上次有效恢复记录。文件保存/清理失败可在页面提示或设置 Tool 重试。

## 配置多个插件共用的 LibVLC

打开“网易云音乐 · 账号与播放设置”Tool。它不要求先打开音乐页面，不会因显示 Tool 自动启动音频引擎。

选择包含 `libvlc.dll`、`libvlccore.dll`、完整 `plugins` 的 **Windows x64 目录**，可指向 VideoSecurityPlayer 已部署目录。先检测，再保存。检测只读文件元数据；保存的路径不会进入插件 manifest，也不会修改外部目录。

有效内置库始终优先；如果希望音乐插件复用外部库，使用下面的不携带原生库构建。内置缺失或不完整时才使用配置目录。Tool 分别显示已保存、候选和当前实际目录；首次原生加载后修改设置需要重启 Host。清空配置不删除共享库，退出账号不删除路径设置。

Tool 关闭只隐藏，再打开保留草稿。扫码入口仍在“新建”的网易云音乐 Document 中；当前 SDK 没有让 Tool 直接打开 Document 的公开端口。

## V3 界面与动效

Document 和 Tool 跟随所在 Host 窗口的深浅主题。结果列表独立滚动，底部播放条固定；窄窗口自动减少显示列。Tool 底部“界面 → 减少动态效果”立即关闭本插件额外过渡并保存偏好，账号和播放配置不受影响。完整说明见[当前界面契约](../reference/netease-desktop-ui.md)。

## 开发验证与运行

```powershell
pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V4
dotnet run --project src/MusicNetEasePlugin.Standalone -c Debug --no-build
```

Standalone 支持主题切换和并排/窄 Document/窄 Tool/底部 Tool 预览，复用生产模型和 View，账号数据使用独立目录。默认 V4 门禁保留 M1/V3 的映射、17 张 V3 截图和动效观察，加入 58 个 M2 场景、8 张播放器截图、原生定位/恢复/三曲与生命周期证据。执行 locked restore、Debug 零警告构建、全部测试、TRX/来源身份/源码指纹/产物检查及文档检查；不连接网易、不读取用户会话、不启动 Host 或系统音频输出。

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
| 自动播放停止 | 内容不可用最多尝试 20 个不同候选；网络、设备等错误立即停止扫描，可手动重试 |
| 已恢复队列但没有声音 | 符合静默恢复规则，点击继续才重新取得资源并播放 |
| 本地恢复未保存 / 清理失败 | 检查磁盘空间及权限，在提示栏或 Tool 重试；损坏/未来版本文件保留，明确新建或清空队列才替换 |
| 设置保存成功但来源仍是内置 | 符合内置优先规则；共享目录模式使用不携带原生库的产物 |
| 窗口较窄 | Document 自动收缩列并保留播放条；Tool 分组纵向滚动，宽度充足时并排 |
