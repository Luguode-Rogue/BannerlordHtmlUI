# Framework 模块地图

本文件是 **Framework C# 代码的职责地图**。它回答两个问题：

1. 某个问题应该在哪个模块修改？
2. 哪些模块禁止为了方便而代替另一个模块承担职责？

当前开发线：`dev`

## 一、总结构

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
        └── Host
              │
              ├── WebView2 UI Thread
              ├── OverlayForm
              ├── Bridge
              ├── WindowTracker
              ├── InputController
              ├── Browser/ContextMenu Policy
              ├── Runtime Injection/Patches
              └── Recovery
```

## 二、模块职责

### 1. `SubModule.cs`
**职责：Framework 启动/卸载入口。**

允许：
- 调用 `HtmlUiService.InitializeAsync()` / `Dispose()`。
- 安装/卸载 Framework 级 Harmony/compatibility patch。
- 连接 Bannerlord 生命周期到 Framework 生命周期。

禁止：
- 实现 Page 业务。
- 实现 Consumer 输入逻辑。
- 直接操作 WebView2 控件。
- 添加窗口跟随、Overlay 几何或页面状态机。

---

### 2. `HtmlUiService.cs`
**职责：Framework 公共门面与生命周期协调器。**

负责：
- Ready / Unloading / Unloaded 状态。
- 对外暴露 `Pages`、`State`、Command、Request、Event 等能力。
- 驱动 `GameThreadDispatcher.Drain()`。
- Framework 内置 command/request。

禁止：
- 直接写 Win32 窗口逻辑。
- 直接实现 WebView2 input capture。
- 直接实现 Page transition 状态机。
- 为某个 Consumer 增加业务分支。

---

### 3. `GameThreadDispatcher.cs`
**职责：跨线程进入 Bannerlord Game Thread 的唯一基础设施。**

负责：
- `GameThread -> queued work`。
- 每 Tick 限量 Drain。
- shutdown 时清空任务。

禁止：
- WebView2 UI 控件操作。
- 业务逻辑。
- Page / Input / Window 状态判断。

以后任何“WebView 回调后需要操作 Bannerlord API”的新路径，先检查这里，而不是自行 `Invoke`、`Task.Run` 或创建第二个队列。

---

### 4. `HtmlUiHost.cs`
**职责：WebView2 + WinForms 原生宿主。**

负责：
- WebView2 环境/实例初始化。
- WebView2 UI Thread 生命周期。
- 导航、资源映射、Browser 事件。
- Host 对外的最小宿主操作。

禁止：
- 再建立第二套 InputController。
- 再建立第二套 WindowTracker。
- 写 TacticalMap/CustomSkill 业务判断。
- 用 Timer 轮询代替专用 WindowTracker。

> 旧 `100ms FollowTimer` 仅属于历史遗留代码，新的窗口同步必须走 `HtmlUiWindowTracker`。

---

### 5. `HtmlUiOverlayForm.cs`
**职责：原生 Overlay 窗口表面。**

负责：
- WinForms `Form`。
- Owner / NoActivate / PassThrough 等原生窗口能力。
- 最低级别的 ESC/native-message fallback。

禁止：
- 决定 Framework 当前是什么 InputMode。
- 决定哪个 Page 应该打开/关闭。
- 读取 Consumer 状态。
- 处理 WebView2 browser policy。

OverlayForm 是“窗口表面”，不是业务状态机。

---

### 6. `HtmlUiWindowTracker.cs`
**职责：窗口事实与 Overlay 几何。**

负责：
- Bannerlord HWND 解析。
- 前台/可见/最小化事实。
- WinEventHook。
- Overlay Bounds。
- `HtmlUiOverlayLayout` 几何应用。

禁止：
- 改 InputMode。
- 决定是否 CaptureInput。
- 处理 ESC/F12。
- 调用 Consumer API。
- 用 Timer 代替 WinEvent。

特别规则：

```text
WindowTracker = “窗口现在在哪里/是什么状态”
InputController = “UI 应该如何接收输入”
```

二者不得互换职责。

---

### 7. `HtmlUiInputControllerPatch.cs`
**职责：唯一的 Framework InputMode 所有者。**

负责：
- `Hidden / Passive / Captured / MouseCaptured` 语义。
- WebView enable/disable。
- Native capture/release。
- PassThrough / activation 的一次性状态转换。
- Input transition generation。

禁止：
- 自己实现窗口移动/resize tracking。
- 自己创建 100ms Timer。
- 自己监听 WinEvent。
- 写某个 Consumer 的模式名（例如 `CompactPassive`、`FullInteractive`）。

Consumer 只能通过 Framework 的公共 InputMode/布局 API 表达意图。

---

### 8. `HtmlUiKeyboardAndDiagnosticsPatch.cs`
**职责：键盘安全过滤与诊断兜底。**

负责：
- ESC close safety fallback。
- DevTools/F12 policy enforcement。
- WebView2 accelerator 诊断。
- 低频输入 trace。

禁止：
- 成为第二套 InputController。
- 改 Overlay 几何。
- 控制 WindowTracker。
- 加入 Consumer 特定按键（例如 TacticalMap 的 N/M）。

原则：

```text
InputController = 主状态
KeyboardPatch   = 只做安全拦截/兜底
```

---

### 9. `HtmlUiContextMenuPatch.cs`
**职责：浏览器 UI 安全策略。**

负责：
- 默认右键菜单策略。
- DevTools 设置策略。
- WebView2 browser chrome 的默认关闭。

禁止：
- 处理输入捕获。
- 处理 Page Close。
- 处理 Consumer UI。

Framework 默认策略必须在 Host 第一次 Configure 时即生效；不能依赖“Ready 之后再补 Patch”才能进入安全状态。

---

### 10. `HtmlUiPageManager.cs`
**职责：Page 注册与 Page 生命周期。**

负责：
- Register / Unregister。
- Open / Close / Reload。
- Current page。
- Pending navigation / stale navigation 保护。
- Page lifecycle event。

禁止：
- 实现 Consumer Controller。
- 直接写 Win32 focus。
- 解释 InputMode 的业务含义。
- 处理 TacticalMap / CustomSkill 子状态。

---

### 11. `HtmlUiBridge.cs`
**职责：C# ↔ JS 协议传输。**

负责：
- Command。
- Request / Response。
- Event。
- Cancellation。
- Owner / entry identity。
- Bridge shutdown。

禁止：
- 判断 UI 布局。
- 判断窗口大小。
- 操作 Bannerlord UI/VM 业务对象。

Request handler 的线程契约必须以 `GameThreadDispatcher` / API 文档为准。

---

### 12. `HtmlUiStateStore.cs`
**职责：C# State source of truth。**

负责：
- Set / Get / Remove。
- Snapshot。
- 向 Runtime 发布 State。

禁止：
- 反向推导 Consumer 业务状态。
- 在 StateStore 内实现 Page 或 Input 状态机。

---

### 13. `HtmlUiConsumerScope.cs`
**职责：Consumer 资源所有权。**

负责：
- Consumer-owned Page。
- Command / Request。
- ContentRoot。
- State。
- Dispose 清理。
- owner + entry identity 保护。

禁止：
- 创建 WebView2。
- 修改 Framework input/window internals。

---

### 14. `HtmlUiOverlayLayout.cs`
**职责：通用 Overlay 几何意图。**

负责：
- `FullWindow`。
- `TopRight` 等布局意图。
- 将布局意图交给 WindowTracker 计算实际 Bounds。

禁止：
- 处理输入。
- 处理 Page 生命周期。
- 写 TacticalMap-specific 规则。

Consumer 可以调用公共布局 API，但 Framework 不得知道“这是小地图”。

---

### 15. Runtime Patch 系列

包括：

- Binding lifecycle
- Binding scheduler
- i18n lifecycle
- State bootstrap/remove
- Error model
- Request cancellation
- Navigation race
- HotReload / Process recovery

这些文件的职责是 **Framework Runtime / WebView compatibility**。

禁止：
- 在 Runtime Patch 中增加 Consumer 业务逻辑。
- 在其中直接处理 Bannerlord gameplay。
- 把某个 Consumer 的临时 workaround 固化成 Framework Patch。

如果一个问题只发生在 TacticalMap，应先修 TacticalMap Consumer；只有确认为 Framework 公共语义错误，才进入这里。

---

## 三、修改决策表

| 要修改什么 | 唯一首选模块 |
|---|---|
| M/N 等 Consumer 热键 | Consumer SubModule / Controller |
| Page Open/Close | `HtmlUiPageManager` |
| WebView2 初始化 | `HtmlUiHost` |
| Overlay Bounds / HWND / Minimize | `HtmlUiWindowTracker` |
| Hidden/Passive/Captured | `HtmlUiInputControllerPatch` |
| ESC/F12 安全兜底 | `HtmlUiKeyboardAndDiagnosticsPatch` |
| 右键菜单/DevTools | Browser policy / `HtmlUiContextMenuPatch` |
| Command/Request | `HtmlUiBridge` |
| State | `HtmlUiStateStore` |
| Consumer cleanup | `HtmlUiConsumerScope` |
| GameThread marshal | `GameThreadDispatcher` |
| Overlay TopRight/FullWindow | `HtmlUiOverlayLayout` |
| i18n / Binding / Runtime | 对应 Runtime Patch / `_Module/web` |
| TacticalMap 具体逻辑 | `New_ZZZF.TacticalMap`，禁止放 Framework |
| CustomSkill 具体逻辑 | `New_ZZZF.CustomSkill`，禁止放 Framework |

## 四、硬性禁止

1. 不在 Framework 里加入 Consumer 名称判断。
2. 不在 Keyboard Patch 里增加 Consumer 热键。
3. 不在 WindowTracker 里解决输入问题。
4. 不在 InputController 里解决窗口几何。
5. 不在 PageManager 里解决焦点。
6. 不在 Bridge 里直接调用 Consumer Bannerlord API。
7. 不再用“先加一个 Patch 临时绕过”替代公共语义修复。
8. 发现已有模块职责不够时，先扩展正确模块；不要把代码塞进最方便修改的文件。
9. 新增 Framework 公共行为必须同时更新 `ARCHITECTURE_MASTER.md`、`API.md`（若影响公共 API）和本文件。
10. 新增 Consumer-specific workaround 必须留在 Consumer，并在其自己的文档说明原因；禁止复制进 Framework。
