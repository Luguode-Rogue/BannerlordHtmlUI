# Bug Knowledge Base

## 快速定位

| 症状 | 首选 owner |
|---|---|
| HWND / Window / Bounds / Minimize | `HtmlUiWindowTracker` |
| Hidden / Passive / Captured | `HtmlUiInputControllerPatch` |
| ESC / F12 safety | `HtmlUiKeyboardAndDiagnosticsPatch` |
| Right-click / DevTools | Browser policy |
| Page Open/Close/Reload | `HtmlUiPageManager` |
| Request / Cancellation / Owner | `HtmlUiBridge` |
| State | `HtmlUiStateStore` |
| GameThread queue | `GameThreadDispatcher` |
| TacticalMap-specific | `New_ZZZF.TacticalMap` |
| CustomSkill-specific | `New_ZZZF.CustomSkill` |

## 已确认规则

### HWND = 0
临时无法解析 Bannerlord HWND 不等于游戏退出。不得因此直接 Hide 正在显示的 UI。

### WindowTracker HWND 排除条件
`BannerlordHtmlUI` Overlay 与 Bannerlord 游戏窗口属于同一进程。调用 `Win32.TryGetGameWindowHandle(...)` 时，必须排除当前 Overlay HWND；不能为了线程封送或简化调用而把 `form.Handle` 改成 `IntPtr.Zero`。

历史回归：`ccf3231a97c8d26b6439f36db96e235e79994506` 将 `TryGetGameWindowHandle(form.Handle, ...)` 改为 `TryGetGameWindowHandle(IntPtr.Zero, ...)`。这样 Overlay HWND 可能被同进程窗口枚举 / foreground / main-window fallback 误认为 Bannerlord 游戏窗口，导致 WindowTracker 建立在错误 HWND 上，出现 `game=0`、异常 Bounds、Overlay 可见性/Foreground 状态错误，以及后续输入重新失效。修复为 `41b5da2940da2e7e5741c7aaef96045b1585c9de`：恢复 Overlay HWND exclusion，同时保留 UI-thread marshal。

结论：线程问题应通过正确的 UI-thread 边界解决，不得破坏 HWND candidate exclusion 作为副作用。

### Passive
`Passive` = 可见但 HTML 完全不拥有输入。只读 Consumer 使用 Framework Passive，不增加 Consumer Harmony 输入 Patch。

### F12 / DevTools / Right-click
Browser policy 默认禁止右键菜单和 DevTools。Keyboard Patch 只作安全兜底；F12 不是页面关闭协议。

### ESC
页面是否允许 ESC 由 `HtmlUiPage.CloseOnEscape` 决定；安全过滤属于 Framework。

### UI Thread
`CoreWebView2` 只能由 WebView2 UI thread 使用。Runtime-dependent Patch 不得从 GameThread 重复安装。

### Overlay rendering
不要通过 `Chrome_RenderWidgetHostHWND` 随机增加 `WS_EX_TRANSPARENT` 等 child-window extended style 解决输入/渲染问题；历史实机已经证明可能导致“看不见但能点”。

### Navigation Race
快速 Open/Close/Reload 时旧 Navigation/async completion 不得覆盖新页面状态；归属 PageManager + Navigation guard。

### Bridge cancellation
Shutdown/Owner dispose/pagehide/timeout/abort 后晚到成功结果不得覆盖新请求。Owner 清理必须同步取消自己的 active cancellable requests。

### Binding / Component
不要使用 object spread 替换 Component；保留 prototype、Symbol、non-enumerable 成员。所有 observer/listener/timer/request/component 都必须有 disposer。

## 核心修复规则

```text
先找状态 owner
→ 查历史失败方案
→ 修改唯一 owner
→ 检查旧 workaround / 第二套状态机
→ 做对应回归
```

如果两个模块都在修改同一个状态：停止继续打补丁，先拆职责。

完整历史现场保留在 `Handoff/`；这里仅维护可复用结论。
