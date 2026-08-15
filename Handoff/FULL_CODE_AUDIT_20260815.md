# BannerlordHtmlUI 全代码审查

日期：2026-08-15
分支：`dev`
审查范围：Framework 核心、WebView2 Host、Bridge、Page Manager、State、Localization、Binding、Request Cancellation、ConsumerScope、Runtime.js、TestMod、工程配置。

## 总结

当前代码已经具备完整的主体架构，但近期连续出现的正式环境问题暴露出一个共性：部分功能已经实现，却缺少严格的生命周期、线程边界和重建场景审计。

本次审查确认：

- P0 级窗口跟随问题已定位并增加稳定性补丁。
- P0 级 ESC 关闭链已定位；Overlay 层有处理器，Framework 全局 ESC filter 也已接入，但必须实机重新验证最终 DLL。
- WebView2 diagnostics hook 的旧 CoreWebView2 生命周期风险已定位并在当前 patch 中处理新实例替换。
- State Remove 在 JS runtime 中存在语义不完整问题：C# 删除后，JS 当前仍会收到 `state:key = null`，`state.has()` 仍可能返回 true。
- PageManager 的 Open/Close 存在并发交错风险，需要后续序列化状态转换。
- Request cancellation 当前通过临时替换 `webview.postMessage` 获取 requestId，存在并发竞态，应该在 runtime 协议层直接暴露 requestId。
- ConsumerScope.Dispose 不会取消所有属于该 owner 的活动 request，存在后台任务继续执行的资源风险。
- HotReload FileSystemWatcher 在页面关闭后仍可能继续触发 Reload；且文件写入可能产生多次 Changed 事件，需要 debounce/lifecycle gating。
- `EnsureUiThread` 对 disposed host 采取静默 return，会导致部分返回 Task 永久不完成，需要统一失败语义。
- TestMod 当前 target `net472;net6`，而 Framework 为 `net472`；作为 Bannerlord 实际运行模块没有必要把两个目标混在一起。
- `HtmlUiHostCancellableExtensions.cs` 已属历史兼容残留，当前 Host/Service 已直接提供 cancellation overload，可以清理。

## P0 已处理/正在验证

### 1. MainWindowHandle == 0 错误隐藏 Overlay

旧逻辑在 `FollowBannerlordWindow()` 中遇到 `Process.GetCurrentProcess().MainWindowHandle == IntPtr.Zero` 时直接 Hide Overlay。

正式环境日志已经证明这会出现：

```text
Page loaded
→ MainWindowHandle == 0
→ visible=false
→ Overlay disappears
```

当前 `HtmlUiWindowTrackingPatch` 对“页面仍请求显示但游戏 HWND 临时为 0”的情况跳过一次跟随更新，不再直接隐藏活动 Overlay。

后续需要实机确认快速切焦点/场景切换时仍能恢复正确 bounds。

### 2. ESC Close 链

用户明确验证过 F12 不可用，因此正式测试入口只使用 ESC。

当前 Overlay Form 提供 `ProcessCmdKey` → `EscapePressed`；Framework 还提供全局 WinForms `IMessageFilter` 作为独立 ESC 路径。

关键要求：

```text
ESC
→ Framework UI thread
→ Pages.CloseCurrent()
→ InputMode = Hidden
→ Overlay Hide
```

必须实机重新确认最新 DLL 已包含：

```text
Overlay ESC callback wired.
Global UI ESC close filter installed.
UI keyboard close detected: key=Escape
```

## P1 结构性问题

### 3. PageManager Open/Close 不是完整的原子状态转换

`Open()` 在 `CloseCurrent()`、设置 `_openId`、发布 lifecycle、Navigate 之间没有统一的 transition generation。

并发 Open/Close 可能出现：

- 新页面已打开，旧 Close 随后又 Hide Overlay。
- lifecycle `closed` 晚于新页面 `opening` 发布。
- navigation guard 只解决 WebView2 navigation completion，不能完全解决 PageManager 状态顺序。

建议：引入单调递增 `transitionId`，所有 close/open 发布和 Hide 动作校验当前 transition。

### 4. ConsumerScope.Dispose 不取消 owner 活动 Request

Scope Dispose 会注销 request 名称，但活动 handler 仍可能继续运行到完成，只能在完成时通过 `IsCurrentRequest` 发现已注销。

建议：Bridge 维护 `activeRequestId -> ownerId/requestName` 映射，Scope Dispose 时按 owner cancel。

### 5. Request cancellation 的 requestId 获取方案存在竞态

`HtmlUiRequestCancellationPatch` 通过临时替换 `webview.postMessage` 捕获 requestId。

