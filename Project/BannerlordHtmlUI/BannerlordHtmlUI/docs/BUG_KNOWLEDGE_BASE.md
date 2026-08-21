# Bug 修复经验库

> 本文档是统一检索入口，不替代原始 Bug 复盘。原始 Handoff/Postmortem 文件必须继续保留完整排错过程。
>
> **当前代码归属规则**：遇到 Bug 先查 `FRAMEWORK_MODULE_MAP.md` 与 `CODE_PLACEMENT_RULES.md`，先进入真正拥有该状态的模块；禁止把 Consumer workaround 塞进 Framework。

## 1. 生命周期 / 输入 / 窗口

### MainWindowHandle 临时变为 0

**现象**：页面已经打开并处于 Captured 模式，随后出现 `Bannerlord main window could not be resolved. hwnd=0`，旧逻辑可能把 Overlay 隐藏或退出。

**结论**：`HWND = 0` 是“当前无法解析窗口句柄”的状态，不等于游戏已经关闭。必须区分临时解析失败、Bannerlord 窗口切换和真正 Framework shutdown。

**代码归属**：`HtmlUiWindowTracker`。不要在 `HtmlUiHost`、Keyboard Patch 或 Consumer 中各自实现一套 HWND 判定。

### 100ms 窗口跟随

**历史问题**：Host 曾通过 100ms `FollowBannerlordWindow()` 同时负责窗口位置、可见性和输入/前台判断，造成状态互相覆盖并放大焦点问题。

**当前规则**：窗口事实和 Overlay Bounds 归 `HtmlUiWindowTracker`；InputMode 归 `HtmlUiInputControllerPatch`。新的功能不得恢复一个新的 Timer 来替代 Tracker。

### Passive / 小地图抢输入

**结论**：`Passive` 的定义是“可见但 HTML 不拥有输入”。如果 Consumer 需要只读小地图，Consumer 应使用 `HtmlUiInputMode.Passive`；Framework 负责 WebView disabled / pass-through 的公共语义。不得给 TacticalMap 单独增加 Harmony 输入 Patch。

### F12 / DevTools

F12 不是关闭协议。Framework 默认禁止 DevTools/浏览器菜单；浏览器 UI policy 由 `HtmlUiContextMenuPatch`/Host 统一负责，Keyboard Patch 只做最终安全兜底。禁止在 Consumer 单独实现 DevTools 开关。

### ESC 关闭

必须确认三段证据：

```text
ESC filter installed
→ Escape detected
→ CloseCurrent completed
```

最终状态应为 `currentPage=<null>`、`inputMode=Hidden`、`hostVisible=False`。

ESC 语义归 `HtmlUiKeyboardAndDiagnosticsPatch` + PageManager；Consumer 的 `CloseOnEscape` 只描述页面是否允许关闭。

## 2. WebView2 / Overlay

### WebView2 UI thread

**历史错误**：`CoreWebView2 can only be accessed from the UI thread`、相关 `E_NOINTERFACE`。

**根因**：Patch 在错误线程访问 `CoreWebView2`。

**规则**：等待 WebView2 UI thread 完成初始化，在 `EnsureCoreWebView2Async()` 完成后再安装依赖 `CoreWebView2` 的 Patch。

**代码归属**：`HtmlUiHost` + 对应 Runtime Patch。Consumer 不可访问 `CoreWebView2`。

原始复盘：`Handoff/BUGFIX_RUNTIME_PATCH_THREADING_20260816.md`、`Handoff/FULL_CODE_AUDIT_20260815.md`。

### Overlay 不可见但仍可点击

**现象**：HTML 看不见，但对应按钮点击区域仍存在。

**关键结论**：不要随意修改 Chromium/WebView2 内部子窗口的 Win32 extended style。特别是 `Chrome_RenderWidgetHostHWND` 上的 `WS_EX_TRANSPARENT` 实验会重新触发该问题。

**代码归属**：Overlay/Host/Win32；禁止通过 Consumer-specific HWND patch 绕过。

原始复盘：`Handoff/BUG_POSTMORTEM_OVERLAY_RENDERING_20260814.md`。

### Captured Overlay 闪烁

**现象**：Captured 模式下 Overlay 变成前台窗口，旧窗口 tracking 逻辑误判 Bannerlord 失焦并反复 Hide/Show。

**结论**：Captured 模式必须允许 Framework Overlay foreground，不得把 Overlay 自身前台当成游戏失焦。

**代码归属**：WindowTracker + InputController；不要在 Consumer 自己处理 foreground。

原始记录：`Handoff/PROJECT_STATUS.md`。

## 3. Page / Navigation

### Page Id 未初始化

**现象**：`Dictionary.ContainsKey(null)`，Page 注册失败，随后 Consumer 页面、Diagnostics 等全部不可用。

**根因**：`HtmlUiPage.Id` 只读属性在构造函数中没有执行 `Id = id`。

**结论**：Page 构造函数必须保证合法、非空、可注册的 Id。

**代码归属**：`HtmlUiPage` / `HtmlUiPageManager`。

### Navigation Race

