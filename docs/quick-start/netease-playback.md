# V8 日常播放器、音乐库与发现快速开始

V6 已接入右侧歌词/队列抽屉和完整交互增强，验证边界见 [专用实施记录](../archive/records/netease-v6/drawer-and-interaction-implementation-20260923.md)。

- 点击底部“歌词”或“队列”展开同一个右侧抽屉，再点或按 Esc 收起。搜索和播放条一直可用，浏览列表保留。拖动左边线调整宽度，聚焦边线后左右键调整、Home 复位；窗口变窄不会覆盖保存的宽度。
- 歌词手动浏览后点击“回到当前歌词”恢复居中跟随；右键或键盘菜单选择“跳到此句”定位，暂停时保持暂停。不可定位时菜单显示原因。
- 队列用每行左侧手柄拖动，插入线指示位置；松手才生效，Esc 取消，也可继续用歌曲菜单上移/下移。最近一次移除或移动可撤销，播放和已切换的歌曲不回退。
- Ctrl+F 聚焦搜索，清空按钮清除文字；歌曲菜单可查看完整资料。歌单宽屏详情提供小封面与折叠简介。
- 设置“界面”可切换紧凑/舒适密度。恢复提示显示曲目数与上次位置，可收起；点击继续才出声。进度条悬停预览时间，键盘或拖动时显示目标时间。

登录后打开“我的歌单”，单击歌单名称，可分页浏览、播放全部或从这里播放。搜索和歌单均可追加歌曲或安排下一首；“播放队列”可调整顺序和四种模式。当前增量见[日常播放器说明](../reference/netease-daily-player.md)。

