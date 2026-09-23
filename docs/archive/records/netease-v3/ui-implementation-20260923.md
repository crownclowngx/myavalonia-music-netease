# V3 紧凑桌面界面、双色主题与轻量动效实施记录

> 后续状态：V1–V4 已于 2026-09-23 根据用户确认完成手工验收，见[验收收口记录](../netease-v1-v4-acceptance-20260923.md)。本文结果和未验收项是当次历史快照，原始证据保持不变。

> 日期：2026-09-23（Asia/Shanghai）。对象仅 MusicNetEasePlugin 的 Document / Tool、共用预览、测试与本地开发工具。
> [当前契约](../../../reference/netease-desktop-ui.md) · [专项维护矩阵](../../../maintenance/netease-v3-ui-verification.md) · [V3 方案](../../plans/netease-v3-desktop-ui-and-theme-plan.md)。

## 源码与交付

- 调研/修改前基线：`3018bb30ea6152fa4f7addf312540aad99df395e`。
- 方案提交：`14ca897`，`docs: 制定 V3 紧凑桌面界面与主题实施基线`。
- 实现提交：`0a491b2`，`feat: 实现 V3 紧凑桌面界面、双色主题与轻量动效`。
- 门禁在实现提交前对当时工作树运行，因此原始 verification 保留 `revision=14ca897`、`workingTreeDirty=true`。测试后原样提交为 `0a491b2`，本记录不将其伪装为干净提交后的另一次运行。后续只同步文档与证据。

Document 从大卡片纵向页变为紧凑账号摘要、弹性列式列表和固定播放条；Tool 使用账号/运行库/实际状态/界面分组。局部双色资源移除了 Document 公共浅色外层；二维码承载面保留白底。增加少量短过渡、按可见性收口的淡入及可保存的减少动态效果开关。

源码职责依照 SOLID 分开：偏好编排依赖窄存储端口，文件 IO 归 Infrastructure，ViewMotion 只拥有动效，View 只负责布局/订阅/图像，既有播放与账号所有权不变。未增加通用框架、事件总线或业务设计模式层。设计原因已用中文注释说明。

未改 Host、SDK、其他插件或发布配置；没有使用 AIFLOW、Windows CI、发布门禁，没有部署或推送远端。

## 验证过程与结果

| 操作 | 实际结果 |
| --- | --- |
| 修改前 M1 本地门禁 | 构建零警告；152 项中 151 通过、1 项真实音频解码结束等待超时，保留[失败 TRX](assets/baseline-m1-failed.trx) |
| 同产物独立重跑 AudioAdapterTests | 1 项通过，见[重跑 TRX](assets/baseline-audio-retry.trx)；不改写首次失败 |
| 新增 UI 测试初次调试 | 发现 Dispatch 无返回值可能选中 async void；早期看似通过的结果不作为验收证据。明确返回值后，暴露颜色字符串别名比较和主题内置过渡断言问题，分别改为 Color 值比较和只检查插件 BrushTransition |
| 滑块视觉复核 | 发现单改高度会裁切 Fluent Thumb；加入实际子控件边界断言并修正局部模板留白，保留裁切回归检查 |
| 最终 V3 本地门禁 | locked restore、Debug 构建零警告/零错误、168 项全通过、零跳过；63 项门禁自测通过 |
| 必测方法映射 | 既有 M1：58 场景、95 方法、152 用例；V3：7 自动分组、10 方法、16 用例 |
| 原生离线解码 | 生产 LibVLC 适配连续 12 轮合成 WAV 解码和媒体释放通过，见[离线证据](assets/libvlc-offline.json)，不是扬声器听感 |
| 文档与空白 | 本轮业务门禁检查通过；文档同步完成后再次执行链接与 Git diff 检查 |

最终运行标识 `20260923-020826-f14c1b3d`，测试开始 `2026-09-23T02:08:36Z`，门禁完成 `02:08:47Z`。原始目录 `TestResults/NetEaseV3/20260923-020826-f14c1b3d`；持久副本为[TRX](assets/v3-development.trx)和[verification.json](assets/verification.json)。源码和脚本对应实现提交；未以复用历史截图替代本次产物。

复现命令：

```powershell
pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V3
```

## 截图与视觉复核

全部使用真实生产 View，Avalonia Headless + Skia，固定离线账号/曲目/路径替身，逻辑尺寸与 PNG 像素按 1:1 渲染。不是设备 150%/200% 缩放验证；不存在真实账号 Cookie 或媒体签名 URL。