页面快速 Open / Close / Reload 时，旧导航或旧异步结果不得覆盖新页面状态。当前代码使用 NavigationId Guard 等机制；任何后续改动都必须保持“旧结果不能提交”的原则。

**代码归属**：`HtmlUiPageManager` + `HtmlUiNavigationRacePatch`。

原始记录：`Handoff/NAVIGATION_RACE_20260815.md`。

## 4. Bridge / Request / Cancellation

### Request active count 与注册数

`BridgeRequestCount` 表示注册数量，不等于当前 pending Request；`ActiveRequestCount` 才表示真正持有 CancellationTokenSource 的活跃 Request。

取消后不能只验证调用了 Cancel；正确验收条件是最终活跃请求回落到基线。

### Shutdown Cancellation

Framework Dispose 时应先取消活跃 Request，再销毁 Host/WebView2。旧 ConsumerScope 在 Framework shutdown 后继续访问服务必须得到明确失败，而不是出现连续 ERROR 或访问已销毁对象。

**代码归属**：`HtmlUiBridgeShutdownPatch` / `HtmlUiBridge` / `HtmlUiService.Dispose`。

### Owner 竞态注销

同名 Command/Request/Page 在旧 Owner Dispose 与新 Owner 重绑之间存在竞态。注销必须用 owner + entry identity 的原子路径，不能“检查 owner → 再按名字删除”。

### requestCancellable 的晚到结果

请求被 timeout / AbortSignal / pagehide / runtime shutdown 取消后，晚到的成功结果不能覆盖后续状态。需维持 cancellation generation / request identity 保护。

**代码归属**：`HtmlUiBridge` + Runtime request cancellation。

## 5. Runtime / Binding / i18n

### Component spread 破坏对象身份

曾出现 `component()` 使用对象 spread 生成新对象，可能丢失 prototype、Symbol 和 non-enumerable methods。

**规则**：保留原 Component 对象，仅包装/替换 disposer。

### i18n bind 生命周期

必须处理：dispose、pagehide、locale generation、重复 bind、动态 DOM 新增/删除、MutationObserver 清理和连续 mutation 合并。

### 翻译不全

先区分：

1. 文本本身硬编码，没有走 i18n。
2. Key → Runtime → Bridge → Bannerlord Localization 链路断裂。

不要仅凭语言 XML 存在判断翻译正常。

## 6. 构建 / 部署

### Consumer 找不到 Framework 程序集

典型错误：`Could not load file or assembly 'BannerlordHtmlUI, Version=0.44.0.0'`。

优先检查 Consumer `launchSettings.json` / `_MODULES_` 序列，确保 `BannerlordHtmlUI` 位于 `$(ModuleId)` 之前。

### HTML 资源未复制

ContentRoot 注册失败时优先检查运行时 DLL 的 `Assembly.Location` 与最终输出目录，不要依据源码目录或普通工程 `bin` 目录猜路径。

### C# 10 Verbatim String

正式环境曾出现 `CS1519/CS8803/CS1003` 级联错误。根因是 C# `@"..."` 字符串中错误使用 `\"` 转义双引号，导致 JS 注入 Patch 被 C# 错误解析。

**规则**：Framework 兼容目标为 C# 10 / net472；不要为了编译问题提高 LangVersion。

## 7. 模块定位速查

| 症状 | 首选模块 | 禁止先改 |
|---|---|---|
| HWND/窗口位置/最小化 | `HtmlUiWindowTracker` | Consumer / Keyboard Patch |
| Hidden/Passive/Captured | `HtmlUiInputControllerPatch` | WindowTracker / Consumer Patch |
| ESC/F12 | `HtmlUiKeyboardAndDiagnosticsPatch` | Consumer 热键 Patch |
| 右键菜单/DevTools | Browser policy | InputController |
| Page 打开/关闭/Reload | `HtmlUiPageManager` | Host / Consumer workaround |
| Request/取消/Owner | `HtmlUiBridge` | PageManager |
| State | `HtmlUiStateStore` | Bridge / PageManager |
| GameThread 调度 | `GameThreadDispatcher` | 自建队列 / Task.Run |
| TacticalMap 特有问题 | `New_ZZZF.TacticalMap` | Framework |
| CustomSkill 特有问题 | `New_ZZZF.CustomSkill` | Framework |

## 8. 快速排错顺序

### 页面打不开

```text
Framework Ready
→ ContentRoot 实际目录
→ Page Id
→ Page 注册
→ Navigate
→ NavigationCompleted
→ Runtime Ready
```

### 页面显示但点不到

```text
InputMode
→ WebView Focus/Enabled
→ app.input.capture()
→ pointer-events
→ Overlay 层级
→ Consumer JS
```

### 页面自动消失

```text
hwnd=0
→ WindowTracking
→ Captured foreground
→ Page transition
→ Framework shutdown
→ ProcessFailed
```

不要看到 `hwnd=0` 就直接认定游戏关闭。

### Overlay 消失/变白

```text
Overlay transparency
→ WebView2 rendering
→ child window style
→ Chromium HWND
→ D3D/Chromium composition
```

优先对照已知正常基线，不要随机修改 Win32 extended style。
