using Avalonia.Headless;

namespace MusicNetEasePlugin.Tests;

/// <summary>
/// 和 Headless 官方 GetOrStartForAssembly 一样，让调度工作线程归测试进程所有。
/// 这里只需普通主题、宿主同款主题两种配置，最多创建两个线程；每次 Dispatch 仍用 PerTest 隔离，
/// 由 Headless 释放该次 Application、字体和服务 scope，业务窗口与容器继续由各测试显式释放。
/// 12.1.2 StartNew 闭包可能在任务字段赋值前构造 session，逐测试 Dispose 因此偶发空引用。
/// 使用进程寿命缓存避免该销毁路径；没有捕获/吞掉断言，也没有修改第三方私有字段或重跑失败用例。
/// 升级修复版本后可移除此兼容层。来源与复现记录见 V4 实施记录。
/// </summary>
internal static class HeadlessSessions
{
    private static readonly Lazy<HeadlessUnitTestSession> Normal = new(() => HeadlessUnitTestSession.StartNew(typeof(UiCompositionTests)));
    private static readonly Lazy<HeadlessUnitTestSession> HostTheme = new(() => HeadlessUnitTestSession.StartNew(typeof(HostThemeEnvironment)));
    internal static HeadlessUnitTestSession Get(Type entryPoint) => entryPoint == typeof(HostThemeEnvironment) ? HostTheme.Value : Normal.Value;
}
