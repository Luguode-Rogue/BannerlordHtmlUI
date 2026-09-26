# Surface 架构设计（v1）

> 状态：设计稿，待评审后进入实现（S1–S6）。
> 面向：Framework 维护者 + 第三方 Consumer 作者。

---

## 1. 为什么需要 Surface

现有 `HtmlUiPageManager` 是**独占式**模型：`Open()` 会先 `CloseCurrentInternal()` 再导航，同一时刻只有一个 `CurrentId`。它能注册多个 Page，但"注册多个"不等于"同时显示"。

实际冲突场景（已在 `New_ZZZF` 复现）：战场 HUD 与战术地图需要同时存在，但两者都是 Page，互相抢占；且被抢占方的本地状态（如 `_pageOpened`）不会收到 Framework 通知，导致自动重开逻辑失效、UI 静默消失。

问题的根因不是"Page 不够多"，而是 **Framework 缺少"持续存在的叠加式 UI"这一抽象**。

---

## 2. 心智模型

| 对象 | 职责 | 典型用途 |
|---|---|---|
| **Page** | 独占式业务页面，整页导航 | 设置、诊断、编辑器、全屏配置界面 |
| **Surface** | 并行式叠加 UI，挂载在 Shell 内 | HUD、小地图、技能栏、Buff、战斗日志、目标信息 |
| **Host** | 浏览器与窗口基础设施（单 WebView2、单 OverlayForm、导航、线程） | — |
| **InputCoordinator** | 唯一输入裁决者 | 由 Page + Surface 组合状态计算 EffectiveInputMode |
| **WindowTracker** | 唯一窗口事实 Owner | 只管 Bannerlord 窗口位置/状态 |

**一句话**：Page 管"切换场景"，Surface 管"叠加显示"。两者共享同一个 Host 与 WebView2。

**唯一 Owner 原则不变**：
- 输入只有一个 Owner（`InputCoordinator`），Surface 只**提出需求**；
- 窗口只有一个 Owner（`WindowTracker`）；
- Consumer 不得操作 HWND、鼠标捕获、或自建第二套窗口同步机制。

---

## 3. Surface 数据模型

```csharp
public sealed class HtmlUiSurface
{
    public string Id { get; }              // 必需，模块内唯一（框架会加 owner 前缀）
    public string RelativePath { get; }    // 必需，HTML 入口，位于 ContentRoot 内
    public string ContentRootId { get; set; } = "ui";
    public string OwnerId { get; internal set; }      // 由 ConsumerScope 注入
    public int ZIndex { get; set; } = 100;            // 层级，同时用于交互仲裁
    public HtmlUiInputMode InputDemand { get; set; } = HtmlUiInputMode.Passive; // 需求，非结果
    public bool Enabled { get; set; } = true;         // false = 不参与渲染与输入聚合
    public Action Opened { get; set; }
    public Action Closed { get; set; }
}
```

**关键字段说明**

- `InputDemand` 是**需求**而非结果。Surface 永远不直接设置 Host 的输入模式。
- `ZIndex` 同时承担**显示层级**与**交互仲裁优先级**（见 §6）。
- **不设 Bounds / LayoutHint**：Surface 的布局由自身 CSS 决定，Framework 不做布局计算（窗口几何唯一属于 `WindowTracker`）。
- **不设 Visible 作为公开可写字段**：可见性由 Framework 通过 `Show/Hide` 管理，避免 Consumer 直接改状态绕过协调器。

---

## 4. 生命周期

| 阶段 | 触发 | Framework 行为 | 原则 |
|---|---|---|---|
| Register | Consumer 初始化 | 登记定义，不挂载 DOM | 一次性 |
| Show | `Surfaces.Show(id)` | 挂载/显示，重算输入与可见性 | **不触碰其他 Surface** |
| Hide | `Surfaces.Hide(id)` | 隐藏，重算输入与可见性 | **不影响其他 Surface** |
| UpdateState | Consumer 主动推 | 走现有 State/事件通路 | 保持线程边界 |
| Unregister / Scope.Dispose | 模块卸载 | 移除 DOM、退订、清 state | Framework 负责，不残留 |
| WebView 重载/恢复 | Framework 内部 | 重放挂载集合 + state 快照 | Surface 不自己创建第二 Host |

