# Transparent Overlay Regression / Recovery

日期：2026-08-17
分支：dev

## 现象

为解决 TacticalMap HtmlUI 覆盖整个游戏窗口时出现白色背景的问题，曾尝试把 WebView2 背景设置为透明。

随后出现大量编译错误，包括 `HtmlUiHost.Navigate`、`ValidatePage`、`RegisterCommand`、`RegisterRequest`、`SendEvent`、`DispatchToGameThread`、`SendResponseAsync` 等成员不存在，以及 `HtmlUiPage.Path` 不存在。

## 根因

提交 `377b50f3cd0b352e3b7c6b64628c3c349680bbff` 在修改透明初始化时错误地大幅重写 `HtmlUiHost.cs`，删除了约 302 行原有 Host 实现，因此产生大量连锁 CS1061 / 委托类型错误。

此外，当前项目使用 `Microsoft.Web.WebView2` 1.0.2849.39；针对 `CoreWebView2.DefaultBackgroundColor`、`WebView2.CoreWebView2Controller`、`CoreWebView2ControllerOptions.DefaultBackgroundColor` 的几种尝试均不能直接按当前 SDK 编译。

## 恢复

`HtmlUiHost.cs` 已恢复为透明实验之前的完整实现基线，保留已有 Framework 生命周期、Request、Binding、Localization 等通过验收的实现。

透明功能暂时不进入 Framework 核心，避免再次破坏稳定基线。

## 当前状态

- Framework Host：已恢复完整实现
- 原有 API：恢复
- 原有测试基线：恢复到透明实验之前
- TacticalMap 并行 HtmlUI：代码链本身已能实际显示
- TacticalMap 白色背景：仍待单独解决

## 后续原则

不要直接重写 `HtmlUiHost.cs`。
透明 Overlay 应作为独立、可验证的能力接入，并先确认当前 WebView2 SDK / COM API 的实际支持情况，再进入 Framework 核心。
