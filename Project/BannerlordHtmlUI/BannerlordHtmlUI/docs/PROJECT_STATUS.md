# 当前项目状态

> 当前开发线：`dev`。版本事实以 `BannerlordHtmlUI.csproj` 为准。

## 项目阶段

**Stability Hardening / API Stabilization / Consumer Regression**。

当前重点：模块职责收口、历史 Bug 回归、Framework 与 TacticalMap/CustomSkill 实机验证。

## 当前 Consumer 基线

### CustomSkill

当前实机已有成功链路：

```text
M → Captured → ESC → Hidden → 再次打开
```

继续做压力回归，不把一次成功视为最终通过。

### TacticalMap

目标语义：

```text
CompactPassive
→ 右上角区域
→ 完全只读
→ HTML 不拥有键鼠输入

FullInteractive
→ 全窗口
→ HTML 才允许鼠标交互
```

仍需实机确认右键、F12、ESC、鼠标和键盘输入。

## 已确认能力

- WebView2 UI-thread 初始化
- Framework Ready / Shutdown
- Page / ContentRoot
- Command / Request / Event / State
- Request cancellation / AbortSignal / timeout / pagehide / runtime shutdown
- Consumer owner scope
- Binding / Component 生命周期基础设施
- i18n 基础链路
- Navigation race guard
- Event-driven WindowTracker
- 通用 Overlay Layout（FullWindow / TopRight）
- Browser policy（默认禁止右键菜单 / DevTools）
- Diagnostics / Input Trace / Hang Watchdog

## 当前高优先级风险

### P0

- InputMode 必须保持单一 owner：`HtmlUiInputControllerPatch`。
- Page `Closed` callback 的线程契约必须明确并落实 GameThread 边界。
- Request `await` 后需要 Bannerlord API 时必须显式回 GameThread。

### P1

- 删除 Host 中遗留 `100ms FollowTimer` / `FollowBannerlordWindow()`。
- WindowTracker 事件同步增加 coalescing。
- InputController / WindowTracker / OverlayForm 对 Visible/Foreground 的职责进一步收口。
- 消除 `RunOnUiThreadSync` / `InitializeGate.Wait()` 等同步跨线程等待。
- DevTools/browser policy 保持单一 owner，内部 `openDevTools` 也服从同一策略。

## 卡死状态

最近实机回归中，**Framework-only 启动阶段长时间卡死未再次触发**。记录为“当前版本未复现”，历史 Bug 不删除。

## 回归矩阵

### Smoke

```text
Framework-only startup
→ WebView2 Ready
→ Consumer Register
→ Page Open
→ ESC
→ Page Close
```

### Lifecycle

```text
Open → Close → Open
Open → Reload
Open → ESC
Pagehide → Reopen
Framework Shutdown
```

### Input / Overlay

```text
Hidden / Passive / Captured / MouseCaptured
→ mouse
→ keyboard
→ ESC
→ F12
→ right click
→ Alt+Tab
→ minimize/restore
→ window move/resize
```

### Consumer

```text
CustomSkill
→ M / ESC / reopen stress

TacticalMap
→ CompactPassive right click / F12 / ESC / mouse / keyboard
→ FullInteractive mouse / keyboard / ESC
```

### Stress

至少覆盖：StressLab Run 10、Run 50、多次 Open/Close、Reload、Binding/Component 长时间运行、Cancellation 高频运行。

## 状态判定

静态代码审计不能替代 Bannerlord 实机验收。未实际测试的项保持“未验证”，不得写成“已修复”。

```text
当前代码 / .csproj
        ↓
最近一次真实实机验证
        ↓
PROJECT_STATUS.md
        ↓
历史 Handoff / Changelog
```