> 当前实现、性能与实机待验统一见[文档导航](../README.md#当前状态与待验范围)。最近开发部署为 2026-09-23 的 [V6 部署与启动复测](../archive/records/netease-v6/deployment-20260923.md)；V5 启动故障修复保留为历史记录。
> 当前支持 Windows x64，V1–V4 已实现并完成手工验收，依据见[验收收口记录](../archive/records/netease-v1-v4-acceptance-20260923.md)。完整边界见[当前播放契约](../reference/netease-music-playback.md)。

## 开始播放

1. 从 Host“新建”打开网易云音乐，按[登录步骤](netease-login.md)登录或恢复账号。
2. 输入歌名/歌手，按搜索按钮或回车。每页 30 条，用上一页/下一页切换；修改输入框后重新搜索才更换关键词。
3. 点击行内播放图标，或选中结果后按 Enter / 双击。加载阶段会补齐详情并完整缓冲本次音频，随后在插件内播放。
4. 使用上下首、暂停、继续、停止和 0–100 音量。完整缓冲且可定位后可拖动进度，也可聚焦滑块使用方向键；松手提交，暂停定位不会继续出声。加载显示已下载大小，试听会明确标记。

每个页面可独立搜索，所有页面观察同一队列和播放器。搜索、歌单或历史立即播放会在当前项后插入并播放，保留原队列；播放当前同曲则继续，不重复插入。只有“播放全部 / 从这里播放整份歌单”替换队列。关闭最后一个音乐 Document 会停止播放并释放媒体和 LibVLC 引擎，保留账号、队列及续播位置；重开后点击继续才重新创建引擎。仍有其他音乐 Document 时继续共享播放；切换标签、隐藏或拖动视图不停止。设置 Tool 不维持播放；退出账号或关闭 Host 也会停止。

点击“歌词”查看同步逐行歌词；接口有相应轨道时显示翻译和逐字高亮，缺失或损坏逐字轨道降级为逐行。手动浏览后暂停跟随，点击“回到当前歌词”恢复；读取失败可重试，不影响播放。试听暂不保证整曲时间映射，会明确显示同步受限。

“最近播放”显示本机实际正常推进过的歌曲，最多 200 首，支持直接播放和追加；立即播放保留原队列。重启后核验同一账号可恢复队列、模式、音量及位置，保持暂停，须点击“继续”。显式退出会清除续播队列但保留该账号历史；会话失效保留上次有效恢复记录。文件保存/清理失败可在页面提示或设置 Tool 重试。

## 发现音乐

1. 登录后点击“发现”，按需选择每日推荐、个性歌单、通用歌单或榜单。浏览和刷新不会出声；歌单 / 榜单的“打开”进入原歌单详情，沿用播放和收藏。
2. 歌曲行或播放条的更多菜单选择“查看歌手 / 专辑”，再选择真实歌手或专辑。歌手页可切热门 / 时间、歌曲 / 专辑，点击“更多”加载下一批。“返回”恢复前页，Esc 先关闭抽屉再返回作品页。
3. 完整推荐 / 专辑可“播放全部”；分页或不完整内容显示“播放已加载”。单曲仍可立即播放、下一首播放、追加、喜欢或加入歌单。
4. “私人 FM → 开始私人 FM”取得首批后替换队列。“下一首”只切歌；“不喜欢这首”会提交当前歌曲的推荐偏好，结果未知时不会自动重发。暂停仍用底部播放条。
5. “结束 FM”停止内容供应，保留现有队列并恢复原模式；要停止声音可用底部停止。普通点播 / 追加或改模式也会退出 FM。FM 期间上一首与队列编辑不可用。
6. 从“最近播放”选择“网易最近”“一周排行”或“全部排行”。网易来源与本机历史独立；未知播放时间不填当前时间，读取不补写本机历史，也不会自动上报播放。

关闭最后音乐页会停止 FM；重开 / 重启静默恢复普通队列，不自动开始 FM。响应限流时等待提示窗口；失败保留可解释的局部状态。完整预算和反馈说明见[音乐发现契约](../reference/netease-music-discovery.md)，真实账号及 Host 的[待验事项](../archive/records/netease-v8/acceptance-20260925.md)尚未收口。

## 管理自己的音乐

1. 歌曲行心形按钮显示喜欢状态；“?” 表示尚未读到可靠状态，在“我的歌单”点击“重新读取音乐库”。播放条“更多播放操作”提供当前歌曲的同款入口。
2. “我的歌单 → 打开歌单 ID”输入正整数 ID，详情可收藏 / 取消收藏他人歌单。该操作需要系统 WebView2 Runtime 和网易官方 SDK 可访问；令牌准备失败时不会发送收藏请求。
3. 点击“创建歌单”，输入名称，创建非私密普通歌单。自有普通歌单的“名称与描述”分别保存，描述留空可清空；失败保留草稿。重新载入会明确替换草稿。
4. 歌曲“＋”打开目标选择，选中歌单后点击添加。在弹层内创建歌单只选中新目标，还需再次明确添加；不会连带自动写曲目。
5. 详情中选中歌曲后使用“从歌单移除选中歌曲”；删除使用“删除歌单…”。两者都有目标确认，默认焦点为取消。内置喜欢、他人、共享或类型未知歌单不能编辑。
6. 页面上方“操作结果”查看结果、逐首分类和待核实资源，选择资源后“重新读取结果”只读不重发。创建缺失 ID 时先刷新目录核对，不能按同名自动认领；确认核对后可结束提示。

库修改不清空播放队列，不停止当前曲。超时或关闭面板不代表远端撤销；请回读核对。名称最多 100 个 Unicode 字符，描述最多 1,000 个；支持中文及 emoji。详细预算、权限与未知结果规则见[音乐库契约](../reference/netease-music-library-management.md)。真实账号、官方令牌链与 Host 仍按[V7 待验记录](../archive/records/netease-v7/acceptance-20260924.md)确认。

## 配置多个插件共用的 LibVLC

从账号菜单打开“账号与播放设置”，或打开 Host 的“网易云音乐 · 账号与播放设置”Tool。它不要求先打开音乐页面，不会因显示 Tool 自动启动音频引擎。

选择包含 `libvlc.dll`、`libvlccore.dll`、完整 `plugins` 的 **Windows x64 目录**，可指向 VideoSecurityPlayer 已部署目录。先检测，再保存。检测只读文件元数据；保存的路径不会进入插件 manifest，也不会修改外部目录。

有效内置库始终优先；如果希望音乐插件复用外部库，使用下面的不携带原生库构建。内置缺失或不完整时才使用配置目录。Tool 分别显示已保存、候选和当前实际目录；首次原生加载后修改设置需要重启 Host。清空配置不删除共享库，退出账号不删除路径设置。

Tool 关闭只隐藏，再打开保留草稿。扫码入口仍在“新建”的网易云音乐 Document 中；当前 SDK 没有让 Tool 直接打开 Document 的公开端口。

## 界面与动效

Document 和 Tool 跟随所在 Host 窗口的深浅主题。结果列表独立滚动，底部播放条固定；窄窗口自动减少显示列。设置“界面”提供封面、歌词翻译与减少动态效果；“减少动态效果”立即关闭本插件额外过渡并保存偏好，账号和播放配置不受影响。完整说明见[当前界面契约](../reference/netease-desktop-ui.md)。

## 开发验证与运行

```powershell
pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V8
dotnet run --project src/MusicNetEasePlugin.Standalone -c Debug --no-build
```

Standalone 支持主题切换和并排 / 窄 Document / 窄 Tool / 底部 Tool 预览，复用生产模型和 View，账号数据使用独立目录。默认 V8 检查保留适用的 M1 / V3 / M2 / V5 / V6 / V7 回归，并核验抽屉、手势、发现、FM、布局与资源证据；门禁不连接网易业务、不读取用户会话、不启动真实 Host 或系统音频输出。检查步骤、产物和证明范围统一见[验证指南](../maintenance/verification.md)。清理 bin/obj 后需重新构建再使用 `--no-build`。

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
