# 当前项目状态

> 当前开发线：`dev`
> Framework 工程版本来源：`BannerlordHtmlUI.csproj` 中的 `<Version>`，当前为 **0.44.0**。
> 本文是当前状态的唯一状态入口；旧 Handoff 中的版本号、里程碑结论仅作为历史记录。

## 项目阶段

Framework 主体已经成型，当前阶段是 **Stability Hardening / API Stabilization / Release Regression**，重点是结构性风险收口而不是继续无边界增加功能。

## 当前已完成能力

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

待验收：

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

待收口：

- v0.44 Public API / Protocol 最终冻结

## M5 Consumer / Diagnostics

已完成：

- TestMod API 覆盖
- Diagnostics 注册数与 ActiveRequestCount
- StressLab
- F8 StressLab / F7 Close
- M5 回归矩阵

待验收：

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

待验收：

- 高频 State/Event 压力
- StressLab 长时间运行
- 大量 DOM Binding / Component
- 多 Page 快速切换/Reload 长时间验证
- 压力测试结果与泄漏基线冻结

## 当前高优先级风险

### P0

1. **MainWindowHandle 临时为 0 时不得错误隐藏正在显示的 UI。** `HWND = 0` 必须区别于真正 Framework shutdown / game exit。
2. **ESC UI-thread close 链必须实机完成验证。** 至少要有 filter installed → Escape detected → CloseCurrent → `currentPage=<null>` / `inputMode=Hidden` / `hostVisible=False` 的证据链。
3. **WebView2 ProcessFailed 必须有明确 recovery / safe shutdown 策略。** 不允许进入 silent death。

### P1

- PageManager transition race 的长期压力验证
- State Remove 的 delete 语义完整回归
- ConsumerScope owned active Request 清理
- `preCanceledRequests` 长期生命周期
- cancellable request 的 requestId 捕获竞态
- HotReload / Page close 回收完整性
- CoreWebView2 事件重建/解绑
- `EnsureUiThread` disposed 状态行为
- keyboard/message filter install/uninstall 对称性

### P2

- Consumer net6 target 是否仍有实际用途
- `HtmlUiHostCancellableExtensions` 是否可以并回正式 API
- diagnostics/testing hooks 是否需要继续留在 Framework
- release build test surface 清理
- 发布前分支关系与发布基线核对

## 实机回归优先顺序

```text
F11 → 页面完全显示 → ESC
F11 → 切换/关闭/再次打开
F8 → StressLab → Run 10
StressLab 长时间 Run 50
Page Reload / Binding / Component 压力
Framework shutdown / game exit
```

## 状态判定规则

静态代码审计不能替代 Bannerlord 实机验收。尤其是 WebView2、Overlay、Input Focus、Navigation Timing、长时间运行资源回落，必须在实际游戏环境验证后才能标记通过。

对于状态冲突：

```text
当前代码 / 当前 csproj
        ↓
最近一次真实运行验证
        ↓
docs/PROJECT_STATUS.md
        ↓
旧 Handoff / 旧 Changelog（历史证据）
```

不得用旧 Handoff 的阶段性版本号覆盖当前代码真实版本。