**状态与可见性解耦**：Hide 一个 Surface **不清除**它的 state。重放时 state 从 `HtmlUiStateStore` 快照恢复，Consumer 无需重新推送。

---

## 5. Page 与 Surface 共存策略（v1 定义）

这是必须**在 Framework 层统一定义**的策略，不允许 Consumer 自行决定。

**规则：**

1. **Page 打开时，Framework 隐藏所有 Surface**（`PageDominant` 策略）。
   - 理由：Page 是独占式语义，打开 Page 意味着用户进入"全屏任务"，叠加 UI 应当让位。
   - 行为：Surface 定义与 state **保留**，仅停止显示；Page 关闭后按原集合恢复，无需 Consumer 重建。
2. **Page 关闭后**，Framework 自动回到"Surface 模式"，按 Surface 集合重算可见性与输入。
3. **没有 Page 打开时**，Host 的可见性完全由 Surface 集合决定（见 §6）。
4. **没有 Page 也没有可见 Surface 时**，Host 进入 `Hidden`。

> 该策略以常量形式暴露（`HtmlUiSurfacePolicy.PageDominant`），为 v2 的 `Coexist`（Page 与 Surface 共存）留出扩展位。

**对现有行为的破坏点**：`HtmlUiPageManager.Unregister/CloseCurrentInternal` 目前无条件调用 `_host.Hide()` 与 `SetInputMode(Hidden)`。改造后这两处改为**通知协调器**，由协调器按 §6 计算。这是 S2 的核心内容，也是唯一需要回归测试的现有行为变更。

---

## 6. 输入模型

### 6.1 聚合规则

每个**可见且 Enabled** 的 Surface 提出 `InputDemand`，Framework 取"最强需求"作为 `EffectiveInputMode`：

```
Hidden(0) < Passive(1) < MouseCaptured(3) < Captured(2)   // 按"强度"排序，非枚举值
```

| Surface 集合状态 | EffectiveInputMode |
|---|---|
| 无可见 Surface（且无 Page） | `Hidden` |
| 全部 Passive | `Passive` |
| 至少一个 MouseCaptured，无 Captured | `MouseCaptured` |
| 至少一个 Captured | `Captured` |

### 6.2 仲裁

多个 Surface 同时提出 `Captured`/`MouseCaptured` 时：

1. `ZIndex` 高者优先；
2. `ZIndex` 相同则**最近一次 Show/Request 者**优先；
3. 仲裁结果记入 trace 日志与诊断面板（`framework.surfaces.inputOwner`）。

### 6.3 v1 限制（必须写进 Consumer 文档）

> **输入模式是整窗的，不是分区的。**
>
> 只要有一个 Surface 请求 `MouseCaptured` 或 `Captured`，整个 overlay 进入不穿透状态，其余所有 Surface 的可见像素同样会拦截鼠标。CSS `pointer-events: none` 只影响 DOM 事件，**不影响 Win32 命中测试**。
>
> 因此：
> - 只读 Surface（HUD / Buff / 日志 / 目标信息）应始终声明 `Passive`，这是 v1 完全支持的场景；
> - 可交互 Surface 在持有交互期间会**独占整窗鼠标**，应设计为短暂状态（如小地图的点击下令），用完立即回到 `Passive`；
> - 不要在 v1 中同时显示两个可交互 Surface。

该限制的成因见 §10。

### 6.4 游戏侧输入屏蔽

让 overlay 不再透明只是**一半**。Bannerlord 通过 `TaleWorlds.InputSystem.Input` **轮询**输入，而不是依赖"哪个窗口收到了 Win32 消息"。因此即便 overlay 拿到了鼠标，游戏依然会观察到同一次点击并作出反应——点小地图的同时角色挥砍。

所以 v1 新增 `HtmlUiInputBlocker`：在 overlay 持有输入期间，对 `Input.IsKeyDown` / `IsKeyPressed` / `IsKeyReleased` 做前缀拦截。

