# 当前项目状态

> 基线：`dev`
> 当前版本文档目标：v0.44

## 项目阶段

Framework 主体已经成型，当前阶段不是继续堆新功能，而是 **Stability Hardening / API Stabilization / Release Regression**。

## 已完成核心能力

- WebView2 UI thread 初始化
- Framework Ready / Shutdown
- Page / ContentRoot
- Command / Request / Response / Event / State
- Request cancellation / AbortSignal / timeout / pagehide / runtime shutdown
- Consumer owner scope
- Binding / Component 生命周期基础设施
- i18n 基础链路
- Navigation race guard
- Overlay 与 Captured Input 基线
- Diagnostics
- Consumer TestMod + StressLab

## M3 Binding / Localization

已完成：

- `i18n.bind()` root 级幂等与 disposer
- 动态 DOM 新增/删除绑定
- 连续 DOM mutation microtask 合并
- debounce/throttle 生命周期隔离
- Component child 生命周期接入 Binder
- pagehide 自动 dispose
- State bootstrap hydration 重试

未完成：

- Language Switch 实机验收
- Binding locale refresh/disposal 压力验证
- Template/List 长期运行与 key diff 验收
- 大量 DOM Binding 性能检查

## M4 API / Protocol

已完成：

- Command 基础成功/错误语义
- Owner / Scope 生命周期
- owner + entry identity 竞态防护
- 稳定 JS Error model
- timeout / AbortSignal / pagehide / runtime shutdown cancellation
- Page / ContentRoot / Reload 安全边界初步审计
- `.d.ts` 与 Runtime API 同步审计

未完成：

- 冻结 v0.44 Public API / Protocol 合同

## M5 Consumer / Diagnostics

已完成：

- TestMod API 覆盖
- Diagnostics 注册数与 ActiveRequestCount
- StressLab
- F8 StressLab / F7 Close
- M5 回归矩阵

未完成：

- StressLab 实机验证
- 一键 smoke/lifecycle 回归完全覆盖

## M6 Stability / Performance

已完成：

- NavigationId Guard
- Request ActiveRequestCount
- Runtime shutdown cancellation
- Binding / Component / timer / observer 静态收口
- GameThread → async continuation → WebView2 UI thread 边界审计
- StressLab 基础压力项

未完成：

- 高频 State/Event 压力
- StressLab 长时间运行
- 大量 DOM Binding / Component
- 多 Page 快速切换/Reload 长时间验证
- 压力测试结果与泄漏基线冻结

## M7 Release Baseline

未完成：

- 清理 debug/实验开关
- Overlay/WebView2 回归矩阵冻结
- 发布版低噪声日志
- API / Protocol / Consumer 文档收口
- v0.44 release checklist 完成

## 当前 P0

1. MainWindowHandle 临时为 0 时不得错误隐藏正在显示的 UI。
2. ESC 必须使用 Framework UI-thread close filter，并有完整日志证据链。
3. WebView2 ProcessFailed 必须有明确 recovery / safe shutdown 策略。

## 当前 P1

- PageManager transition race
- State Remove 的 delete 语义
- ConsumerScope owned active Request 清理
- `preCanceledRequests` 生命周期
- cancellable request requestId 捕获竞态
- HotReload / Page close 回收完整性
- CoreWebView2 事件重建/解绑
- EnsureUiThread disposed 状态
- keyboard/message filter install/uninstall 对称性

## 当前 P2

- Consumer net6 target 是否保留
- `HtmlUiHostCancellableExtensions` 是否合并回正式 API
- diagnostics/testing hooks 是否继续留在 Framework
- release build test surface 清理
- main/trunk/dev 分支收口

## 实机回归优先顺序

```text
F11 → 页面完全显示 → ESC
F11 → 切换/关闭/再次打开
F8 → StressLab → Run 10
StressLab 长时间 Run 50
Page Reload / Binding / Component 压力
Framework shutdown / game exit
```
