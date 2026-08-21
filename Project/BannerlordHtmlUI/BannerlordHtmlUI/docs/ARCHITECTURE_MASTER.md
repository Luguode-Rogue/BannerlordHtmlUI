# BannerlordHtmlUI 当前架构与代码归属

> 当前 Framework 架构唯一规范。历史架构记录保留在 Handoff/CHANGELOG。
> 当前开发线：`dev`；版本以 `BannerlordHtmlUI.csproj` 为准。

## 1. Framework 定位

Framework 只提供通用 HTML/WebView2 UI 基础设施；Consumer Mod 负责游戏业务、Controller/VM、页面和业务状态。

```text
Bannerlord Game Thread
        ↓
HtmlUiService
        ├── GameThreadDispatcher
        ├── PageManager
        ├── StateStore
        ├── ConsumerScope
        └── HtmlUiHost
              ├── WebView2 UI Thread
              ├── Bridge
              ├── WindowTracker
              ├── InputController
              ├── OverlayForm
              ├── Browser Policy
              ├── Runtime Patches
              └── Recovery
```

## 2. 唯一状态 Owner

| 模块 | 唯一职责 | 禁止代替谁 |
|---|---|---|
| `HtmlUiService` | Framework facade、Ready/Shutdown、公共 API 聚合 | 不写 Win32/Input/Page 业务 |
| `GameThreadDispatcher` | WebView/async → Bannerlord GameThread | 不写业务、不操作 WebView2 |
| `HtmlUiHost` | WebView2/WinForms 宿主、UI thread、Navigation、资源映射 | 不实现 Consumer 状态机 |
| `HtmlUiPageManager` | Page Register/Open/Close/Reload、Current、Navigation race | 不处理焦点/Win32 |
| `HtmlUiBridge` | Command/Request/Event/Response、Cancellation、Owner identity | 不处理 Window/Input/Page UI |
| `HtmlUiStateStore` | C# State source of truth、Set/Get/Remove/Snapshot | 不实现 Page/Input 状态机 |
| `HtmlUiConsumerScope` | Consumer-owned Page/Command/Request/State/ContentRoot 生命周期 | 不改 Framework 内部状态 |
| `HtmlUiWindowTracker` | HWND、Foreground、Visible、Minimized、Overlay Bounds | 不改 InputMode/ESC/Page |
| `HtmlUiOverlayLayout` | `FullWindow` / `TopRight` 等通用布局意图 | 不处理输入/Page |
| `HtmlUiInputControllerPatch` | `Hidden/Passive/Captured/MouseCaptured`、WebView enable、PassThrough、focus | 不算窗口几何/Consumer 热键 |
| `HtmlUiOverlayForm` | 原生 Form/Owner/NoActivate/PassThrough/native message | 不做业务状态机 |
| `HtmlUiKeyboardAndDiagnosticsPatch` | Framework ESC/F12/browser accelerator safety + diagnostics | 不处理 M/N/N 等 Consumer 热键 |
| `HtmlUiContextMenuPatch` | 浏览器菜单/DevTools/Status bar policy | 不处理 Input/Page |
| Runtime Patch 系列 | Binding、i18n、State bootstrap/remove、Error、Cancellation、Navigation、Recovery | 不固化 Consumer workaround |

### 核心边界

```text
WindowTracker  = 窗口在哪里/是什么状态
InputController = HTML 是否拥有输入
PageManager    = 当前 Page 是谁/如何切换
Bridge         = C# ↔ JS 怎么通信
StateStore     = C# 状态是什么
Consumer       = 具体游戏业务是什么
```

## 3. 输入规则

Framework 的唯一 InputMode owner 是 `HtmlUiInputControllerPatch`：

```text
Hidden         页面不可见，不接收 HTML 输入
Passive        页面可见，但 HTML 完全不拥有输入
Captured       HTML 拥有输入
MouseCaptured  HTML 拥有鼠标输入
```

Keyboard Patch 只能做安全兜底；WindowTracker 不得修改 InputMode；Consumer 不得建立 Framework 输入 Patch。

