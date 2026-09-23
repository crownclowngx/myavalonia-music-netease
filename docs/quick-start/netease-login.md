# 扫码登录开发快速开始

> 状态：V1 登录功能已实现，手工验收已由用户于 2026-09-23 确认完成；默认使用微信扫码，见[验收收口记录](../archive/records/netease-v1-v4-acceptance-20260923.md)。
> 本文介绍当前操作；后续回归方法见[维护矩阵](../maintenance/netease-login-verification.md)。

## 1. 构建与启动

需要 .NET SDK 10 和 PowerShell 7。仓库根目录执行：

```powershell
pwsh -NoProfile -File tools/verify-development.ps1
dotnet run --project src/MusicNetEasePlugin.Standalone -c Debug --no-build -- --data-dir D:\Temp\NetEaseLogin-Dev
```

请选择自己的独立开发数据目录，不要把真实登录数据放入仓库或与 Host/另一个进程共用。
不指定参数时，Standalone 使用当前用户 LocalApplicationData 下专用目录，详情见[会话契约](../reference/netease-http-session.md)。

## 2. 登录、恢复与退出

1. 打开页面会检查已保存会话；无会话时点击绿色“微信扫码登录 / 更新”。
2. 使用微信扫一扫，并确认“授权登录网易云音乐”。桌面收到确认后完成网易回调，再核对网易账号。
3. 页面显示昵称、账号 ID 和保存状态。头像失败时保留占位图。
4. 关闭窗口再以同一数据目录启动，账号检查成功后恢复；网络不通时可点击“重试恢复”。
5. “退出 / 清除本地登录信息”先清本地会话，再尝试远端退出。清除失败会保留醒目提示，可重试。

需要网易云音乐 App 扫码时，点击“网易云 App 扫码”；页面会更换二维码和扫码说明。
两种二维码不能混用：微信打开旧 App 二维码可能只登录手机网页，不会确认桌面登录。

关闭页面不是退出账号。取消扫码或刷新二维码会撤销旧尝试；过期后需要主动重取。
保存失败时本次内存登录仍有效，可点击“重试保存登录信息”，不需要重新扫码。
登录后可使用[日常播放器](netease-playback.md)的搜索、歌单、队列、歌词和本机历史/静默恢复。账号设置 Tool 提供登录恢复、保存重试、退出、LibVLC 目录及本地播放记录保存/清理重试；收藏与推荐等后续安排见[能力路线图](../roadmap/netease-capability-roadmap.md)。

## 3. 常见情况

| 情况 | 处理 |
| --- | --- |
| 网络失败/超时 | 等待有限重试结束，检查网络后重新尝试；已有保存文件不会因断网被删除 |
| 需要额外验证/限流 | 在官方客户端检查账号，稍后重试；插件不会高频循环登录 |
| 二维码过期/等待结束 | 点击重新生成；本地尝试预算为 3 分钟 |
| 微信已确认但网易未建立会话 | 在官方客户端完成账号绑定或额外验证，再生成微信二维码；不会停留在无限等待 |
| 微信登录页面或协议变化 | 重新尝试；仍失败可切换网易云 App 扫码 |
| 恢复文件损坏/无法解密 | 清除本地会话后重新扫码；更换 Windows 用户不能直接复用 DPAPI 文件 |
| 已登录但保存失败 | 检查目录可写性，点击重试保存；当前不提供明文保存选项 |
| 本地清除失败 | 关闭其他占用文件的进程后重试；不能把此状态当作退出已完全完成 |
| 本地已清除但远端未确认 | 重开不会从该文件恢复；可在官方客户端管理在线设备 |

## 4. 手动联网探针与验证记录

无需账号的 P0 探针是显式联网命令，不包含在默认测试或开发门禁中：

```powershell
dotnet run --project tools/MusicNetEasePlugin.LoginProbe -c Debug --no-build
```

探针创建一次 key、检查一次状态，再验证未登录结果；只输出脱敏状态，不读取历史 Cookie。
它不能证明真实手机 803 授权、恢复和远端退出通过。按[人工清单 M01–M08](../maintenance/netease-login-verification.md#7-m人工验证清单)记录这些结果，勿提交真实二维码、Cookie、session.bin 或完整账号响应。

真实微信流程探针使用相同的生产 Provider 和协调器，只把二维码图片写到指定绝对路径；不会持久保存账号 Cookie：

```powershell
dotnet run --project tools/MusicNetEasePlugin.LoginProbe -c Debug --no-build -- --wechat --output D:\Temp\netease-wechat.png
```

看到 `QR_READY` 后打开该图片，用微信扫码。成功以 `accountVerified=true` 为准；`persistentSessionWritten=false` 表示探针未写会话。
探针不验证重启恢复；2026-09-22 真实扫码结果见[专项记录](../archive/records/netease-v1/wechat-login-implementation-20260922.md)。
按用户指定路径进行开发部署，不执行 Windows CI 或发布门禁；部署后清理 bin/obj，下一次运行探针前需重新构建。
