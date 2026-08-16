# M3 / M4 Framework 收口记录

日期：2026-08-16
分支：`feature/framework-finalization-m3-m4`
基线：当前 `dev`

## 本轮目标

继续把 BannerlordHtmlUI 从“核心能力可用”收口到“可以稳定作为具体 UI 项目的基础框架”。本轮优先处理 M3 Binding 生命周期中已经能从代码审计确认的资源泄漏点，并建立对应验收记录。

## 本轮发现的实际缺口

### 1. `bind.component()` 没有进入 Binder 的统一 Dispose 生命周期

`createBinder()` 原实现返回 `component()` 对象后，没有把其 `dispose()` 注册到 Binder 的 `subscriptions` 中。

结果：

```text
game.bind.component(...)
        ↓
返回 component handle
        ↓
页面 / Binder Dispose
        ↓
State listener 可以被清理
但 component 本身可能继续持有 DOM / child disposer
```

这与项目既有的 Binder lifecycle 设计不一致。

### 2. `bind.list()` / `bind.template()` 的返回 disposer没有纳入 Binder Dispose

两者原实现只把 State listener 的 `off` 放入 Binder 的 `subscriptions`：

```text
Binder.Dispose()
    ↓
解除 State listener
    ↓
没有自动执行 list/template 返回的 clear()
```

因此当前页面被销毁、Reload 或 `game.bind.dispose()` 时，已经创建的 child DOM 和 child disposer 不能保证完整回收。

## 最终修复

新增：

`src/HtmlUiBindingLifecyclePatch.cs`

该 Patch 不重写 Runtime Core，而是在 WebView2 DocumentCreated 阶段对 Binder 做生命周期增强：

```text
runtime-core 创建 Binder
        ↓
BindingLifecyclePatch 包装 Binder
        ↓
list() 返回 disposer → 自动纳入 Binder Dispose
        ↓
template() 返回 disposer → 自动纳入 Binder Dispose
        ↓
component() handle.dispose → 自动纳入 Binder Dispose
        ↓
Binder.Dispose()
        ↓
所有 managed child disposer
        ↓
原 Binder.Dispose()
```

同时覆盖：

- `game.bind`
- `game.app.bind`
- `game.scope(...).bind`
- `pagehide` 自动 Dispose 当前页面已注册的 Binder

修复采用外围 Patch，不修改已冻结的 `runtime-core.js`，降低 Runtime 核心回归风险。

## 为什么采用 Patch，而不是直接继续扩大 Runtime Core

Runtime Core 当前承担 Request / State / Binding / Scope / Lifecycle 等多条核心链路，并已经经过多轮稳定性修正。本轮缺口属于生命周期补强，不需要重新重构 Runtime Core。

因此采用：

```text
Runtime Core = 稳定核心
外围 Patch = 生命周期增强
```

保持之后继续模块化与 API 冻结时的边界清晰。

## 当前 M3 状态

已具备：

- 普通 text/value/checked/disabled/hidden/visible/class/attr binding
- two-way binding
- debounce / throttle
- list diff / key reuse
- template
- component
- delegate / events
- 动态 i18n DOM binding
- MutationObserver 生命周期
- Binder disposer
- pagehide cleanup
- component/list/template child disposer 纳入 Binder 生命周期（本轮修复）

仍需实机验收：

- Language Switch 后完整 DOM 自动刷新
- 大量 DOM Binding
- Template / List 长时间运行
- 多次 Reload / Page 切换后的 disposer 回收
- StressLab 长时间运行后的注册数、PageCount、StateCount、ActiveRequestCount 回落

## M4 当前状态

已有：

- Command / Request / Response / Event / State 协议基础
- owner / scope lifecycle
- cancellation / AbortSignal
- pagehide / runtime shutdown cancellation
- ContentRoot / Page path 安全边界
- Runtime / TypeScript `.d.ts` API 同步
- Protocol v1

仍需最终冻结：

- v0.44 Public API contract
- Protocol error / timeout / cancellation 的最终稳定语义
- disposer 与 ownership 的最终约定
- 对外文档与 Golden Consumer 示例统一

## 与正式 UI 开发的关系

本轮完成后，Framework 的核心能力已经达到可以承载真实 Consumer UI 的阶段。剩余工作主要是：

```text
代码缺口收口
    ↓
实机回归
    ↓
API / Protocol 冻结
    ↓
StressLab 长测
    ↓
Release Baseline
```

而不是继续新增基础 UI 能力。