## 4. Browser Policy

默认：

```text
Context Menu = disabled
DevTools/F12 = disabled
Status Bar   = disabled
```

Browser policy 必须有唯一 owner。禁止 Host、Keyboard Patch、Consumer 各自实现一套 DevTools/右键开关。

## 5. Window / Overlay

`HWND = 0` 表示暂时无法解析窗口，不等于游戏退出；不得仅凭 HWND=0 隐藏正在显示的 UI。

禁止恢复新的 100ms window polling。旧 `FollowTimer` 属于遗留代码，只能删除/兼容，不能复制。

禁止通过 `Chrome_RenderWidgetHostHWND + WS_EX_TRANSPARENT` 等随机 child-window style 解决渲染问题。

## 6. 三个执行域

```text
Bannerlord Game Thread
        ↓
Framework C#
        ↓ marshal
WebView2 UI Thread
        ↓
Chromium / JavaScript Runtime
```

规则：

1. `CoreWebView2` 只能在 WebView2 UI thread 使用。
2. Bannerlord Game API 只能在 GameThread 使用。
3. Request `await` 后不保证仍在 GameThread；需要 Game API 时显式回 GameThread。
4. 同一线程边界不得新增第二套 `Task.Run` / `Invoke` / queue 体系。

## 7. Page 生命周期

```text
Framework Startup
 → WebView2 Ready
 → Consumer Register
 → Page Open
 → Navigation
 → Runtime Bootstrap
 → Active
 → Page Close
 → Owner cleanup / Request cancellation
```

旧 Navigation/async 结果不得覆盖新页面状态。

## 8. Framework 修改硬规则

### 8.1 先找 owner

修改前必须回答：

> 谁拥有这个状态？谁应该负责这个副作用？

找不到 owner 时先拆职责，不得把代码塞进最方便修改的文件。

### 8.2 Consumer-specific 必须留在 Consumer

例如：

```text
TacticalMap 右上角布局
TacticalMap N 键
CustomSkill M 键
技能业务状态
战斗地图业务状态
```

Framework 只能提供通用 API，例如：

```csharp
HtmlUiOverlayLayout.UseTopRight(...);
HtmlUiService.SetInputMode(...);
HtmlUiService.Pages.Open(...);
```

Framework 禁止出现 `TacticalMap` / `CustomSkill` 等 Consumer 名称判断。

### 8.3 禁止跨区修 Bug

```text
Window 问题       → WindowTracker / OverlayLayout
InputMode 问题    → InputController
ESC/F12           → Keyboard safety
右键/DevTools     → Browser policy
Page/Open/Close   → PageManager
Request/Cancelling→ Bridge
State             → StateStore
GameThread queue  → Dispatcher
Consumer 业务     → Consumer
```

如果两个模块同时修改同一个状态：先合并 owner，再修 Bug；禁止继续增加第三个补丁点。

### 8.4 修改前静态检查

至少搜索：

```text
SetForegroundWindow
ShowWindow
BeginInvoke
Invoke
Timer
InputMode
CloseCurrent
NavigationCompleted
CoreWebView2
Harmony
```

同时搜索旧 workaround 与 Consumer ID。

### 8.5 修改后的最低回归

Framework：

```text
Build
→ Framework-only startup
→ Open
→ ESC
→ Reopen
→ Alt+Tab
→ Move/Resize/Minimize
→ Shutdown
```

Input/Overlay：

```text
Hidden / Passive / Captured / MouseCaptured
→ mouse / keyboard / ESC / F12 / right click / Alt+Tab
```

Consumer：先确认 Framework baseline，再测试 Consumer-specific 行为。

## 9. Failure Model

必须区分：

- Page not registered
- ContentRoot missing
- Navigation race
- Runtime not ready
- Request cancellation/timeout
- Owner disposed
- ProcessFailed
- Framework shutdown
- Game HWND temporarily unavailable

不要通过“再加一个全局 Patch”掩盖错误的状态 owner。
