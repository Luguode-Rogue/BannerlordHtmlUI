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

### ccf3231 WindowTracker 回归记录
提交 `ccf3231a97c8d26b6439f36db96e235e79994506` 曾同时修改 WindowTracker 的启动/卸载调度、`PostToUi` 调度语义、Overlay HWND exclusion，以及 Overlay foreground 判定。

该提交已经确认会改变运行时行为，并与后续“输入重新失效”回归高度相关；但**具体根因不得在未经过完整实机证据链确认前定性**。因此这里不记录“某一处单独修改就是根因”的结论。

当前处理原则：

- 出现输入重新失效时，必须比较 `ccf3231` 前后的完整 WindowTracker 行为，不得只挑一个 diff 点下结论。
- `PostToUi` 的同步/异步语义属于 WindowTracker 线程边界的一部分，修改后必须检查 `Install → StartCore → RequestSync → InputController` 的时序。
- `TryGetGameWindowHandle(...)` 的 Overlay exclusion 不能被线程修复顺手破坏。
- WindowTracker 不得读取或修改 `InputMode`；输入语义归 `HtmlUiInputControllerPatch`。

`ccf3231` 的运行时代码已在当前 `dev` 恢复到该提交之前的 WindowTracker 基线；后续如需重新引入其中任何改动，必须单独验证并记录实机回归结果。

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