本次审阅覆盖深色宽 Document、浅色中宽 Document、深色窄 Document、深色 280 Tool、浅色 640 底部 Tool、双色登录区及打开菜单的同款主题组合。确认大面积浅色外层消失、播放条固定、窄列重排、长路径可编辑、滑块完整、菜单可读。Tool 在 640×240 需要向下滚动访问后续配置，属于设计行为；列表底部出现部分行由列表滚动裁切，不带动播放条。

| 页面内容尺寸 | 浅色 | 深色 |
| --- | --- | --- |
| Document 1200×720 | [截图](assets/v3-document-light-1200.png) | [截图](assets/v3-document-dark-1200.png) |
| Document 800×600 | [截图](assets/v3-document-light-800.png) | [截图](assets/v3-document-dark-800.png) |
| Document 520×420 | [截图](assets/v3-document-light-520.png) | [截图](assets/v3-document-dark-520.png) |
| Tool 280×900 | [截图](assets/v3-tool-light-280.png) | [截图](assets/v3-tool-dark-280.png) |
| Tool 320×900 | [截图](assets/v3-tool-light-320.png) | [截图](assets/v3-tool-dark-320.png) |
| Tool 420×900 | [截图](assets/v3-tool-light-420.png) | [截图](assets/v3-tool-dark-420.png) |
| Tool 640×240 | [截图](assets/v3-tool-light-640.png) | [截图](assets/v3-tool-dark-640.png) |
| 登录区 800×520 | [截图](assets/v3-login-light.png) | [截图](assets/v3-login-dark.png) |

[同款 Fluent / Semi / Ursa 基础主题组合，深色账号菜单](assets/v3-host-theme-dark-composition.png)另有自动绑定与跨 Window 主题继承断言。这个夹具没有加载实际 Host 和 Dock 管理器，不能算真浮窗或系统跟随验收。

## 动效与资源观察

机器：Windows 11 家庭中文版 10.0.26200，Intel Core Ultra 5 225H，14 逻辑处理器，约 31.4 GiB 可见内存。测试进程运行 Headless/Skia，固定 30 首本地替身数据，无在线请求；播放使用可控音频替身，真实解码另列。

每组等待有限淡入结束后采样约 500 ms；下表是**整个测试进程**的增量，包含测试框架、Headless 渲染与调度，不是插件动画独立 CPU，不能作为硬件性能达标结论。JSON 原值见[动效观察](assets/v3-motion-observation.json)。

| 减少动态效果 | 场景 | 墙钟 ms | 进程 CPU ms | 分配字节 | 采样后自有淡入 |
| --- | --- | ---: | ---: | ---: | --- |
| 关闭 | 空闲 | 500.35 | 156.25 | 4464 | 无 |
| 关闭 | 播放 | 500.09 | 250.00 | 8336 | 无 |
| 关闭 | 隐藏 | 501.89 | 171.88 | 15680 | 无 |
| 开启 | 空闲 | 503.07 | 78.13 | 8200 | 无 |
| 开启 | 播放 | 495.69 | 156.25 | 8336 | 无 |
| 开启 | 隐藏 | 499.59 | 31.25 | 7592 | 无 |

另执行 20 轮隐藏/恢复及重挂，模型、播放所有者和草稿保持；20 轮创建并拆卸音乐/设置 View 后，40 个弱引用在 GC 后全部释放。没有通过新增装饰计时器持续驱动 UI；该观察不覆盖所有原生图像/硬件渲染资源，也不承诺操作系统整体零重绘。

程序化交互记录包括：播放触发状态淡入 → 祖先隐藏撤销 → 恢复 → detach/attach（20 轮）→ 最小化 → 实际复选框减少动效 → 重新允许 → 原地换深色；歌曲仅创建一个音频句柄，草稿保持。缺少真机鼠标操作录屏、长时间多轮成本对照和硬件帧时间，已移交专项矩阵。

## 未覆盖项与后续承接

真实 Host/Dock 拖拽、浮动回停、系统主题、100%/150%/200% 与跨屏、真实二维码识别、各异常状态逐态视觉、硬件帧时间/输入响应及 M1 扬声器听感尚未新增验收。Standalone 已提供预览入口，但本轮没有把原生窗口人工操作记为通过。

自动套件与核心实现交付完成；完整实机结论继续按[专项矩阵](../../../maintenance/netease-v3-ui-verification.md)留证。当前部署仍为此前版本，使用新界面需要后续开发部署，不能把本地构建当作已更新运行中的 Host。
