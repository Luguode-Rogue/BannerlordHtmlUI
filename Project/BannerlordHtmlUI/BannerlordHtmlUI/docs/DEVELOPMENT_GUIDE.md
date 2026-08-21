# Framework 开发指南

## 0. 开始开发前必须阅读

新改动先看：

1. `FRAMEWORK_MODULE_MAP.md`：每个 C# 模块到底负责什么。
2. `CODE_PLACEMENT_RULES.md`：什么代码禁止写进什么模块。
3. `ARCHITECTURE_MASTER.md`：线程、生命周期、输入、窗口的总体契约。
4. `BUG_KNOWLEDGE_BASE.md`：以前已经踩过什么坑。

**以后禁止“哪里方便就往哪里补一段”。**先找到状态 owner，再修改。

## 1. Framework 与 Consumer 分工

Framework：

- WebView2 Host
- Overlay
- Page 生命周期
- Bridge
- State
- InputMode
- Window tracking
- Browser policy
- Diagnostics
- Runtime / i18n / Binding
- 线程边界与故障恢复

Consumer：

- Bannerlord 业务逻辑
- Controller / VM / Service
- HTML/CSS/JS 资源
- Page UI
- 游戏数据转换
- Consumer hotkey
- Consumer-specific UI mode
- Command / Request handler 的业务实现

Consumer 不复制 Framework 内部 WebView2、Win32、WindowTracker、InputController 逻辑。

## 2. 新功能的归属原则

### Framework 公共能力

只有同时满足：

1. 两个以上 Consumer 都需要；或
2. 问题由 Framework 自身状态/协议错误引起；或
3. 行为必须在所有 Consumer 一致。

才允许进入 Framework。

### Consumer-specific 能力

例如：

```text
TacticalMap 右上角布局
CustomSkill 多级菜单
TacticalMap N 键
SkillUI M 键
战斗地图状态
技能配置
```

必须放 Consumer。

Framework 只能提供通用 API，例如：

```text
HtmlUiService.SetInputMode(...)
HtmlUiOverlayLayout.UseTopRight(...)
Pages.Open(...)
```

## 3. 标准接入流程

```text
找到业务入口
→ 找到 VM/Controller/Service
→ 定义 UI State
→ 创建 ConsumerScope
→ 注册 ContentRoot
→ 注册 Page
→ 注册 Command/Request/Event
→ 页面打开
→ State/Binding
→ 使用 Framework InputMode
→ 实机验证
```

## 4. 输入问题不要跨模块补丁化

统一定位顺序：

```text
页面没有输入？
  ↓
InputMode
  ↓
WebView Enabled
  ↓
Overlay PassThrough / activation
  ↓
Keyboard/Accelerator
  ↓
Consumer JS pointer/keyboard
```

对应模块分别是：

```text
InputMode              → HtmlUiInputControllerPatch
WebView Enabled        → HtmlUiInputControllerPatch
Window geometry        → HtmlUiWindowTracker
ESC/F12/browser key    → HtmlUiKeyboardAndDiagnosticsPatch
Context menu/DevTools  → HtmlUiContextMenuPatch
Consumer JS input      → Consumer
```

不得为了解决一次输入问题，同时修改以上多个模块而没有明确状态归属。

## 5. Window / Overlay

窗口问题只进入 `HtmlUiWindowTracker` / `HtmlUiOverlayLayout`。

InputController 不负责：

- 100ms timer
- WinEventHook
- Bounds calculation
- Window size tracking

PageManager 不负责：

- SetForegroundWindow
- ShowWindow
- PassThrough
- Win32 style

Consumer 不负责：

- overlay HWND
- WebView2 HWND
- Chromium child style

## 6. Browser policy

Framework 默认安全策略：

```text
Context Menu = disabled
DevTools     = disabled
Status Bar   = disabled
```

Policy 必须有唯一 owner。不要让 Host、Keyboard Patch、Consumer 各维护自己的 DevTools 开关。

F12 只是安全拦截，不是 Page close protocol。

## 7. Page 生命周期

Page open/close/reload 统一进入 `HtmlUiPageManager`。

Consumer 的 `Opened` / `Closed` 只做 Consumer 自己的资源绑定/释放，不允许在里面建立新的 Framework window/input state machine。

## 8. Bridge / async

Request handler 初始入口由 Framework 放到 GameThread。

`await` 后不保证继续位于 GameThread。

如果 continuation 需要 Bannerlord API：

```text
await external work
→ GameThreadDispatcher
→ Bannerlord API
```

禁止：

```text
Task.Run(() => Hero.MainHero ...)
```

## 9. Runtime / JS

Runtime 问题先进入对应 runtime 模块：

- State → runtime state module
- Binding → binding runtime
- i18n → i18n runtime
- Request cancellation → request runtime
- Component lifecycle → component runtime

不要在 C# Host 里用一段临时 JS patch 修 Consumer 页面问题。

## 10. 日志

正常模式低噪声。

记录：

- Framework Ready / Shutdown
- Page register/open/close
- Navigation failure
- Runtime error
- ProcessFailed
- ESC 关键链路
- 输入模式转换

不要恢复逐帧 Window Tracking 日志。

## 11. 修改前静态检查

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

确认：

1. 当前状态是不是已经有 owner。
2. 当前文件是不是正确 owner。
3. 是否存在旧 workaround。
4. 是否会形成第二套状态机。
5. 是否需要更新 Bug Knowledge / Architecture。

## 12. 修改后的最低验证

Framework 修改：

```text
Build
→ Framework-only startup
→ Page Open
→ ESC Close
→ Reopen
→ Alt+Tab
→ Window move/resize/minimize
→ Shutdown
```

Input/Overlay 修改：

```text
Hidden
Passive
Captured
MouseCaptured
→ mouse
→ keyboard
→ ESC
→ F12
→ right click
→ Alt+Tab
```

Consumer 修改：

```text
Framework baseline
→ Consumer-specific feature
```

如果 Consumer-specific 测试失败，不得为了让它通过而把 workaround 加进 Framework，除非确认 Framework 公共契约本身错误。

## 13. 不允许重复踩的规则

- 不把 `HWND=0` 当成窗口关闭。
- 不把 F12 当作关闭协议。
- 不直接从 Consumer 创建 WebView2。
- 不在错误线程访问 CoreWebView2。
- 不随机修改 Chromium child Win32 styles。
- 不用第二个 Timer 复制 WindowTracker。
- 不创建第二套 GameThread queue。
- 不让 Keyboard Patch 承担 Consumer hotkey。
- 不让 WindowTracker 修改 InputMode。
- 不让 InputController修改 Window Geometry。
- 不让 PageManager 修改焦点。
- 不以 object spread 替换带 prototype 的 Component。
- 不为了 C# 语法错误提高 LangVersion。
- 不用旧 Handoff 的版本号覆盖当前代码事实。
