# Stability Hardening Progress — 2026-08-16

分支：`dev`

## 当前基线与原则

- 继续在 `dev` 上开发，不回退到 `main` / `trunk`。
- 已验证正常的 i18n、基础 Page/Overlay 行为不得因稳定性修复被回退。
- `F12` 不作为可靠关闭方案；正式验收以 ESC / 页面自身 Close 为准。
- Framework 默认日志保持低噪声，不恢复逐帧 Window Tracking 日志。

## 已落库

### P0

- ESC close 生命周期接线已补齐：`HtmlUiHost` 在 WebView2 ready 后真正安装 `HtmlUiKeyboardAndDiagnosticsPatch`。
- ESC / diagnostics global `IMessageFilter` 在 Framework Host Dispose 时卸载。
- `MainWindowHandle == 0` 的活动 Overlay 隐藏竞态已有稳定性 guard。
- Page transition race 已通过统一 transition 串行化与导航失败回滚处理。

### P1

- `HtmlUiBridge` 记录 `requestId -> ownerId/requestName`。
- `HtmlUiConsumerScope.Dispose()` 在注销 owner 资源前主动取消 owner 活动 Request。
- 单个 `UnregisterRequest(name, owner)` 只取消对应 Request。
- Request 注销前后各做 targeted cancellation，覆盖 active-set 进入竞态。
- `preCanceledRequests` 使用带时间戳 tombstone，并有 30 秒 TTL 与 2048 条上限清理。
- `State.Remove` 已改为独立 `state-remove:<key>` 协议事件；Runtime 对内部 State Map 执行真实 `Map.delete(key)`，同时继续通过 `state:<key>` 通知绑定。
- HotReload 增加生命周期 gate 与 75ms debounce，避免隐藏页面被 watcher 重新加载及 reload storm。
- HotReload Patch 已接入显式 `Uninstall()` 生命周期，Framework shutdown 时卸载。
- Binding Component 已避免对象 spread 导致 prototype / Symbol / non-enumerable 属性丢失。
- HWND 临时解析失败不再被视为 Framework shutdown 的充分条件。
- **cancellable requestId 捕获竞态已改为协议侧独立 request ID：**不再临时替换 `webview.postMessage`。Cancellation Patch 自己生成 requestId，直接发送 Request，并通过持久化 `__receive` wrapper 接收对应 Response；Abort / timeout / pagehide 均按该 ID 发送 cancel。

## 当前未完成

### P1

- WebView2 `ProcessFailed` 尚未形成完整恢复状态机；当前仍主要记录错误并通知 `BrowserError`。
- `EnsureUiThread` disposed-host failure semantics 仍待收口。
- Page / HotReload / WebView2 reinitialize 之间的完整生命周期回收仍需继续审计。

### P2

- Consumer `net472/net6` target 是否简化。
- 历史 cancellation extension 是否可以移除。
- Framework 内测试型 F10 / Diagnostics hook 是否下沉到 Consumer TestMod。
- Release build debug/test surface、默认日志与分支清理。

## 下一开发顺序

1. **WebView2 ProcessFailed recovery state machine**。
2. `EnsureUiThread` disposed-host failure semantics。
3. Page / WebView2 reinitialize 生命周期审计。
4. Binding / i18n / State / Request 组合回归入口完善。
5. Consumer TestMod 多 Page 长时间切换与 StressLab 验收。
6. v0.44 Public API / Protocol freeze。
7. 发布清理与 Release Candidate。

## 实机验收策略

代码收口前不要求用户反复测试。完成 P1 后统一做：

- F11 → 完整显示 → ESC。
- F11 → Open/Close/Reopen。
- 多 Page 快速切换。
- F8 StressLab Run 10 / Run 50 / 长时间运行。
- Cancellation：AbortSignal / timeout / pagehide / owner Dispose。
- Language Switch + dynamic i18n binding。
- Input Capture / Release。
- WebView2 ProcessFailed / recovery（若能稳定触发）。

## 当前结论

静态代码层面的主要 State / owner Request / pre-cancel / HotReload / requestId race 已完成；**当前最大的未完成结构性问题是 WebView2 ProcessFailed recovery 和 WebView2 reinitialize 生命周期**。在这两项收口前，不把 v0.44 标记为实机稳定发布版。

本文件记录的是开发状态，不等同于 Bannerlord 实机验收通过。
