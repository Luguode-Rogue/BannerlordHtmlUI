# BannerlordHtmlUI 开发指南

> 版本：Surface 架构 v1（0.45.0 开发线）
> 本文档是当前唯一规范入口。历史文档见 `docs/过期文档/`，仅作考古用途。
> Surface 详细设计见 `BannerlordHtmlUI/docs/SURFACES.md`。

---

## 1. 架构总览

### 1.1 线程模型

| 线程 | 归属 | 规则 |
|---|---|---|
| **GameThread** | Bannerlord 主线程 | 一切游戏状态访问；由 `SubModule.OnApplicationTick` → `HtmlUiService.Tick()` → `Dispatcher.Drain()` 驱动 |
| **WebView UI thread** | 专属 STA 线程 | WinForms Form / WebView2 控件操作；经 `EnsureUiThread` / `BeginInvoke` 投递 |
| **JS Runtime** | WebView2 进程 | 通过 bridge 消息与 C# 通信，无直接线程共享 |

**线程契约（强制）**：游戏线程没有 `SynchronizationContext`。request handler 中任何真实 `await` 之后的代码在**线程池**执行。回到游戏线程的唯一方式：

```csharp
var data = await LoadDataAsync();      // 线程池
await HtmlUiService.SwitchToGameThread(); // 回游戏线程
// 此后才能触碰 TaleWorlds API / 框架游戏侧状态
```

框架兜底：request 的**结果处理与清理**已由 `HtmlUiBridge.ExecuteRequestOnGameThread` 通过 `GameThreadTaskScheduler` 回投游戏线程，handler 的**同步起始段**也在游戏线程。handler 内部的 await 是 Consumer 自己的责任。

### 1.2 唯一 Owner 清单

| 职责 | Owner | 说明 |
|---|---|---|
| Host 可见性与输入裁决 | `HtmlUiInputCoordinator` | Page + Surface 组合状态 → 唯一输出 |
| 输入模式应用 | `HtmlUiInputControllerPatch` | Harmony prefix 完全接管 `Host.SetInputMode` |
| 游戏侧输入屏蔽 | `HtmlUiInputBlocker` | 仅 InputControllerPatch 可开启；异常路径必须释放 |
| 窗口事实 | `HtmlUiWindowTracker` | 位置/状态/最小化，不参与 UI 业务 |
| Page 生命周期 | `HtmlUiPageManager` | 独占式页面；**不再直接操作 Host 可见性** |
| Surface 生命周期 | `HtmlUiSurfaceManager` | 注册/显示/抑制/聚合 |
| Surface 资源服务 | `HtmlUiHost.OnWebResourceRequested` | `__surface/` 同域映射 |
| 布局 | Surface 自身 CSS | Framework 不做布局计算 |
| Browser policy | `HtmlUiHost` | 导航白名单、资源映射 |

### 1.3 强制规则

1. 禁止 Consumer 直接调用 `HtmlUiService.SetInputMode`（有 Surface 注册时会告警）；用 `InputDemand` 表达需求。
2. 禁止操作 HWND、创建第二套窗口同步/轮询机制。
3. 禁止 Consumer 在 Framework 层新增 Harmony Patch。
4. Surface 必须通过 `HtmlUiConsumerScope.RegisterSurface` 注册（直接 `Surfaces.Register` 会被拒绝）——否则模块卸载时无法回收。
5. 未实机验证的功能不得标记为已修复。

---

## 2. Public API

### 2.1 Page（独占式，行为不变）

```csharp
var scope = HtmlUiService.CreateScope("MyMod");
scope.RegisterContentRoot("ui", uiRoot);
string pageId = scope.RegisterPage(new HtmlUiPage("settings", "index.html")
{
    DefaultInputMode = HtmlUiInputMode.Captured,
    CloseOnEscape = true
});
HtmlUiService.Pages.Open(pageId);
HtmlUiService.Pages.Close(pageId);
```

### 2.2 Surface（并行叠加，新增）

