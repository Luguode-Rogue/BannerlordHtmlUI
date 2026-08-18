# BannerlordHtmlUI 当前架构

## 1. 系统定位

BannerlordHtmlUI 是 Bannerlord Mod 的 HTML/WebView2 UI Framework。Framework 负责宿主、通信、页面生命周期、输入、状态同步、诊断与兼容边界；Consumer 负责游戏业务、UI 资源和具体页面。

```text
Bannerlord
   │
   ├── Game Thread
   │
   └── BannerlordHtmlUI Framework
          ├── HtmlUiService
          ├── HtmlUiHost
          │    └── HtmlUiOverlayForm
          ├── HtmlUiPageManager
          ├── HtmlUiBridge
          ├── HtmlUiStateStore
          ├── HtmlUiConsumerScope
          ├── HtmlUiDiagnostics
          └── Runtime / WebView2
                 │
                 └── HTML / CSS / JS

HtmlUiConsumerTestMod
   ├── Test page
   └── StressLab
```

## 2. Framework 层职责

### HtmlUiService

公共服务入口。负责 Framework Ready/Shutdown、页面、状态、Bridge、诊断等能力聚合。

### HtmlUiHost

负责 WebView2 初始化、UI thread、导航、窗口状态、Overlay、WebMessage 转发和宿主生命周期。

**禁止 Consumer 绕过 Framework 自行创建 WebView2。**

### HtmlUiPageManager

负责 Page 注册、Open、Close、Reload、当前页面和导航生命周期。

### HtmlUiBridge

负责 Command / Request / Response / Event，以及 owner、取消和生命周期竞态。

### HtmlUiStateStore

负责 C# 侧 State 存储、删除、快照和 Runtime 同步。

### HtmlUiConsumerScope

Consumer 的资源所有权边界。Page、ContentRoot、State、Command、Request 等资源应归属于 Scope，并在 Dispose 时统一清理。

### Diagnostics

只提供低频、可操作的运行时摘要。禁止恢复逐帧 Window Tracking 日志作为正常模式。

## 3. Runtime 层

当前 Runtime 包含：

- State / State binding
- Event
- Command
- Request / requestCancellable
- i18n / i18n.bind
- Component 生命周期
- HotReload / Navigation race guard
- Runtime error reporting

## 4. 三条线程边界

```text
Bannerlord Game Thread
        │
        │ Game-thread API
        ▼
Framework C#
        │
        │ marshal
        ▼
WebView2 UI Thread
        │
        ▼
Chromium / JavaScript Runtime
```

关键规则：

1. `CoreWebView2` 只能在 WebView2 UI thread 使用。
2. Request handler 的初始调用位于 Bannerlord game thread；`await` 后继续执行不保证仍在 game thread。
3. 需要访问 Bannerlord Game API 的 async continuation 必须显式回到 game thread。
4. Response/PostMessage 应回到 WebView2 UI thread。
5. Framework shutdown 后任何旧 Consumer/handler 都必须尽早得到明确失败，而不是继续访问已销毁 Host。

## 5. 页面生命周期

```text
Framework Startup
 → WebView2 Initialization
 → HtmlUiService Ready
 → Consumer Register
 → Page Open
 → Navigation
 → Runtime Ready
 → State / Command / Request
 → Page Close
 → Owner Dispose / Request Cancel
```

Reload / HotReload 会重新建立页面 Runtime。Consumer 不得假定 Runtime 对象永远不重建。

## 6. 输入与关闭

主要关闭路径：

- ESC
- 页面自身 Close Command / Button

F12 不作为可靠关闭方案。

Captured Input 模式下，Overlay 自身成为前台不能被错误解释成游戏失焦。临时 `HWND = 0` 也不能直接等价为游戏窗口关闭；应区分暂时无法解析 HWND 与真正 Framework shutdown。

## 7. Overlay

透明 Overlay 属于 Framework / Host 能力。

已验证规则：不要随意修改 Chromium/WebView2 内部子窗口的 Win32 extended style。特别是对 `Chrome_RenderWidgetHostHWND` 设置 `WS_EX_TRANSPARENT` 的实验会导致已经确认的“不可见但点击区域存在”问题复现。

当前正常渲染基线：`debug/test-root-transparent`。

修改 Overlay、D3D、Chromium 子窗口层级或透明度时，必须执行对应回归测试。

## 8. Ownership

推荐：

```text
Consumer
  ↓
HtmlUiConsumerScope
  ├── ContentRoot
  ├── Page
  ├── State
  ├── Command
  ├── Request
  └── Component
```

所有资源清理必须具备 owner identity 保护，防止旧 Scope 在竞态期间注销新 Owner 的同名资源。

## 9. 状态、动作、查询

```text
State   = UI 现在是什么状态
Command = 请执行这个动作
Request = 请执行这个动作并返回结果
Event   = 某个运行时事件发生了
```

业务逻辑留在 C#；HTML/JS 是 View 和交互层。

## 10. Failure Model

Framework 必须区分：

- Page 不存在
- Navigation race
- Runtime 未就绪
- Request 被取消
- Request timeout
- Owner 已 Dispose
- WebView2 ProcessFailed
- Framework shutdown
- Game window 临时不可解析

失败应产生稳定错误码/诊断信息，而不是静默吞掉。
