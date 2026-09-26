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

### 1.1.1 跨桥刷新性能契约

**WebView2 的跨线程、跨进程脚本调用容易制造短暂帧时间尖峰。** `HtmlUiService.SendEvent` / `State.Set` 会把载荷序列化为 JavaScript，并通过 `ExecuteScriptAsync` 广播到顶层文档和所有存活 frame；高频调用可能显著拉低 1% Low，因此 Consumer 必须限制频率和载荷。

Consumer 必须遵守以下规则：

1. 禁止把逐帧变化、连续倒计时或自然回复值直接跨桥发送；应在浏览器侧使用本地时钟推进，只同步开始、结束、跳变和校准。
2. 高频状态必须拆成最小增量载荷，不得因一个标量变化重新构造、序列化并发送包含列表或大对象的完整状态。
3. 同一游戏帧内的多项变化必须先合并，再发送至多一条事件。
4. 大图片、二进制数据和大段文本必须经虚拟主机资源管线加载，禁止 Base64 塞入 state/event。
5. 动态集合必须设置数量上限、使用紧凑表示，并按实际显示需求限频；前端用 `requestAnimationFrame` 合并绘制，避免每个事件触发重复布局或整棵 DOM 重建。
6. `State.Set` 的非标量相等比较会产生 `JToken` 转换成本；高频且已自行去重的数据应使用轻量事件，完整 state 仅用于首次水合或低频结构变化。

Framework 侧保证：

- document-created runtime 与关键补丁全部注册完成后，Host 才进入 Ready 并允许首次导航；
- `game.ready()` 单例化，版本化快照不会覆盖并发到达的新状态；
- retained state 在同一个浏览器 UI flush 内按 key 合并，只投递最终值；
- 每个文档使用独立 `documentId`，广播响应不会被其他 iframe 误消费；
- `state.watch(key, handler)` 提供可靠的“首值 + 后续更新”入口。
- 新建或重新导航的 Surface iframe 会在导航完成后由 Framework 主动注入一次 retained-state 快照，避免“创建 iframe 的批次同时携带首个业务状态”时丢失首屏状态。

注意：state 是 latest-value 语义，不保证交付同一 flush 内的全部中间值。必须逐条处理的业务变化使用 event。倒计时、长按进度、平滑条等连续视觉过程仍应由 Consumer 在浏览器侧推进。

### 1.2 唯一 Owner 清单

| 职责 | Owner | 说明 |
|---|---|---|
| Host 可见性与输入裁决 | `HtmlUiInputCoordinator` | Page + Surface 组合状态 → 唯一输出 |
| 输入模式应用 | `HtmlUiInputControllerPatch` | Harmony prefix 完全接管 `Host.SetInputMode` |
| 游戏侧输入屏蔽 | `HtmlUiInputBlocker` | 仅 InputControllerPatch 可开启；异常路径必须释放 |
| 窗口事实 | `HtmlUiWindowTracker` | 位置/状态/最小化，不参与 UI 业务 |
| Page 生命周期 | `HtmlUiPageManager` | 独占式页面；**不再直接操作 Host 可见性** |
| Surface 生命周期 | `HtmlUiSurfaceManager` | 注册/显示/抑制/聚合 |
| Surface 资源服务 | `HtmlUiHost.SetVirtualHostNameToFolderMapping` | Surface URI = content root 虚拟主机 + 相对路径（与 Page 同机制）。**禁止改回 `__surface/` 通道**：WebView2 不对 iframe 子框架导航触发 WebResourceRequested，该通道在 iframe 内必然 404/错误页 |
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
    InputDemand = HtmlUiInputMode.Passive,  // 需求，不是结果
    CoexistWithPage = true                  // v2 Coexist：Page 打开时不被抑制，由框架直接挂载进页面文档
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

- **Page 打开** → 普通 Surface 进入 `Suppressed`（实际显示状态翻转时触发 `Closed`），定义与 state 保留；设置 `CoexistWithPage = true` 的 Passive Surface 是例外，由 Coexist 宿主挂入 Page 顶层文档；
- **Page 关闭** → 普通 Surface 自动恢复（实际显示状态翻转时触发 `Opened`）；Coexist Surface 随文档导航回 Shell 后由 shell.js 接管；
- **无 Page** → Host 可见性与输入完全由 Surface 集合决定；
- **无 Page 且无可见 Surface** → `Hidden`。

