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

### 100ms Window Follow
历史 `FollowBannerlordWindow()` 同时控制位置、可见性、焦点，制造状态覆盖与卡死风险。窗口同步统一使用 Event-driven `HtmlUiWindowTracker`；禁止恢复第二个 Timer。

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
