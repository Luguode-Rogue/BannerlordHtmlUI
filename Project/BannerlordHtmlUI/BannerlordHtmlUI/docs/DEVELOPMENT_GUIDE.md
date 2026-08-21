# Framework 开发指南

## 开发前

先看：

1. `ARCHITECTURE_MASTER.md`：当前模块职责、线程、输入、窗口和代码归属硬规则。
2. `BUG_KNOWLEDGE_BASE.md`：历史 Bug、失败方案和定位入口。
3. `API.md`：公共 API 与 Consumer 契约。

禁止“哪里方便就往哪里补一段”。先找状态 owner。

## Framework 与 Consumer

Framework：WebView2 Host、Overlay、Page lifecycle、Bridge、State、InputMode、Window tracking、Browser policy、Diagnostics、Runtime/i18n/Binding、线程边界、Recovery。

Consumer：Bannerlord 业务、Controller/VM/Service、HTML/CSS/JS、游戏数据、Consumer hotkey、Consumer-specific UI mode、Command/Request 业务 handler。

只有公共语义、两个以上 Consumer 需要，或明确是 Framework 自身错误，才进入 Framework。

## Consumer-specific 规则

例如：

```text
TacticalMap 右上角布局
TacticalMap N 键
CustomSkill M 键
技能/战斗地图业务状态
```

必须留在 Consumer。

Framework 只提供通用 API：

```text
HtmlUiService.SetInputMode(...)
HtmlUiOverlayLayout.UseTopRight(...)
HtmlUiService.Pages.Open(...)
```

## 输入问题

```text
InputMode
→ WebView Enabled
→ Overlay PassThrough / activation
→ Browser Accelerator
→ Consumer JS input
```

对应 owner：

```text
InputMode / WebView Enabled → InputController
Window geometry             → WindowTracker / OverlayLayout
ESC/F12                     → Keyboard safety
Context Menu / DevTools     → Browser policy
Consumer JS                 → Consumer
```

不要同时修改多个 owner 来解决一个没有先定位的输入问题。

## Window / Overlay

Window 几何问题进 `HtmlUiWindowTracker` / `HtmlUiOverlayLayout`。

InputController 不实现 WinEvent、Bounds、100ms polling。
PageManager 不处理 Focus、PassThrough、Win32 style。
Consumer 不碰 Overlay/WebView2 HWND。

## Page / Bridge / Runtime

Page Open/Close/Reload 统一进入 PageManager。

Request handler 进入 GameThread；`await` 后不保证仍在 GameThread，需要 Game API 时显式回 Dispatcher。

Runtime 问题进入对应 Runtime 模块，不要在 Host 中写一次性 JS workaround。

## Browser policy

默认：

```text
Context Menu = disabled
DevTools     = disabled
Status Bar   = disabled
```

必须保持单一 policy owner。F12 是安全拦截，不是 Page close protocol。

## 日志

正常模式低噪声。记录 Ready/Shutdown、Page lifecycle、Navigation failure、Runtime error、ProcessFailed、ESC/input-mode 关键转换；不要恢复逐帧 Window Tracking。

## 修改前检查

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

确认：

1. 状态 owner 是否唯一。
2. 当前文件是否为正确 owner。
3. 是否存在旧 workaround。
4. 是否形成第二套状态机。
5. 是否需要同步 Bug Knowledge / Architecture / API。

## 最低验证

Framework：

```text
Build → Framework-only startup → Open → ESC → Reopen → Alt+Tab → Window move/resize/minimize → Shutdown
```

Input/Overlay：

```text
Hidden / Passive / Captured / MouseCaptured
→ mouse / keyboard / ESC / F12 / right click / Alt+Tab
```

Consumer 修改先确认 Framework baseline，再做 Consumer-specific 回归。
