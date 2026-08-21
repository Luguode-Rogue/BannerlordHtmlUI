# 当前项目状态

当前开发线：`dev`。版本事实以 `BannerlordHtmlUI.csproj` 为准。

## 阶段

**Stability Hardening / API Stabilization / Consumer Regression**。

## Consumer 基线

### CustomSkill

已有实机成功链路：

```text
M → Captured → ESC → Hidden → 再次打开
```

仍需压力回归，不把一次成功视为最终通过。

### TacticalMap

当前目标语义：

```text
CompactPassive
→ 右上角区域
→ 完全只读
→ HTML 不拥有键鼠输入

FullInteractive
→ 全窗口
→ HTML 才允许输入
```

右键、F12、ESC、鼠标、键盘仍需实机确认。

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
- 通用 Overlay Layout
- Browser policy
- Diagnostics / Input Trace / Hang Watchdog

## 当前风险

### P0

- InputMode 必须保持唯一 owner：`HtmlUiInputControllerPatch`。
- Page `Closed` callback 的线程契约必须落实 GameThread 边界。
- Request `await` 后访问 Bannerlord API 必须显式回 GameThread。

### P1

- 删除 Host 遗留 `100ms FollowTimer` / `FollowBannerlordWindow()`。
- WindowTracker 事件同步增加 coalescing。
- 收口 InputController / WindowTracker / OverlayForm 对 Visible/Foreground 的职责。
- 消除 `RunOnUiThreadSync` / `InitializeGate.Wait()` 等同步跨线程等待。
- DevTools/browser policy 保持单一 owner，内部 `openDevTools` 也服从同一策略。

## 卡死

最近实机回归中，Framework-only 启动阶段长时间卡死**当前未再次触发**。保持为“未复现”，不写成已修复。

## 最低回归

```text
Framework-only startup
→ WebView2 Ready
→ Consumer Register
→ Page Open
→ ESC
→ Page Close
→ Reopen
→ Shutdown
```

```text
Hidden / Passive / Captured / MouseCaptured
→ mouse / keyboard / ESC / F12 / right click / Alt+Tab
```

### Consumer

```text
CustomSkill → M / ESC / reopen stress
TacticalMap → CompactPassive right click / F12 / ESC / mouse / keyboard
TacticalMap → FullInteractive mouse / keyboard / ESC
```

## 状态原则

未实际测试的项保持“未验证”；静态代码审查不能替代 Bannerlord 实机验收。
