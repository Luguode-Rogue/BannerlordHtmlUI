# 当前项目状态

> 当前开发线：`dev`
> Framework 工程版本来源：`BannerlordHtmlUI.csproj`。
> 本文是当前状态入口；旧 Handoff / Changelog 只作为历史证据。

## 项目阶段

Framework 当前处于 **Stability Hardening / API Stabilization / Consumer Regression**。

当前重点不是继续堆功能，而是：

1. 固化模块职责。
2. 消除多处重复状态 owner。
3. 完成 Framework 与 TacticalMap / CustomSkill 的真实回归。
4. 收口历史 Bug 的代码路径与验证记录。

## 当前模块边界

正式规范：

- `docs/FRAMEWORK_MODULE_MAP.md`
- `docs/CODE_PLACEMENT_RULES.md`
- `docs/ARCHITECTURE_MASTER.md`

以后修改 Framework 前必须先看这三份文档。

## 当前已完成能力

- WebView2 UI thread 初始化
- Framework Ready / Shutdown
- Page / ContentRoot
- Command / Request / Response / Event / State
- Request cancellation / AbortSignal / timeout / pagehide / runtime shutdown
- Consumer owner scope
- Binding / Component 生命周期基础设施
- i18n 基础链路
- Navigation race guard
- Overlay / InputMode 基础设施
- Event-driven WindowTracker
- 通用 Overlay Layout（FullWindow / TopRight）
- Browser policy（默认禁止右键菜单 / DevTools）
- Diagnostics / Input Trace / Hang Watchdog

## 最近 Consumer 基线

### CustomSkill

最近实机测试中：

- `M` 打开：正常
- Captured：正常
- ESC：正常关闭
- 关闭后重新打开：已有成功链路

仍按压力测试继续验证，不把一次成功当成最终绿灯。

### TacticalMap

当前目标语义：

```text
CompactPassive
    → 右上角区域
    → 完全只读
    → 不接收 HTML 键鼠输入

FullInteractive
    → 全窗口
    → 允许 HTML 鼠标交互
```

当前最新一轮代码已完成对应结构修改；需要实机确认右键、F12、ESC、鼠标输入是否完全符合该语义。

## 已确认的历史修复

- Navigation stale completion guard
- Page Id 初始化
- Owner + entry identity 注销保护
- Request cancellation / late-result protection
- Runtime Patch 的 CoreWebView2 UI-thread 安装
- State Remove compatibility
- Captured Overlay foreground 不应被窗口跟踪误判为失焦
- 100ms Legacy FollowTimer 已由 Event-driven WindowTracker 接管运行路径

## 当前仍需收口的结构问题

### P0

- Framework 输入状态必须保持单一 owner：`HtmlUiInputControllerPatch`。
- Page Closed callback 的线程契约必须明确并落实 GameThread 边界。
- Request `await` 后的 GameThread 契约必须明确；需要 Game API 时必须显式回 GameThread。

### P1

- Host 中旧 `100ms FollowTimer` / `FollowBannerlordWindow` 遗留代码彻底删除。
- WindowTracker 的事件同步需要 coalescing，避免窗口移动时重复 BeginInvoke。
- InputController / WindowTracker / OverlayForm 对 Visible/Foreground 的职责进一步收口，避免重复修改同一 native state。
- Host `RunOnUiThreadSync` / Service `InitializeGate.Wait()` 的同步等待逐步消除。
- DevTools/browser policy 必须保持单一 owner，内部 `openDevTools` 也必须服从同一策略。

## 最新卡死状态

最近的实机回归中，之前的 **Framework-only 启动阶段长时间卡死没有再次触发**。

这只能记为“当前版本未复现”，不能删除历史 Bug。Watchdog 和旧卡死日志继续保留在知识库中。

## 实机回归优先顺序

```text
Framework-only startup
→ CustomSkill M / ESC / reopen stress
→ TacticalMap CompactPassive
   → right click
   → F12
   → ESC
   → mouse / keyboard
→ TacticalMap FullInteractive
   → mouse / keyboard / ESC
→ Alt+Tab
→ minimize/restore
→ window move/resize
→ Framework shutdown
```

## 状态判定规则

静态代码审计不能替代 Bannerlord 实机验收。尤其是 WebView2、Overlay、Input Focus、Navigation Timing、长时间运行资源回落，必须实际验证后才能标记通过。

```text
当前代码 / 当前 .csproj
        ↓
最近一次真实实机验证
        ↓
PROJECT_STATUS.md
        ↓
旧 Handoff / Changelog（历史）
```