风险：若 runtime request 未来改成异步 dispatch 或消息发送不是同步发生，patch 可能抓不到 id，导致 Abort 无法发送正确 cancel。

正确方案：直接在 `runtime.js` 的 request 内部生成并暴露 cancellation handle，不应 monkey-patch `postMessage`。

### 6. 标准 Request timeout 不自动取消服务器侧 handler

`runtime.js` 的普通 `request()` 超时只 reject JS Promise，并不自动向 Framework 发送 cancel。

因此：

```text
JS timeout
≠
C# handler cancellation
```

需要在协议文档中明确，或最终统一成 timeout -> cancel。

### 7. `EnsureUiThread()` 对 disposed host 静默 return

当前 host disposed 时直接 return，调用方拿不到失败状态；`SendResponseAsync()` 创建的 completion task 可能无法完成。

建议：统一改为 `TryEnsureUiThread` + 异步结果失败，至少给 pending response 一个确定终止态。

### 8. HotReload 生命周期

`FileSystemWatcher` 在 CloseCurrent/Hide 后仍可能存在。

风险：

- 页面关闭后修改文件仍触发隐藏 WebView reload。
- Changed 事件可能连续触发多个 Reload。
- 新页面打开时才更换 watcher，关闭阶段没有明确 gate。

建议：CloseCurrent 时停止 watcher；重新打开时再创建；对 Changed 做 50~100ms debounce。

### 9. WebView2 diagnostics hook 生命周期

静态 diagnostics patch 需要绑定当前 `CoreWebView2` 实例，而不是只用一次性 installed 标志。

当前版本已经处理“新 Core 实例替换”的挂钩；仍需最终验证 framework reload/shutdown/reinitialize 后不会残留旧事件。

### 10. 全局 IMessageFilter 生命周期

ESC filter 是进程级对象。Framework shutdown 后必须 RemoveMessageFilter，否则可能残留旧 Host 引用并影响后续重建。

下一步应把 `HtmlUiKeyboardAndDiagnosticsPatch.Uninstall()` 接到 Framework Dispose。

## P1/P2 Runtime / Binding

### 11. `binder.component()` 使用对象展开

`HtmlUiBindingSchedulerPatch` 中对 component 返回值使用 `{ ...component, dispose }`。

如果原 component 是带 prototype 方法的 class/object，这会丢失 prototype 行为，只留下 own enumerable properties。

建议：使用代理对象或直接装饰原对象的 dispose，而不是 spread clone。

### 12. Scheduler 同一 element 的 debounce/throttle namespace

Scheduler 以 element 为粒度清理，因此同一 DOM element 上多个独立 binding 可能互相清掉定时器。

应按 `element + binding identity` 建立 timer state。

### 13. i18n bind 重绑定成本

当前生命周期 patch 会维护 WeakMap/MutationObserver/translation cache，设计正确，但在大型 DOM 下属性变更会触发重复扫描；需要压力测试确认成本。

### 14. State Remove 的 JS 语义不完整

C# `HtmlUiStateStore.Remove()` 当前发送 `state:<key>` null。

runtime `__receive()` 当前使用 `state.set(key, msg.payload)`，因此：

```text
C# Remove
→ JS state.set(key, null)
→ state.has(key) 仍为 true
```

需要协议级 remove 事件或显式删除语义。

## 工程配置

### 15. Consumer 多目标框架

`HtmlUiConsumerTestMod.csproj`：`net472;net6`

Framework：`net472`

Bannerlord 实际运行模块应优先保持单一 `net472` 运行目标。若 net6 只是工具用途，应拆为独立 project，避免运行引用与输出目录混淆。

### 16. 历史兼容文件

`HtmlUiHostCancellableExtensions.cs` 当前与 Host/Service cancellation overload 重复，建议清理以减少 API 来源歧义。

## 日志与诊断

### 当前正确行为

Framework / Consumer 启动时清空本次日志，运行期间追加。

Debug 默认关闭，避免恢复逐帧 Window tracking 噪声。

### 推荐保留

- WebView2 ProcessFailed
- Navigation start/completed
- Page open/close
- ESC close path
- i18n DOM audit（仅诊断版）
- Browser runtime error
- ActiveRequestCount

## 发布前必须完成

1. Framework + Consumer 最新 dev 重编。
2. F11 -> ESC 实机验证。
3. 确认 `MainWindowHandle=0` 不再隐藏活动 Overlay。
4. 确认 i18n DOM audit 能写入日志。
5. Run 10 StressLab。
6. State Remove 语义修复。
7. PageManager transition race 修复。
8. Owner request cancellation 修复。
9. HotReload lifecycle + debounce。
10. IMessageFilter Uninstall。
11. net472/net6 工程目标重新整理。
12. 最终清理历史 extension / debug-only artifacts。