回调语义：`Opened/Closed` 跟随**实际显示状态**翻转，每次状态变化恰好触发一次——被抑制期间 `Show()` 不会误报 `Opened`。

### 2.4 输入模型（v1）

```
聚合强度：Hidden(0) < Passive(1) < MouseCaptured(2) < Captured(3)
仲裁：强度 > ZIndex > 最近变更；结果写入 framework.surfaces.aggregate.inputOwnerId
```

**v1 限制（必读）**：输入是整窗的，不是分区的。任一 Surface 请求 `MouseCaptured/Captured` 时整窗不穿透；shell 中只有 inputOwner 的 iframe 是 `pointer-events:auto`。可交互 Surface 应设计为短暂状态。

**v2 Coexist（已实现）**：`CoexistWithPage = true` 的 Passive Surface 在 Page 打开期间不被抑制。由于 Page 打开时 WebView 加载的是页面文档而非 Shell，框架通过 `HtmlUiCoexistHost`（document-created 脚本）把这类 Surface 以透明 `pointer-events:none` iframe 直接挂载进页面文档；回到 Shell 后由 shell.js 正常接管。桥接响应与 state 事件已广播到所有存活 frame（`HtmlUiHost.ExecuteScriptInAllDocuments`），iframe 内 runtime 收发消息与顶层文档一致。首个消费示例：`New_ZZZF.BattleHud`。

Coexist document-created 脚本只允许顶层 Page 文档挂载 Surface（`window !== window.top` 的子 frame 立即退出），防止某个 HUD iframe 再次嵌套挂载其他 HUD。战术地图属于 **Page**，不是 Surface；不要据此把它记入 Page/Shell 的并行 Surface 策略。

**2026-09-24 生命周期修复记录**：复现序列为 Page A 的同步 `Closed` 回调打开 Page B（或同一 Page 再开），旧关闭尾部继续发布 `closed`、恢复 Surface 或设置输入模式，可能覆盖新 Page；同时 A 的 UI 线程排队导航可能在快速切换后迟到执行。PageManager 现在先发布旧 Page 的 `closed` 再调用 Consumer 回调，按 transition revision 复核回调前后仍由本次打开拥有页面；同 id 已打开时 `Open` 幂等返回。Surface 恢复回调如打开了新 Page，Coordinator 后续跳过旧的 Surface 输入裁决。Host 的排队导航按 generation 和当前 Page 所有权复核，旧 Page 导航失效；WebView2 进程恢复重新建立 pending page generation，避免恢复页被旧导航检查误丢弃。Page/Shell 导航、Core 重配和 Dispose 会解绑已追踪 Frame 的 `Destroyed` 与 `NavigationCompleted` 事件；广播失败也移除追踪，迟到的状态水合会先确认 Frame 仍受追踪。

业务 Consumer 的暂停必须按来源管理，不能用一个共享 `bool`。战术地图和战斗 HUD 现在分别维护暂停 owner 集合；换装页、地形拍照、单张照片验证使用不同稳定 owner，只有集合从空变非空时隐藏，且全部 owner 释放后才允许 MissionTick 恢复。新增暂停来源时必须使用成对且相同的 owner 标识；兼容旧调用的无参重载只能作为单一 legacy 来源，不能混用来抵消具名来源。这样可避免“换装结束先释放暂停，但照片捕获仍在进行”时提前恢复 HUD/地图并重新进入输入或状态发布。

本轮还清理了 Consumer 的临时 retained state：M 技能页关闭时移除 `customSkill`，换装页结束时移除 `equipmentSession`；关闭页不应把大列表或会话对象留在全局快照中。HUD 的倒计时在 Surface 隐藏或页面失活时停止，重新显示时按原性能时间戳立即刷新 DOM；战术地图在隐藏时停止周期性 canvas 矩形上报。

