# Framework 代码归属硬规则

> 这是开发约束，不是建议。
> 目的：以后修 Bug 时必须进入“真正拥有该状态/职责”的模块，禁止把补丁塞进当前最方便修改的文件。

## 1. 判断原则

修改前先问：

> **谁拥有这个状态？谁应该对这个副作用负责？**

然后只能在该模块修改。

### 状态归属

```text
Page 当前是谁        → HtmlUiPageManager
InputMode 是什么      → HtmlUiInputControllerPatch
Window 在哪里/是否最小化 → HtmlUiWindowTracker
Overlay 几何意图      → HtmlUiOverlayLayout
Browser policy        → HtmlUiContextMenuPatch / HtmlUiHost
JS↔C# 协议            → HtmlUiBridge
C# State              → HtmlUiStateStore
GameThread queue      → GameThreadDispatcher
Consumer 业务状态     → Consumer 自己
```

## 2. 绝对禁止的跨区写法

### 禁止在 Consumer 修改 Framework 内部窗口状态

错误：

```csharp
HtmlUiHost.SomePrivateField = ...;
Win32.SetForegroundWindow(...);
new WebView2(...);
HarmonyPatch(HtmlUiHost...);
```

正确：

```csharp
HtmlUiService.SetInputMode(...);
HtmlUiOverlayLayout.UseTopRight(...);
HtmlUiService.Pages.Open(...);
```

Consumer 只能表达意图，不得管理 Framework 内部窗口生命周期。

### 禁止在 Framework 判断 Consumer 名称

禁止：

```csharp
if (ownerId == "New_ZZZF.TacticalMap") ...
if (pageId.Contains("CustomSkill")) ...
```

如果只有 TacticalMap 出问题，先修 TacticalMap。
如果证明是 Framework 公共语义错误，才修改 Framework，并且必须使修改对所有 Consumer 都成立。

### 禁止 Keyboard Patch 承担 Consumer 热键

禁止把：

```text
M / N / F10 / F11 / TacticalMap 特有按键
```

加入 `HtmlUiKeyboardAndDiagnosticsPatch`。

Keyboard Patch 只允许负责 Framework 级 ESC/F12/browser accelerator safety。

### 禁止 WindowTracker 解决输入问题

`HtmlUiWindowTracker` 只能回答：

```text
游戏窗口在哪？
当前是否前台？
当前是否可见？
当前是否最小化？
Overlay 应该在哪里？
```

禁止在 Tracker 内：

```text
CaptureInput
ReleaseInput
ESC
F12
Page.CloseCurrent
Consumer callback
```

### 禁止 InputController 解决窗口几何

`HtmlUiInputControllerPatch` 只负责 InputMode。

禁止：

```text
监听 WinEvent
计算窗口 Bounds
实现 100ms FollowTimer
判断 TacticalMap 位置
```

### 禁止 PageManager 解决焦点

PageManager 只能管理 Page transition。

禁止在 PageManager 里添加：

```text
SetForegroundWindow
SetPassThrough
WebView.Enabled
WS_EX_* 修改
```

它只调用公开的 Host/Input API。

### 禁止 Bridge 直接执行业务

Bridge 只做 transport/routing。

错误：

```text
Bridge → Hero.MainHero
Bridge → Mission.Current
Bridge → TacticalMapController
```

正确：

```text
Bridge → registered Consumer handler
          ↓
      Consumer GameThread logic
```

## 3. Framework 新需求的放置规则

### 新增公共 API

顺序：

```text
1. 修改正确模块
2. 更新 API.md
3. 更新 ARCHITECTURE_MASTER.md
4. 更新 FRAMEWORK_MODULE_MAP.md
5. 增加回归测试
```

### 新增 Framework workaround

如果 workaround 只针对一个 Consumer：

> **不得进入 Framework。**

放在 Consumer 自己，并记录：

```text
现象
触发条件
为什么 Framework 不是根因
为什么 workaround 不应进入 Framework
```

### 新增安全策略

例如：

```text
DevTools
Context Menu
ESC
Browser Accelerator
```

必须先判断它是不是 Framework 公共规则。

如果是公共规则，建立唯一 policy owner，禁止多个模块各自实现一份。

## 4. 输入问题定位树

```text
页面没有输入？
    ↓
先看 InputMode
    ↓
InputMode 是否正确？
    ├─ 否 → InputController
    └─ 是
         ↓
WebView 是否 Enabled？
         ├─ 否 → InputController
         └─ 是
              ↓
Overlay 是否 PassThrough / 激活？
              ├─ 错 → InputController / OverlayForm
              └─ 对
                   ↓
是否是 Browser Accelerator？
                   ├─ 是 → KeyboardAndDiagnostics
                   └─ 否
                        ↓
检查 Consumer 自己的 JS pointer/keyboard 逻辑
```

## 5. 页面问题定位树

```text
页面打不开
    ↓
Page 注册？          → PageManager / Consumer registration
    ↓
ContentRoot 正确？   → Host / Consumer deployment
    ↓
Navigation 正确？    → Host / NavigationRace
    ↓
Runtime 正常？       → _Module/web Runtime
    ↓
Consumer State？     → Consumer
```

## 6. 窗口/Overlay 问题定位树

```text
Overlay 位置错误
    → WindowTracker / OverlayLayout

Overlay 不显示
    → WindowTracker → Host visibility

Overlay 显示但抢焦点
    → InputController / OverlayForm

Overlay 不可见但点击仍存在
    → 禁止先改 Chromium 子窗口 style
    → 对照 Overlay rendering postmortem
```

## 7. 修改前必须做的静态检查

每次修改 Framework 前：

1. 搜索同一状态是否已有第二个 owner。
2. 搜索调用链是否已经存在旧 workaround。
3. 搜索 `SetForegroundWindow` / `ShowWindow` / `BeginInvoke` / `Invoke` / `Timer`。
4. 搜索 Consumer ID 是否泄漏进 Framework。
5. 搜索是否需要同步更新 `BUG_KNOWLEDGE_BASE.md`。

如果发现两个模块都修改同一状态：

> **先拆职责，再修 Bug。**

禁止继续追加第三个修复点。

## 8. 修改完成后的最低要求

Framework 变更至少检查：

```text
Build
↓
Framework-only startup
↓
Page Open / Close / Reopen
↓
ESC
↓
Alt+Tab
↓
Window move/resize/minimize
↓
Consumer TacticalMap
↓
Consumer CustomSkill
↓
Shutdown
```

任何一项未测试，不得把对应问题标记为“已修复”。
