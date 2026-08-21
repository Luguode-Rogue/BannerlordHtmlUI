# BannerlordHtmlUI 当前架构与代码归属

> 当前 Framework 架构唯一规范。当前开发线：`dev`。

## Framework 与 Consumer

Framework 只提供通用 HTML/WebView2 UI 基础设施；Consumer Mod 负责 Bannerlord 游戏业务、Controller/VM、页面和业务状态。

## 唯一状态 Owner

| 模块 | 唯一职责 |
|---|---|
| `HtmlUiService` | Framework facade、Ready/Shutdown、公共 API 聚合 |
| `GameThreadDispatcher` | async/WebView → Bannerlord GameThread |
| `HtmlUiHost` | WebView2/WinForms 宿主、UI thread、导航、资源映射 |
| `HtmlUiPageManager` | Page Register/Open/Close/Reload、Current、导航竞态 |
| `HtmlUiBridge` | Command/Request/Event/Response、Cancellation、Owner |
| `HtmlUiStateStore` | C# State source of truth |
| `HtmlUiConsumerScope` | Consumer 资源生命周期 |
| `HtmlUiWindowTracker` | HWND、Foreground、Visible、Minimized、Overlay Bounds |
| `HtmlUiOverlayLayout` | FullWindow / TopRight 等通用布局 |
| `HtmlUiInputControllerPatch` | Hidden / Passive / Captured / MouseCaptured、WebView enable、PassThrough、focus |
| `HtmlUiOverlayForm` | 原生 Form、Owner、NoActivate、PassThrough、native message |
| `HtmlUiKeyboardAndDiagnosticsPatch` | Framework ESC/F12 safety 与诊断 |
| Browser policy | 右键菜单、DevTools、Status bar |
| Runtime Patch 系列 | Binding、i18n、State bootstrap/remove、Error、Cancellation、Navigation、Recovery |

核心边界：

```text
WindowTracker   = 窗口在哪里/是什么状态
InputController = HTML 是否拥有输入
PageManager     = 当前 Page 是谁/如何切换
Bridge          = C# ↔ JS 怎么通信
StateStore      = C# 状态是什么
Consumer        = 具体游戏业务是什么
```

## 硬性代码归属

```text
Window / Bounds / Minimize  → WindowTracker / OverlayLayout
InputMode / WebView input   → InputController
ESC/F12 safety              → Keyboard safety
Right-click / DevTools      → Browser policy
Page Open/Close/Reload      → PageManager
Request/Cancellation/Owner  → Bridge
State                       → StateStore
GameThread queue            → Dispatcher
Consumer business           → Consumer
```

如果两个模块同时修改同一个状态：先拆职责、确定唯一 owner；禁止继续增加第三个补丁点。

Framework 禁止出现 `TacticalMap` / `CustomSkill` 等 Consumer 名称判断。Consumer 不得自行管理 WebView2 HWND 或建立 Framework 输入 Patch。

## 执行域

```text
Bannerlord Game Thread
        ↓ marshal
Framework C#
        ↓ marshal
WebView2 UI Thread
        ↓
Chromium / JavaScript
```

`CoreWebView2` 只能在 WebView2 UI thread 使用；Bannerlord API 只能在 GameThread。Request `await` 后如需 Game API，必须显式回 Dispatcher。

## 输入语义

```text
Hidden         页面不可见，不接收 HTML 输入
Passive        页面可见，但 HTML 完全不拥有输入
Captured       HTML 拥有输入
MouseCaptured  HTML 拥有鼠标输入
```

唯一 InputMode owner：`HtmlUiInputControllerPatch`。

## Browser policy

默认：

```text
Context Menu = disabled
DevTools/F12 = disabled
Status Bar   = disabled
```

Browser policy 只有一个 owner；F12 是安全兜底，不是页面关闭协议。

## 严禁重新引入

- 新的 100ms window polling / FollowTimer
- Chromium 子窗口 `Chrome_RenderWidgetHostHWND` 的随机 extended-style 实验
- Consumer 专用的 Framework Patch
- 第二套线程 queue / Task.Run / Invoke 体系
- 为修一个状态而在错误 owner 中添加副作用

## 修改前强制检查

搜索：

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

然后确认状态 owner、旧 workaround、第二套状态机和对应回归项。