```csharp
var scope = HtmlUiService.CreateScope("MyMod.Hud");
scope.RegisterContentRoot("hud", hudRoot);
string id = scope.RegisterSurface(new HtmlUiSurface("missionhud", "index.html")
{
    ContentRootId = "hud",
    ZIndex = 200,
    InputDemand = HtmlUiInputMode.Passive   // 需求，不是结果
});

HtmlUiService.Surfaces.Show(id);
HtmlUiService.Surfaces.Hide(id);
HtmlUiService.Surfaces.SetInputDemand(id, HtmlUiInputMode.MouseCaptured);
HtmlUiService.Surfaces.SetZIndex(id, 300);
HtmlUiService.Surfaces.IsVisible(id);          // 实际显示状态（含抑制判断）
HtmlUiService.EffectiveInputMode;              // 当前聚合结果
```

**Surface 文档内 JS**（`runtime.js` 自动注入，含 iframe）：

```js
const s = game.surface();
s.isSurface(); s.isInputOwner();
await s.setVisible(false);
await s.requestInput('MouseCaptured');  // 仅表达需求
await s.setZIndex(300);
game.state.subscribe('myKey', render);  // 与 Page 相同
```

### 2.3 共存策略（Framework 级，不可协商）

- **Page 打开** → 所有 Surface 进入 `Suppressed`（`Closed` 回调触发），定义与 state 保留；
- **Page 关闭** → Surface 自动恢复（`Opened` 回调触发），Consumer 零参与；
- **无 Page** → Host 可见性与输入完全由 Surface 集合决定；
- **无 Page 且无可见 Surface** → `Hidden`。

回调语义：`Opened/Closed` 跟随**实际显示状态**翻转，每次状态变化恰好触发一次——被抑制期间 `Show()` 不会误报 `Opened`。

### 2.4 输入模型（v1）

```
聚合强度：Hidden(0) < Passive(1) < MouseCaptured(2) < Captured(3)
仲裁：强度 > ZIndex > 最近变更；结果写入 framework.surfaces.aggregate.inputOwnerId
```

**v1 限制（必读）**：输入是整窗的，不是分区的。任一 Surface 请求 `MouseCaptured/Captured` 时整窗不穿透；shell 中只有 inputOwner 的 iframe 是 `pointer-events:auto`。可交互 Surface 应设计为短暂状态。

**游戏侧屏蔽**：Bannerlord 轮询 `TaleWorlds.InputSystem.Input`，不看 Win32 消息归谁。`HtmlUiInputBlocker` 在 overlay 持有输入期间对 `IsKeyDown / IsKeyPressed / IsKeyReleased / IsKeyDownImmediate` 做前缀拦截（已对照 1.5.0 反编译源码确认四个方法均为无重载静态方法）。`Passive/Hidden` 永不屏蔽；所有异常路径强制释放。诊断字段：`framework.getDiagnostics` 的 `BlockingGameMouse / BlockingGameKeyboard`。

**⚠️ 硬性边界：Chromium 激活与鼠标输入（实测结论，勿再尝试绕过）**

> **WebView2（Chromium）只有在自身窗口处于激活（foreground）状态时才派发鼠标输入事件。** 经四轮方案实测（禁用子窗口激活 `WS_EX_NOACTIVATE`、`SetForegroundWindow` 矫正、`AttachThreadInput` 强制矫正、`MoveFocus(Programmatic)`）全部失败，从不同方向撞上同一堵墙：
>
> 1. `WS_EX_NOACTIVATE` 加在 Chromium 子窗口上：点击不再激活，但 Chromium 同时**丢弃全部鼠标事件**（连 `pointermove` 都不产生）；
> 2. 激活后把前台强制拉回游戏：Chromium 失去激活状态，随后的点击同样被丢弃；
> 3. 游戏会在失焦后 1–3 秒内**主动抢回前台**（无边框窗口模式同样存在），任何一次性矫正都会被覆盖。
>
> **结论**：`MouseCaptured/Captured` 期间，overlay 必然周期性或持续持有前台。游戏侧靠轮询（`Input.IsKeyDown` / `GetAsyncKeyState` 在游戏后台 tick 中照常工作）继续处理热键，不受失焦影响。**已知残留**：此期间 Alt+F4 会指向 overlay（WindowTracker 的重新显示兜底 + ProcessRecovery 缓解，根治需 v2 独立交互窗口）。
>
> **v2 根治方向**：Framework 提供独立的"交互窗口"（仅交互 Surface 使用、与穿透显示窗口分离），或彻底研究 Bannerlord 原生 Gauntlet 的输入屏蔽机制。

