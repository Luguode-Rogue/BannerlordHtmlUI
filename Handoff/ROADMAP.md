# ROADMAP

## M0 工程基线
基本完成：BUTR layout、Debug config、Framework + Consumer TestMod。

## M1 Host / Lifecycle — 基本完成

- WebView2 UI thread / Host
- Overlay
- Open / Close / Reopen
- Input modes
- ConsumerScope
- ESC lifecycle
- Page transition serialization
- HWND temporary-resolution guard

## M2 Bridge — 核心完成，进入协议收口

- Command / Request / Response / Event / State
- Owner / ConsumerScope lifecycle
- Request cancellation / AbortSignal / timeout / pagehide / shutdown
- State.Remove delete semantics
- Binding / Component lifecycle
- Error model

剩余：

- cancellable request 协议已改为独立 requestId，待实机回归
- Public API / Protocol v0.44 最终冻结

## M3 Localization / Binding — 核心完成，待组合验收

- Bannerlord Localization → Framework → Runtime → HTML
- i18n.t / locale / language list
- Dynamic i18n binding
- Dynamic DOM binding
- Template / List binding
- Component / disposer

剩余：

- Language Switch 实机验收
- 多页面 i18n / Binding 组合压力
- 长时间 Binding / Component 生命周期验证

## M4 Developer Experience — 基本完成

- API / Protocol 文档
- Consumer integration 文档
- Diagnostics
- StressLab
- Regression matrix

剩余：

- Release API / Protocol freeze
- Debug/Test surface 与正式 Framework 最终分离

## M5 Stability Hardening — 当前主线

1. WebView2 `ProcessFailed` recovery state machine
2. `EnsureUiThread` disposed-host failure semantics
3. Page / HotReload / WebView2 reinitialize 生命周期回收
4. CoreWebView2 event / Patch reinstall / uninstall 对称性
5. 多 Page / StressLab / Cancellation / i18n 组合回归

## M6 Release Candidate

- Public API / Protocol v0.44 冻结
- 默认日志低噪声
- 清理历史兼容层与测试入口
- Consumer target / branch 清理
- Release build 验证
- 实机回归矩阵全部通过

## 当前明确不做

- 不把 F12 作为正式关闭方案。
- 不恢复逐帧 Window Tracking 日志。
- 不为了稳定性审查随意重写已经验证正常的 i18n / 基础 UI 代码。
- 不在 P1 稳定性未收口前继续无关的新功能扩张。
