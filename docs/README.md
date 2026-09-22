# MusicNetEasePlugin 文档导航与开发快速开始

> 用途：本插件唯一文档总导航。状态：扫码登录代码与本地开发门禁已实现，真实账号和 Host 待人工验证；核对日期：2026-09-22。
> 文档分类参考 host：roadmap 保留后续计划，reference/quick-start 为当前事实，maintenance 为复用验证，archive 为历史证据。

本解决方案用于开发 `myavalonia.plugin.music.netease` Managed Plugin。它把真实插件、独立 Avalonia 开发窗口和
自动化测试放在同一个解决方案中，使界面与业务代码既能快速预览，也能由 MyAvaloniaManagement Host
按正式插件协议加载。

## 当前计划与能力调研

| 任务 | 文档入口与状态 |
| --- | --- |
| 运行扫码登录 | [快速开始](quick-start/netease-login.md)：恢复、扫码、取消、退出与排错 |
| 查看当前架构和接口 | [HTTP 与会话契约](reference/netease-http-session.md)：Flurl、协议、凭据与生命周期 |
| 查看阶段进度 | [V1 实施计划](roadmap/netease-v1-flurl-login-plan.md)与[本轮记录](archive/records/netease-v1/login-implementation-20260922.md) |
| 执行本地开发门禁 | [专用回归矩阵](maintenance/netease-login-verification.md)：实际用例映射、门禁自测、人工验证边界 |
| 查看上游支持范围与后续扩容候选 | [api-enhanced 能力清单](roadmap/netease-api-enhanced-capabilities.md)：固定提交调研，含完整模块索引；不代表本插件已支持 |

本阶段不使用 AIFLOW、Windows CI 或发布门禁。真实账号观察、自动测试、Host 验证、部署与发布分别留证；
下面的打包和部署链接是既有模板参考，不是本轮开发的执行步骤。

## 项目结构

```text
MusicNetEasePlugin/
├─ MusicNetEasePlugin.slnx
├─ src/
│  ├─ MusicNetEasePlugin.Plugin/       # 唯一真实插件程序集和正式交付内容
│  └─ MusicNetEasePlugin.Standalone/   # 只供本地开发的 Avalonia 窗口
├─ tests/
│  └─ MusicNetEasePlugin.Tests/        # 插件业务、状态和注册行为测试
├─ tools/                           # 本地开发门禁、协议向量生成与显式联网探针
└─ docs/                            # 总导航、现行契约、维护矩阵、计划及历史记录
```

`MusicNetEasePlugin.Plugin` 是唯一正式插件项目。Standalone 和 Tests 都直接引用它，不能各自复制一套 View、
ViewModel、服务或贡献清单。

## 最短开发流程

在解决方案根目录打开 PowerShell：

```powershell
pwsh -NoProfile -File tools/verify-development.ps1
dotnet run --project src/MusicNetEasePlugin.Standalone -c Debug --no-build
```

Standalone 适合快速检查 AXAML、编译绑定、命令和插件自身对象图。写到可以联调时，再把干净的插件目录
部署到真实 Host；发布前则必须生成正式 ZIP。不要把 Standalone 能运行当成 Host 验收已经通过。

## 既有插件开发说明

1. [项目、Host 与 Standalone 窗口职责](project-and-window-responsibilities.md)
2. [临时部署、正式发布与验收](deployment-and-release.md)
3. [Workflow Action Provider 与 Consumer 接入](workflow-actions.md)
4. [Workbench Command 开发说明](workbench-commands.md)
5. [公共资源与专属图标](plugin-icons.md)：注册、画布、独立预览与私有包交付。

## 开发前记住

- `myavalonia.plugin.music.netease` 是持久身份，发布后不要因为显示名、项目名或文件夹改名而改变它。
- manifest 由 Build 包生成，不要手写或复制一份长期维护。
- 插件只通过公开 Plugin SDK 接入 Host，不引用 Host 内部项目。
- 新增插件运行时 NuGet 包时，要同时更新根目录 `Directory.Packages.props`、Plugin 项目的
  `PackageReference` 和 `ManagedPluginPrivatePackage`；完整示例见部署文档。
- 当前交付目标是 Windows x64；插件替换后必须完整重启 Host，不支持热更新。
- Workflow Action Provider 与 Consumer 是两种互斥角色，选择前先阅读专项文档，不要在同一插件中同时注册。
- Workbench Command 只提升跨工作台有价值的用户意图；当前登录页面不注册模板示例命令，也不占用快捷键。
