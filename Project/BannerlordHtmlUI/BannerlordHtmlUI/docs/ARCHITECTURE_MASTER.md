# BannerlordHtmlUI 当前架构与模块边界

> 本文是当前 Framework 架构规范。历史架构记录保留在 Handoff/CHANGELOG，不得反向覆盖当前规范。
>
> 当前开发线：`dev`。Framework 工程版本以 `BannerlordHtmlUI.csproj` 为准。

## 1. Framework 定位

BannerlordHtmlUI 是 Bannerlord 的 HTML/WebView2 UI Framework。Framework 只提供通用 UI 基础设施；Consumer Mod 负责自己的游戏业务、Controller、VM、页面和业务状态。

```text
Bannerlord Game Thread
        │
        ▼
HtmlUiService
        │
        ├── GameThreadDispatcher
        ├── PageManager
        ├── StateStore
        ├── ConsumerScope
        └── HtmlUiHost
              │
              ├── WebView2 UI Thread
              ├── Bridge
              ├── WindowTracker
              ├── InputController
              ├── OverlayForm
              ├── Browser Policy
              ├── Runtime Patches
              └── Recovery
```

详细文件归属见：`FRAMEWORK_MODULE_MAP.md`。
强制修改规则见：`CODE_PLACEMENT_RULES.md`。

## 2. 唯一职责模型

### `HtmlUiService`
Framework 公共门面与生命周期协调器。

负责：
- Initialize / Ready / Dispose / Shutdown。
- 对外暴露 Page、State、Command、Request、Event。
- 驱动 GameThreadDispatcher。

不负责：窗口几何、输入状态机、Consumer 业务。

### `GameThreadDispatcher`
唯一的 WebView/异步回 GameThread 基础设施。

不允许 Consumer 自己建立第二套 GameThread queue。

### `HtmlUiHost`
WebView2 / WinForms 宿主。

负责：
- WebView2 Environment / Control / UI thread。
- Navigation / resource mapping。
- Host 生命周期。

不负责：Consumer 热键、Page business state、第二套 Input/Window tracking。

### `HtmlUiPageManager`
唯一的 Page transition owner。

负责：
- Register / Unregister。
- Open / Close / Reload。
- Current page。
- Pending/stale navigation protection。
- Page lifecycle event。

不负责：焦点、Win32 Window、Consumer page-specific state。

### `HtmlUiBridge`
唯一的 C# ↔ JS transport owner。

负责：Command / Request / Response / Event、Owner、Cancellation、stale result protection。

不负责：Window、Input、Page UI business。

### `HtmlUiStateStore`
唯一的 C# State source of truth。

负责：Set / Get / Remove / Snapshot / Runtime sync。

不负责：Page/Input 状态机。

### `HtmlUiConsumerScope`
Consumer ownership boundary。

负责：ContentRoot、Page、Command、Request、State 的 owner identity 与 Dispose。

### `HtmlUiWindowTracker`
唯一的 Window fact/geometry owner。

负责：
- Bannerlord HWND。
- Foreground/visible/minimized。
- WinEventHook。
- Overlay Bounds。
- `HtmlUiOverlayLayout` 几何。

不负责：InputMode、ESC/F12、Page Close。

### `HtmlUiOverlayLayout`
唯一的通用 Overlay 几何意图 API。

例如：`FullWindow`、`TopRight`。

Consumer 只能调用它表达布局意图；Framework 不知道“这是 TacticalMap”。

### `HtmlUiInputControllerPatch`
唯一的 Framework InputMode owner。

负责：
- Hidden / Passive / Captured / MouseCaptured。
- WebView Enabled/Disabled。
- PassThrough / native capture。
- 一次性 focus transition。
- Input transition generation。

不负责：Window tracking、Consumer hotkey、Consumer-specific mode。

### `HtmlUiOverlayForm`
原生 Overlay 表面。

只负责 WinForms/native window behavior 与最低级 ESC/native-message fallback；不是业务状态机。

### `HtmlUiKeyboardAndDiagnosticsPatch`
Framework keyboard/browser safety fallback。

负责：
- ESC close safety。
- F12 / DevTools policy enforcement。
- WebView accelerator diagnostics。

禁止加入 M/N 等 Consumer hotkey。

