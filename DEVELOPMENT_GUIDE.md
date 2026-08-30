# BannerlordHtmlUI 开发规则

## 开发前必须做

1. 先看 `ARCHITECTURE_MASTER.md`，找状态 owner。
2. 再看 `BUG_KNOWLEDGE_BASE.md`，检查历史失败方案。
3. 再看当前代码和回归矩阵。
4. 如果当前版本异常，先找最近的**实测稳定基线**，再做 commit-level regression；不要直接从当前坏状态继续打补丁。

禁止“哪里方便就往哪里补一段”。

## 稳定基线与继续开发策略

当发现当前代码存在公共功能回归时，先判断是否应该从稳定版本重新演进：

```text
稳定版本已实机验证
        ↓
当前版本发生回归
        ↓
比较稳定版本 → 当前版本
        ↓
找到首个行为回归提交
        ↓
确认该提交的原始目的
        ↓
保留必要目的，撤销错误副作用
        ↓
从稳定语义重新合并必要改动
```

只有在满足以下条件时，才继续直接修当前版本：

```text
1. 回归点已经明确；
2. 当前版本的后续改动依赖该回归点；
3. 从稳定版本重新整理会丢失已经验证过的必要功能；
4. 可以在同一 owner / 同一区域内完成最小修复。
```

如果无法证明以上条件，应优先从稳定基线重新整理，而不是在坏版本上继续叠补丁。

## 历史修改意图必须保留

任何影响公共行为的代码修改，都必须能够回答：

```text
为什么当时要改？
解决什么已观察问题？
依赖什么前置假设？
是否经过实机验证？
后来是否被回滚？为什么？
```

提交信息、Bug 知识库或开发文档至少保留“原始目的”和“已知副作用”。

禁止仅根据当前代码形态推断历史意图；应先查看对应 commit message、diff 和当时的 Bug/交接记录。

## Framework / Consumer 分工

Framework 负责：

```text
WebView2 Host
Overlay
Page lifecycle
Bridge
State
InputMode
Window tracking
Browser policy
Diagnostics
Runtime / Binding / i18n
线程边界
Recovery
```

Consumer 负责：

```text
Bannerlord 游戏业务
Controller / VM / Service
HTML / CSS / JS
游戏数据
Consumer hotkey
Consumer-specific UI mode
业务 Command / Request
```

只有公共语义、多个 Consumer 需要，或明确属于 Framework 自身错误，才进入 Framework。

## 禁止跨区修 Bug

```text
Window / Bounds        → WindowTracker / OverlayLayout
InputMode              → InputController
ESC / F12 safety       → Keyboard safety
Right-click / DevTools → Browser policy
Page Open/Close        → PageManager
Request/Cancellation   → Bridge
State                  → StateStore
GameThread queue       → Dispatcher
Consumer business      → Consumer
```

如果两个模块同时修改同一状态，先合并 owner，禁止继续增加第三个补丁点。

Framework 禁止判断 `TacticalMap` / `CustomSkill`；Consumer 禁止创建 Framework 输入/Win32 Patch 或直接管理 WebView2 HWND。

## Window / Input

InputMode 唯一 owner：`HtmlUiInputControllerPatch`。

WindowTracker 只提供 HWND、Foreground、Visible、Minimized、Bounds 等事实；不能修改 InputMode。

## Threading

只允许现有 GameThread ↔ WebView2 UI thread 边界；禁止另起第二套 queue / Task.Run / Invoke 架构。

Request `await` 后需要 Bannerlord API 时显式回 GameThread。

## Browser policy

默认：

```text
Context Menu = disabled
DevTools     = disabled
Status Bar   = disabled
```

必须保持单一 owner。

## 修改前搜索

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

确认没有旧 workaround、第二套状态机或错误 owner 副作用。

## 最低回归

```text
Framework-only startup
→ Open
→ ESC
→ Reopen
→ Alt+Tab
→ Move/Resize/Minimize
→ Shutdown
```

输入：

```text
Hidden / Passive / Captured / MouseCaptured
→ mouse / keyboard / ESC / F12 / right click / Alt+Tab
```

Consumer 修改前先确认 Framework baseline，再做 Consumer-specific 回归。
