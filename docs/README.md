# MusicNetEasePlugin 文档导航与开发快速开始

> 用途：本插件唯一文档总导航。状态：当前代码为初始模板，网易登录方案待实施；核对日期：2026-09-22。
> 文档分类参考 host：待实施方案及配套验证放在 roadmap，实施后的现行契约、维护流程和历史证据分别维护。

本解决方案用于开发 `myavalonia.plugin.music.netease` Managed Plugin。它把真实插件、独立 Avalonia 开发窗口和
自动化测试放在同一个解决方案中，使界面与业务代码既能快速预览，也能由 MyAvaloniaManagement Host
按正式插件协议加载。

## 当前计划与能力调研

| 任务 | 文档入口与状态 |
| --- | --- |
| 确定 Flurl 请求方式与扫码登录最短路径 | [V1 实施计划](roadmap/netease-v1-flurl-login-plan.md)：待实施，SOLID 优先、模式朴素、详细中文注释 |
| 编写单元测试并执行本地开发门禁 | [专用开发验证计划](roadmap/netease-v1-login-verification.md)：矩阵已整理，测试与门禁尚未实现或执行 |
| 查看上游支持范围与后续扩容候选 | [api-enhanced 能力清单](roadmap/netease-api-enhanced-capabilities.md)：固定提交调研，含完整模块索引；不代表本插件已支持 |

本阶段不使用 AIFLOW、Windows CI 或发布门禁。真实账号观察、自动测试、Host 验证、部署与发布分别留证；
下面的打包和部署链接是既有模板参考，不是本次计划编写的执行步骤。

## 项目结构

```text
MusicNetEasePlugin/
├─ MusicNetEasePlugin.slnx
├─ src/
│  ├─ MusicNetEasePlugin.Plugin/       # 唯一真实插件程序集和正式交付内容
│  └─ MusicNetEasePlugin.Standalone/   # 只供本地开发的 Avalonia 窗口
├─ tests/
│  └─ MusicNetEasePlugin.Tests/        # 插件业务、状态和注册行为测试
└─ docs/                            # 文档总导航、模板说明与 roadmap 计划
```

`MusicNetEasePlugin.Plugin` 是唯一正式插件项目。Standalone 和 Tests 都直接引用它，不能各自复制一套 View、
ViewModel、服务或贡献清单。

## 最短开发流程

在解决方案根目录打开 PowerShell：

```powershell
dotnet restore
dotnet build -c Debug -warnaserror
dotnet test -c Debug --no-build
dotnet run --project src/MusicNetEasePlugin.Standalone
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
- Workbench Command 只提升跨工作台有价值的用户意图；模板示例默认不占用快捷键。