以上 2026-09-24 生命周期和业务 UI 改动目前经过静态检查与脚本模拟，**尚未在游戏内完成本轮实机回归**。用户已验证上一轮 M 键打开后出现战场装备提示及技能状态变化卡顿的修复，该序列不再复现；这不代表下表中的其他页面已经验证。

**⚠️ Surface 文档强制约束**：根元素**禁止声明 `color-scheme:dark`**（或任何依赖 UA 默认画布色的写法）。规范规定根背景透明时画布使用"当前配色方案的 UA 默认色"——顶层文档有 WebView2 透明环境变量兜底不受影响，但 **iframe 子框架没有这层兜底**，画布会被填充为不透明深色，整个 Surface 变成一块盖住游戏的全屏色块（2026-09-06 实测踩坑）。所有颜色显式声明，不依赖 UA 控件样式。

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
| Surface iframe 全屏错误页 → 全屏色块（2026-09-06 三连坑） | ① `WebResourceRequested` **不对 iframe 子框架导航触发**，`__surface/` 资源通道在 iframe 内必然漏掉 → 错误页；② 桥接 `ExecuteScriptAsync` 只达顶层文档，iframe runtime 收不到任何响应/事件；③ iframe 文档 `color-scheme:dark` 时画布按配色方案被 UA 填充为不透明深色（顶层有透明环境变量兜底，iframe 没有） | 已修：① Surface URI 改走 content root 虚拟主机（与 Page 同机制）；② 响应/事件经 `FrameCreated` 追踪广播到全部存活 frame，各 runtime 按 pending-map 幂等结算；③ Surface 文档禁止 `color-scheme`，颜色全部显式声明。教训：**iframe 不是"小号顶层文档"——平台对子框架的资源拦截、消息投递、画布默认色都有独立行为，每个都要实测** |

---

## 5. 当前状态与回归清单

### 5.1 已实现待实机验证

- [ ] Passive 下游戏输入完全正常（底线）
- [ ] 强制打断（Alt+Tab / 关页面 / 杀 WebView2）后输入不卡死
- [ ] WebView2 重载后 Surface 自动重挂载、state 不丢
- [ ] `Hide→Show` 同一 Surface，其 JS/DOM 状态保留

### 5.2 2026-09-24 业务 UI 回归矩阵（本轮待实机）

| 业务 UI | 代码侧处理 / 预期行为 | 游戏内回归步骤与通过条件 |
|---|---|---|
| M 技能 Page | 关闭只由当前技能 Page 所有者处理；战场提示或技能状态变化不应让关闭后的技能任务继续发布状态 | 战场按 M 打开/关闭技能页，再触发装备提示与技能状态变化；UI 响应持续正常，关闭后技能状态停止更新 |
| 战术地图 Page | `Closed` 检查当前 Page 所有权；停止 native/cursor 资源；恢复等待无其他 Page 的 Tick，避免嵌套导航 | 打开战术地图、关闭，再立即打开另一 Page；地图不会错误隐藏新 Page，游标/输入恢复，关闭后地图不再上报 |
| 战斗 HUD Surface | 隐藏时暂停 cooldown/GCD interval；重新显示时依据原性能时间戳刷新剩余时间；Page 关闭后的恢复等待 MissionTick，任务结束清 capture | Page 覆盖/恢复 HUD，检查倒计时文字即时更新；任务结束后确认无残留捕获输入 |
| 战场换装 Page | 打开失败时关闭本页、清理 session state 并恢复其他 UI；关闭时移除临时 retained state | 正常打开/关闭及强制资源错误；不残留空白 Page、旧换装状态或覆盖层 |
| 技能提示 | 关闭时清理 `customSkill` retained state，避免旧提示状态持续进入后续完整快照 | 触发并关闭技能提示，再打开其他 UI；旧提示状态不重现，其他 state 正常 |

本矩阵中的实现目前仅静态审阅/脚本模拟；所有格都须在游戏内逐项确认后再标记通过。HUD 的隐藏后到期再显示需即时刷新 DOM，前端修正完成后再执行该项回归。

### 5.3 v2 路线

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