**诊断工具**：
- DevTools 默认开启（**F12**）；右键菜单被框架抑制。发布前如需关闭，改 `HtmlUiContextMenuPatch.Install` 中的 `host.DevToolsEnabled`；
- Consumer 页面可加输入探针（`pointerdown/pointermove` 计数 + `document.hasFocus()` 轮询，经 `clientLog` 打到 `BannerlordHtmlUI.log`）——TacticalMap 与 CustomSkill 页面已有现成示例；
- **任何需要交互的 Page 必须设 `DefaultInputMode = Captured`**——框架自带的 diagnostics 页曾因缺省 Passive（整窗穿透）而"打开后关不掉"，已修复。

---

## 3. 诊断

游戏内 F10 打开诊断页。Surface 相关字段：

| 字段 | 含义 |
|---|---|
| `SurfaceCount / VisibleSurfaceCount` | 注册数 / 实际显示数 |
| `ShellActive` | 当前文档是否为 Shell |
| `SurfacesSuppressedByPage` | 是否处于 Page 主导抑制 |
| `SurfaceInputOwner` | 当前持有交互的 Surface id |
| `BlockingGameMouse / BlockingGameKeyboard` | 游戏侧输入屏蔽状态 |
| `SurfaceSummary` | 每个 Surface 的 owner/z/可见性/需求一行清单 |

排查口诀：Surface 不显示 → 先看 `SurfaceCount`（没注册）→ `SurfacesSuppressedByPage`（被 Page 压制）→ `VisibleSurfaceCount`（Enabled=false 或 Hide）→ `ShellActive`（导航是否到位）。

---

## 4. 已知问题与教训（Bug 知识库）

| 问题 | 根因 | 修复/预防 |
|---|---|---|
| MouseCaptured 收不到任何鼠标 | `SetMouseOnly` 未清 `WS_EX_TRANSPARENT`，窗口被命中测试跳过 | 已修：启用时一并清除。教训：**Win32 扩展样式是整窗原子属性，切换模式必须成对处理** |
| 点 HUD 时游戏同时响应 | Bannerlord 轮询输入而非消费消息 | 已修：`HtmlUiInputBlocker`。教训：**让窗口不透明只解决了一半输入问题** |
| Surface 全部不挂载（曾有） | C# 属性 PascalCase 序列化 vs JS camelCase 读取 | 已修：state 载荷统一小写匿名对象。教训：**跨语言契约没有编译器兜底，序列化形状必须显式声明** |
| Page 关闭后 Surface 消失（曾有） | PageManager 关闭路径无条件 `Hidden+Hide` 覆盖协调器 | 已修：Host 状态唯一归协调器。教训：**两个 Owner 写同一状态必然漂移** |
| request handler await 后线程错误 | 游戏线程无 SyncContext | 已修：结果处理回投游戏线程 + `SwitchToGameThread()`。教训：**线程契约不能只靠文档** |

---

## 5. 当前状态与回归清单

### 5.1 已实现待实机验证

- [ ] 双 Surface（TacticalMap + HUD）并行显示，互不关闭
- [ ] Page 打开/关闭时 Surface 抑制与自动恢复（含回调各触发一次）
- [ ] Passive 下游戏输入完全正常（底线）
- [ ] MouseCaptured 下点地图：地图响应、游戏不响应；切回 Passive 后鼠标立即恢复
- [ ] 强制打断（Alt+Tab / 关页面 / 杀 WebView2）后输入不卡死
- [ ] WebView2 重载后 Surface 自动重挂载、state 不丢
- [ ] `Hide→Show` 同一 Surface，其 JS/DOM 状态保留

### 5.2 v2 路线

1. 区域级命中路由（解除整窗输入限制，API 不变）；
2. 鼠标移动（视角）屏蔽；
3. Page 与 Surface 共存策略扩展位（`Coexist`）。

---

## 6. 文档索引

| 文档 | 内容 |
|---|---|
| 本文件 | 架构、Owner、API、线程契约、回归 |
| `BannerlordHtmlUI/docs/SURFACES.md` | Surface 设计全案（数据模型/策略/挂载选型/v2 路线） |
| `docs/FRONTEND_*.md` | 前端 Runtime/Binding/组件参考（仍有效） |
| `docs/CHANGELOG*.md` | 版本历史 |
| `docs/过期文档/` | 被 Surface 架构取代的旧输入/路由文档 |
