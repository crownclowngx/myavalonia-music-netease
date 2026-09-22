# MusicNetEasePlugin

本项目用于开发 Avalonia 网易云音乐播放器。当前代码仍是由 `myavalonia-plugin` 创建的 Managed Plugin
初始模板，网易接口与登录功能尚未实现。真实交付物是 `src/MusicNetEasePlugin.Plugin`；
`Standalone` 只负责快速预览同一份 View、ViewModel 与业务代码。

开发方向是以 api-enhanced 为主要协议上游，使用 C# 与 Flurl 统一请求，优先完成扫码登录最短路径：

- [Flurl 接入与扫码登录实施计划](docs/roadmap/netease-v1-flurl-login-plan.md)
- [扫码登录专用开发验证计划](docs/roadmap/netease-v1-login-verification.md)
- [上游能力与完整模块清单](docs/roadmap/netease-api-enhanced-capabilities.md)

以上为待实施计划。本阶段使用本地开发验证，不使用 AIFLOW、Windows CI 或发布门禁；正式发布时再按发布流程执行。

> 第一次开始开发前，请先阅读 [项目文档与快速开始](docs/README.md)。其中说明了三个子项目和
> Standalone 窗口的职责、接入真实 Host 的边界，以及临时部署和正式 ZIP 发布流程。

```powershell
dotnet restore
dotnet build -c Debug -warnaserror
dotnet test -c Debug --no-build
dotnet run --project src/MusicNetEasePlugin.Standalone
```

后续需要在真实 Host 中调试时，请显式提供 Host 的 `Controls` 目录；计划编写阶段不执行部署：

```powershell
dotnet msbuild src/MusicNetEasePlugin.Plugin/MusicNetEasePlugin.Plugin.csproj `
  -t:DeployManagedPlugin `
  -p:ManagedPluginDeployRoot=C:\Path\To\Host\Controls
```

Standalone 只能验证界面和插件自身对象图；manifest、加载上下文、Document Scope、Dock、Tool 和
生命周期必须使用真实 Host 做最终验收。

模板包含一条不注册快捷键的最小 Document Command 示例。设计边界、Target 适配和测试清单见
[Workbench Command 开发说明](docs/workbench-commands.md)。
