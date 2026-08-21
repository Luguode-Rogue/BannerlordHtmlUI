# Bug Knowledge Base

## 快速定位

| 症状 | 首选 owner |
|---|---|
| HWND / Window / Bounds / Minimize | `HtmlUiWindowTracker` |
| Hidden / Passive / Captured / MouseCaptured | `HtmlUiInputControllerPatch` |
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

### ccf3231 输入重新失效回归
提交 `ccf3231a97c8d26b6439f36db96e235e79994506` 是一次已确认的历史回归。该提交同时修改了 WindowTracker 的启动/卸载调度、`PostToUi` 调度语义、Overlay HWND exclusion，以及 Overlay foreground 判定。

已实机确认的症状：

```text
页面请求 Captured
→ Framework 日志显示 InputMode=Captured / INPUT_MODE_APPLIED
→ Bannerlord 仍收到 LeftMouseButton / Escape
```

并可伴随 WindowTracker 的：

```text
GAME HWND = 0
Foreground / Visible / Bounds 异常
```

因此这里的 Bug 定义是：

> **Framework 的 InputMode 状态与 native Overlay/WebView 的实际输入所有权脱节，导致模式显示为 Captured 时，Bannerlord 仍收到输入。**

### 错误结论撤销
历史上曾错误地把以下单项变化直接认定为唯一根因：

- `TryGetGameWindowHandle(form.Handle, ...) → IntPtr.Zero`
- `PostToUi()` 无条件 `BeginInvoke`
- 某一次 `Captured` 激活条件

这些都属于 `ccf3231` 的实际行为变化，但目前实机证据不足以证明其中任一项单独就是唯一根因。

正确的排查链必须整体检查：

```text
Install
→ StartCore
→ RequestSync
→ WindowTracker.SyncNow
→ InputController.ApplyRequestedMode
→ OverlayForm native state
→ Foreground / Owner / Bounds
→ WebView focus / hit-test
→ 实际 GAME_INPUT / WebView input
```

### 本 Bug 的硬性回归判定
仅出现：

```text
INPUT_MODE_APPLIED mode=Captured
```

不能判定修复成功。必须同时满足：

```text
Captured
→ Overlay 实际成为输入目标
→ WebView 实际收到鼠标/键盘
→ Bannerlord 不再收到对应输入
```

### 已确认的模块边界

- `HtmlUiInputControllerPatch`：唯一负责 Hidden / Passive / Captured / MouseCaptured 的输入语义、native input ownership、Overlay activation/focus。
- `HtmlUiWindowTracker`：只负责 Bannerlord HWND、Foreground、Visible、Bounds、Minimize 等窗口事实；不得读取或修改 `InputMode`。
- `HtmlUiOverlayForm`：只负责 WinForms/native window 行为，例如 PassThrough、NoActivate、WM_NCHITTEST、WM_MOUSEACTIVATE。
- `Win32`：只提供底层 native API 封装，不决定业务输入模式。
- ZZZF Consumer：只声明业务需要的 `HtmlUiInputMode`，不得自行添加第二套输入拦截/焦点/Win32 逻辑。

### `ccf3231` 后续处理原则
`ccf3231` 之后重新引入任何 WindowTracker 调度/HWND 行为，都必须单独验证，不能和 InputController 的输入修复混为同一改动。

`PostToUi` 的线程语义必须明确验证；`TryGetGameWindowHandle(...)` 的 Overlay exclusion 必须保留；而输入问题必须最终落在 InputController + OverlayForm 的实际 native 行为上，而不是只看 Framework 状态字段。

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

Bug 历史统一记录在本文件；同一 Bug 不重复写入其他项目主文档。
