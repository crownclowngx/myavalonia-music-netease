# V5 修复版重新编译、部署与清理（2026-09-23）

> 用户再次要求部署至 `D:\data\avalonia\Controls`，不携带 libVLC，清理中间产物与旧版本。
> 源码提交 `cca46ba6f29cba5fd958371be2d21c282f1707cf`；V5 其余待决范围未继续开发，整体完成度见[复核清单](../../../maintenance/netease-v5-completion-audit-20260923.md)。
> [部署摘要](assets/redeployment-20260923.json) · [清理明细及剩余绝对路径](assets/redeployment-cleanup-20260923.json)

## 构建与实际部署

locked restore 成功；独立 Debug 输出使用 `IncludeLibVlcRuntime=false`，警告视为错误，构建 0 警告、0 错误。经 `DeployManagedPlugin` 先生成干净暂存，再部署到 `D:\data\avalonia\Controls\MusicNetEasePlugin`，12 个文件的名称、长度、SHA256 与暂存一致，清理后再次验证相同。

没有复制原生 `native` 目录、`libvlc.dll` 或 `libvlccore.dll`，保留必要的托管 `LibVLCSharp.dll`。未改写 Common、其他插件或用户配置，未创建旧版备份。入口 DLL SHA256：`667C74FAE28B88FDD2ABC58D1683338EC8ACD738839CDFA711B1C2871BD1AAF5`。

本次源码指纹与此前 309 项测试、249 项门禁自测通过的运行 `20260923-095639-88e63d4d` 一致，因此未重复整套业务测试。重新编译后的实际 Host 使用隔离 Host 数据目录启动并自动关闭，结果 ready、退出码 0、诊断 0 条；未运行 Windows CI、正式发布门禁或 AIFLOW，也不以启动成功代替完整 DPI/Dock/听音验收。

## 清理结果

已逐项核对四个项目 bin/obj、历次部署的构建/Controls 暂存/previous-plugin，以及本轮独立输出，共 29 个目录。均为仓库内绝对路径，无受控源码或重解析点。标准 `dotnet clean` 成功，删除 **427 个文件、87,368,957 字节（83.32 MiB）**。

随后按用户明确指示，以逐项列出的绝对 LiteralPath 删除旧版本和剩余中间目录。自动审批审核拒绝该操作，仅返回 **`blocked by policy`**，没有具体原因；删除命令未执行，没有切换工具绕过。

已选范围仍有 **634 个文件、188,064,385 字节（179.35 MiB）**，包括旧版副本、暂存目录及还原缓存。旧版本和全部中间产物清空的要求尚未完成，精确位置及前后数量见清理 JSON。源码、Git 历史、验证证据、当前实际部署和公共运行库均保留。