| 模式 | 屏蔽鼠标 | 屏蔽键盘 |
|---|---|---|
| `Hidden` / `Passive` | 否 | 否 |
| `MouseCaptured` | 是 | 否 |
| `Captured` | 是 | 是 |

规则：

- 只有 `HtmlUiInputControllerPatch`（唯一输入 Owner）能开启屏蔽；
- 任何异常路径（窗口未就绪、游戏窗口未解析、`Hidden`、`Passive`）**都必须释放屏蔽**——把游戏输入卡住是灾难性故障；
- `HtmlUiInputBlocker.Enabled` 可整体关闭，仅用于排查；
- 鼠标**移动**（视角转动）不在 v1 屏蔽范围内，见 §10。

> **附带修复**：`HtmlUiOverlayForm.SetMouseOnly()` 原先只改 `WS_EX_NOACTIVATE`，没有清除 `WS_EX_TRANSPARENT`。从 `Passive` 切到 `MouseCaptured` 时窗口仍被命中测试跳过，`WndProc` 收不到 `WM_NCHITTEST`，导致 `MouseCaptured` **实际收不到任何鼠标**。现已一并清除。

---

## 7. API

### 7.1 C#

```csharp
// 注册（在 ConsumerScope 内，自动带 owner 前缀）
var scope = HtmlUiService.CreateScope("MyMod.Hud");
scope.RegisterContentRoot("hud", uiRoot);
string id = scope.RegisterSurface(new HtmlUiSurface("missionhud", "index.html")
{
    ContentRootId = "hud",
    ZIndex = 200,
    InputDemand = HtmlUiInputMode.Passive
});

// 控制
HtmlUiService.Surfaces.Show(id);
HtmlUiService.Surfaces.Hide(id);
HtmlUiService.Surfaces.SetInputDemand(id, HtmlUiInputMode.MouseCaptured);
HtmlUiService.Surfaces.SetZIndex(id, 300);

// 查询
HtmlUiService.Surfaces.IsVisible(id);
HtmlUiService.Surfaces.All;                 // 只读快照
HtmlUiService.EffectiveInputMode;           // 当前裁决结果
```

**与 Page API 保持对称**：`Pages.Open/Close` ↔ `Surfaces.Show/Hide`。现有 Page API **不删除、行为不变**。

### 7.2 JS（Surface 内）

```js
const surface = game.surface();              // 当前 Surface 句柄
surface.state.subscribe('missionHud', render);
surface.setVisible(false);
surface.requestInput('MouseCaptured');       // 仅表达需求
surface.zIndex;                              // 只读
```

`game.state`、`game.call`、`game.request` 在 Surface 内与 Page 内行为一致（同域 iframe + `runtime.js` 自动注入）。

---

## 8. Shell 与挂载机制

### 8.1 结构

WebView2 始终加载 Framework Shell，Surface 由 JS Runtime 动态挂载：

```html
<div id="ui-root">
  <iframe data-surface="New_ZZZF.TacticalMap.tacticalmap"></iframe>
  <iframe data-surface="New_ZZZF.MissionHud.missionhud"></iframe>
</div>
```

### 8.2 为什么是同域 iframe

在“同 DOM / Shadow DOM / iframe”方案中，当前实现选择**独立完整 iframe 文档**：

| 方案 | CSS/JS 隔离 | 崩溃隔离 | 通信 | Consumer 心智 |
|---|---|---|---|---|
| 同 DOM | ✗ | ✗ | 最好 | 需写"片段"而非完整页面 |
| Shadow DOM | 部分 | ✗ | 好 | 需写片段 |
| 跨 origin iframe | ✓ | ✓ | postMessage，复杂 | 完整页面 |
| **独立 iframe** | ✓ | ✓ | 每个文档注入 runtime，经 WebView2 bridge 通信 | **完整页面** |

**资源如何实现（当前实现）**：Surface 与 Page 一样使用已注册 ContentRoot 的
虚拟主机，例如：

```
https://bannerlord-htmlui-<content-root>.local/<relativePath>?__bannerlord_htmlui_owner=...&__bannerlord_htmlui_surface=...
```

