# Stability Hardening Progress — 2026-08-15

分支：`dev`

## 本轮已落库

### P0

- ESC close 生命周期接线已补齐：`HtmlUiHost` 在 WebView2 ready 后真正安装 `HtmlUiKeyboardAndDiagnosticsPatch`。
- ESC / diagnostics global `IMessageFilter` 在 Framework Host Dispose 时卸载。
- `MainWindowHandle == 0` 的活动 Overlay 隐藏竞态已有稳定性 guard。

### P1

- `HtmlUiBridge` 现在记录 `requestId -> ownerId/requestName`。
- `HtmlUiConsumerScope.Dispose()` 已在注销 owner 资源前主动取消 owner 活动 Request。
- 单个 `UnregisterRequest(name, owner)` 只取消对应 Request，不影响同 owner 的其他 Request。
- Request 注销前后各做一次 targeted cancellation，覆盖 active-set 进入竞态。
- `preCanceledRequests` 改为带时间戳的 tombstone，并加入 30 秒 TTL 与 2048 条上限清理，避免无界累积。
- HotReload 增加生命周期 gate：无当前可见页面时 watcher 触发的 `Reload()` 不再重新加载隐藏页面。
- HotReload 增加 75ms debounce，抑制 FileSystemWatcher 连续 Changed/Created 事件产生的 reload storm。
- `State.Remove` 已改为独立 `state-remove:<key>` 协议事件。
- Framework document-created runtime patch 会捕获内部 state `Map`，收到 `state-remove:<key>` 后执行真实 `Map.delete(key)`。
- 删除后仍通过原 `state:<key>` 订阅链通知绑定/监听器，因此既保持 `state.has(key) == false`，又不会破坏现有绑定刷新语义。

## 仍未完成

- Request `requestCancellable()` 当前仍通过临时替换 `webview.postMessage` 捕获 requestId；尚未完成协议层直接暴露 requestId 的重构。
- WebView2 `ProcessFailed` 尚未形成完整恢复状态机；当前仍主要记录错误并通知 `BrowserError`。
- `EnsureUiThread` disposed-host failure semantics 仍待收口。
- Page / HotReload / WebView2 reinitialize 之间的完整生命周期回收仍需继续审计。
- Consumer `net472/net6` target、历史 cancellation extension、Framework 内测试型 F10 hook 仍待发布前清理。

## 测试状态

本轮只做静态代码审查与代码落库，**没有要求用户进行实机测试**。

在 P0/P1 代码收口之前，不将 Bannerlord 实机验收标为通过。
