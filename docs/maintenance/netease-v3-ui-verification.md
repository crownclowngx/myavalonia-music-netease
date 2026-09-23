# V3 紧凑界面与主题专用开发验证

> 更新：2026-09-23。V3 实现、自动验证与手工验收已完成，用户确认依据见[验收收口记录](../archive/records/netease-v1-v4-acceptance-20260923.md)。
> [设计方案](../archive/plans/netease-v3-desktop-ui-and-theme-plan.md) · [当前界面契约](../reference/netease-desktop-ui.md) · [当次结果与证据](../archive/records/netease-v3/ui-implementation-20260923.md)。不使用 AIFLOW、Windows CI 或发布门禁。

## 运行方式

```powershell
pwsh -NoProfile -File tools/verify-development.ps1 -Milestone V3
```

当前不传 Milestone 时默认 [V4](netease-v4-m2-daily-player-verification.md)，Login/M1/V3 入口保留；所有入口仍运行完整测试项目，V4 保留本页的全部映射、截图与动效检查。V3 在 M1 证据检查之上增加 UI 专项；锁定还原、Debug 零警告构建、完整 TRX、文档链接、Git 空白检查均属于本地开发门禁。

门禁为每轮建立独立证据目录，任何失败、跳过、零测试、超时、旧 TRX、缺失方法/参数化用例、无效截图或采样缺项都会失败。映射为 [v3-test-map.json](../../tools/v3-test-map.json)，不扫描 Trait 文字冒充执行。门禁自身有失败注入自测。

## 自动覆盖与方案对应

| 自动分组 | 实际方法或断言 | 对应方案及限制 |
| --- | --- | --- |
| THEME-AUTO | `DesktopUiTests.真实Document主题尺寸与播放区域验证`、`真实Tool宽窄布局和主题切换保留草稿`、`宿主同款主题组合下菜单绑定和局部资源独立` | T01/T02；画刷、热切换、Fluent→Semi→Ursa 组合、菜单命令与跨 Window 挂载。T03 系统联动和真浮窗另验 |
| DOCUMENT-AUTO | 双主题 1200×720、800×600、520×420；控件及 Thumb 边界、列表高度、选择、播放器实例与暂停/继续显示 | D01、D02 的主要控件子集；错误/试听等由 M1 模型回归和人工逐态检查覆盖 |
| TOOL-AUTO | 双主题 280/320/420×900、640×240；长路径、横向边界、草稿保持、不加载音频引擎 | U01；U02 检测/保存/加载规则仍由 M1 套件验证 |
| LOGIN-AUTO | 双主题登录区、二维码白色承载面、窄窗重排；原有 UiCompositionTests 继续验证实际二维码绑定、登录与挂载释放 | D03 的布局与流程子集；不证明手机识别或全部错误状态的视觉质量 |
| MOTION-AUTO | 20 轮祖先隐藏/恢复和重挂、最小化、复选框、原地切主题；自有淡入取消、BrushTransition 撤销，播放/草稿保留 | M01/M02 程序化子集；鼠标悬停和真忙碌下的 Dock 操作另验 |
| PREFERENCES-AUTO | UiPreferencesTests 覆盖原子保存、取消、默认、损坏/未知版本、迟到读取、读一次、串行合并写、失败与重试；共享 View 同步 | M03；文件重建读取已有断言，真实重启插件体验另验 |
| RESOURCE-AUTO | 离线固定数据开/关动效 × 空闲/播放/隐藏；淡入有界结束；20 轮后 40 个音乐/设置 View 可回收；不新增 Application 资源 | M04 自动观察子集，不是硬件帧时间或 CPU 性能达标认证 |

既有 M1 全量用例继续保护登录、搜索、账号代次、双 Document 共享播放与页面寿命、真实离线 PCM 解码、目录选择器迟到结果、位图与资源释放。详见 [M1 验证矩阵](netease-v2-m1-playback-verification.md)。

Headless Dispatch 必须使用可等待的泛型异步重载，lambda 明确返回值，防止误选 async void 导致测试提前结束。截图存在只证明生成了文件，仍需视觉审阅。

## 截图与采样约定

- `v3-document-{light|dark}-{1200|800|520}.png`：真实 Document 播放页。
- `v3-tool-{light|dark}-{280|320|420|640}.png`：真实 Tool 长路径与窄/底部布局。
- `v3-login-{light|dark}.png`：未登录和二维码承载面，不是在线二维码识别证据。
- `v3-host-theme-dark-composition.png`：同款基础主题组合、账号菜单，不是实际 Host 截图。
- `v3-motion-observation.json`：六组固定数据，每组约 500 ms；墙钟、整个测试进程 CPU、分配字节、剩余淡入与弱引用回收数。
- `verification.json`：Git revision、工作树状态、测试与场景数、时间、机器及未执行的真实账号/Host/发布标志。

审阅背景/前景、主操作边界、长文本、登录例外、选中与滚动；不能因为 PNG 头正确就宣布视觉通过。Headless 渲染及调度会贡献进程 CPU，短采样只用于观察和复现，不能计算“插件动画占用率”或承诺零消耗。

## 手工验收与后续回归

| 项目 | 操作及应记录内容 |
| --- | --- |
| 实际 Host / Dock | 核对产物身份；主窗/浮窗深浅切换及跟随系统；拖动分组/浮动/回停/Tool Hide；窗口/内容尺寸、播放和草稿 |
| 真忙碌与异常状态 | 扫码等待/过期/取消、保存失败/清理失败、空结果/搜索错误、音频加载/试听/错误、运行库失败/待重启；深浅逐态检查 |
| 键盘与缩放 | Tab、回车、列表选择和音量；100%/150%/200% 与跨屏；焦点、裁字和菜单 |
| 扫码识别 | 实际手机扫描，核对静区和白底；图片显示不等于识别通过 |
| 动效实机成本 | 同机固定数据多轮对照；真实滚动、快速交互、隐藏和最小化；记录机器、缩放、采样时长、输入延迟和帧时间 |
| M1 播放回归 | 扬声器听感、真实双插件共存等按 M1 矩阵复验并记录当次结果 |

现有 V3 手工验收已由用户确认完成，见[验收收口记录](../archive/records/netease-v1-v4-acceptance-20260923.md)。以上保留为后续回归方法，整体确认不补造具体缩放样本、机器参数或硬件帧时间；部署和正式发布分别记录。