这会直接映射到对应 Consumer 的磁盘目录。不得恢复已废弃的 `__surface/`
`WebResourceRequested` 通道：WebView2 不保证对子框架导航触发该拦截，实际会产生
iframe 错误页。Page 共存时，Framework 为 Surface 选择与当前 Page 相同的 ContentRoot
虚拟主机 URI，因此共存 iframe 保持同源。

**关键机制**：`AddScriptToExecuteOnDocumentCreated` 对 iframe 同样生效，`runtime.js`
自动注入每个 Surface，`window.game` 开箱可用；状态和响应不依赖 `parent.game`。

### 8.3 与现有 ContentRoot 的关系

Page 与 Surface 统一使用“每 ContentRoot 一个虚拟域名”的映射。进程恢复创建新
CoreWebView2 后，Framework 必须重放所有已注册 ContentRoot 映射。

---

## 9. 恢复与重放

WebView2 进程崩溃、页面刷新、HotReload 后：

1. Host 重新加载 Shell；
2. Shell 的单例 `game.ready()` 拉取带 revision 的状态快照，恢复全部 state；
3. Framework 按内部维护的 Surface 集合重放挂载（顺序按 ZIndex），恢复可见性与 `InputDemand`；
4. 重算 `EffectiveInputMode`。

**唯一真相源**：Framework 侧的 Surface 集合（C#），不是 DOM。Consumer 不需要做任何恢复处理。

---

## 10. v2 路线：区域级命中路由

**第二项已在 v1 完成**（见 §6.4）：鼠标/键盘按键已能被可靠屏蔽，交互 Surface 现在真正可用。

v2 只剩第一项：

**Win32 命中测试无法读取 DOM 元素**（`docs/INPUT.md:58-62` 已记录）。需要 Framework 维护每个 Surface 的命中区域，在 `WM_NCHITTEST` 按坐标返回 `HTCLIENT` / `HTTRANSPARENT`。难点：区域由 CSS 决定，JS→C# 上报存在延迟、DPI 缩放、resize 竞态。

v2 完成它之后 §6.3 的整窗限制解除，且 **API 不需要变化**——`InputDemand` 语义从"整窗"升级为"区域"，对 Consumer 透明。

**v2 待办：鼠标移动。** v1 只屏蔽按键。overlay 持有鼠标期间拖动仍会转动游戏视角。需要拦截移动量（难点：Bannerlord 读取移动量的具体入口需实测确认，可能是 `Input` 的鼠标增量 API，也可能是直接读光标位置差）。

---

## 11. 对第三方 Consumer 的约束

1. Surface 必须通过 `HtmlUiConsumerScope` 注册，禁止绕过（否则模块卸载时无法回收）。
2. 禁止直接调用 `HtmlUiService.SetInputMode`；有输入需求用 `SetInputDemand`。
3. 禁止操作 HWND、创建第二套窗口同步或轮询机制。
4. 只读 Surface 必须声明 `Passive`，并在 CSS 中对纯展示元素加 `pointer-events: none`。
5. Surface 的 state key 沿用 owner 前缀，由 `scope.SetState` 自动处理。

---

## 12. 防误用与可观测性（"给别人用"的硬要求）

**主动校验**（抛带修复指引的异常，而非静默失败）：

- 重复 Surface Id；
- Show 一个未注册 / 已卸载 owner 的 Surface；
- `InputDemand` 与 Surface 声明的能力不匹配；
- Consumer 直接调用 `SetInputMode`（记录 warning + trace）；
- owner scope 未 Dispose 就重复创建。

**诊断面板**（`diagnostics` 页新增 Surface 区块）：

- 当前 Surface 集合：Id / Owner / 可见性 / ZIndex / Enabled；
- 每个 Surface 的 `InputDemand` 与全局 `EffectiveInputMode` 的推导过程；
- `framework.surfaces.inputOwner`：当前谁持有交互；
- 挂载失败 / JS 异常的 Surface 级错误（不影响其他 Surface）。

**故障隔离**：单个 Surface 的 JS 异常、绑定死循环、资源加载失败，不得影响其他 Surface 与当前 Page。`HtmlUiHangWatchdog`、`HtmlUiProcessRecovery`、`HtmlUiBindingSchedulerPatch` 需按 Surface 粒度覆盖。