### `HtmlUiContextMenuPatch`
Browser UI policy owner。

负责：
- 默认右键菜单策略。
- DevTools policy。
- Status bar/browser chrome policy。

不得接管 Page/Input/Consumer。

### Runtime Patch 系列

负责 Framework runtime：Binding、i18n、State bootstrap/remove、Error model、Request cancellation、Navigation race、HotReload/Recovery。

如果问题只存在于 Consumer，先修 Consumer；不得把临时 workaround 固化为 Framework Runtime Patch。

## 3. 三个执行域

```text
Bannerlord Game Thread
    ↓
Framework C#
    ↓ marshal
WebView2 UI Thread
    ↓
Chromium / JS Runtime
```

规则：

1. `CoreWebView2` 只在 WebView2 UI thread 使用。
2. Game API 只能在 Bannerlord Game Thread 使用。
3. Request handler 进入 GameThread 后，`await` 不保证 continuation 仍在 GameThread；需要 Game API 时必须显式回 GameThread。
4. Runtime/Bridge Response 最终要在 WebView2 UI thread 发布。
5. 禁止为同一条边界创建新的 `Task.Run` / `Invoke` / queue 体系。

## 4. Input ownership

Framework 的 InputMode 是单一状态源：

```text
Hidden
Passive
Captured
MouseCaptured
```

语义：

- `Hidden`：页面关闭/不可见，不接收 HTML 输入。
- `Passive`：页面可见但只展示，HTML 不拥有输入。
- `Captured`：HTML 拥有键盘/鼠标焦点。
- `MouseCaptured`：HTML 拥有鼠标输入；具体键盘语义仍由 Consumer/Framework 规则决定。

输入修改必须进入 `HtmlUiInputControllerPatch`。Keyboard Patch 只能做安全兜底，WindowTracker 不能修改 InputMode。

## 5. Window ownership

窗口事实与输入语义必须分开：

```text
WindowTracker
    = “窗口在哪里/是什么状态”

InputController
    = “HTML 是否拥有输入”
```

禁止恢复 100ms window polling 作为新的架构；旧 `FollowTimer` 属于待移除遗留兼容代码。

### HWND=0

`HWND = 0` 只能表示当前暂时无法解析窗口，不能直接等价于 game exit/shutdown，也不能未经状态判断隐藏现有 UI。

## 6. Browser policy

Framework 默认：

- 右键浏览器菜单关闭。
- DevTools/F12 关闭。
- Status bar 关闭。

如果未来允许开发模式打开 DevTools，必须由单一 browser policy owner 管理；不得同时在 Host、Keyboard Patch、Consumer 中各写一份开关判断。

## 7. Page lifecycle

```text
Framework Startup
 → WebView2 ready
 → Consumer register
 → Page Open
 → Navigation
 → Runtime bootstrap
 → Active
 → Page Close
 → Owner cleanup / Request cancellation
```

快速 Open/Close/Reload 必须保证 stale navigation/async result 无法覆盖新页面。

## 8. Overlay rendering

不要通过随机修改 Chromium child window extended style 解决渲染问题。尤其禁止重新引入 `Chrome_RenderWidgetHostHWND + WS_EX_TRANSPARENT` 方案；该方案历史上会制造“不可见但点击区域存在”。

透明度、D3D、Overlay 几何修改后必须跑对应回归矩阵。

## 9. Failure model

Framework 必须区分：

- Page not registered
- ContentRoot missing
- Navigation race
- Runtime not ready
- Request cancellation/timeout
- Owner disposed
- ProcessFailed
- Framework shutdown
- Game HWND temporarily unavailable

错误不能靠“再加一个全局 Patch”消失；先定位真正状态 owner。

## 10. 修改规则

每次 Framework 修改前必须：

1. 查 `FRAMEWORK_MODULE_MAP.md`。
2. 查 `CODE_PLACEMENT_RULES.md`。
3. 搜索同一状态是否已经有第二个 owner。
4. 搜索已有 workaround / historical bug。
5. 若修改公共语义，同步 API/Architecture/Testing 文档。
6. 若只针对 Consumer，必须留在 Consumer。

违反上述规则的修复不得作为正式 Framework 方案合入。
