# BannerlordHtmlUI 项目交接文档

日期：2026-08-16
分支：`dev`
仓库：`Luguode-Rogue/BannerlordHtmlUI`

## 1. 当前项目目标

BannerlordHtmlUI 是 Bannerlord 的 WebView2 HTML UI Framework。目标是让其他 Mod 通过 Framework 注册 ContentRoot / Page / Command / Request / State，并在游戏内打开网页 UI，同时提供 Runtime、Bridge、Binding、i18n、输入焦点、页面生命周期、HotReload 与 Consumer TestMod 能力。

当前阶段不是继续盲目扩展功能，而是完成 Stability Hardening，然后恢复完整功能开发并准备 v0.44 收口。

## 2. 分支约束

- `main`：稳定归档/基准版本，不作为当前开发目标。
- `trunk`：已有的最新版备份/参考分支。此前用于核对多页面等功能完整性。
- `dev`：当前开发主线。
- 历史上曾做过多轮二分定位。已经确认过可用/不可用版本，不应再无目的重复二分。
- 当前结论：继续开发应直接基于 `dev` 当前已恢复的完整功能基线处理结构性问题。

## 3. 历史重大 Bug：必须保留在知识链中

此前网页 UI 出现“打不开、显示异常、无法关闭、焦点异常、部分翻译消失/按钮无法点击”等问题。经过多轮版本二分定位，确认问题来自某一轮开发修改，而不是 Consumer 测试本身。

实际测试中曾出现：

- F11 `Pages.Open()` 返回 True，但 UI 显示异常。
- 部分 i18n 文本为空。
- 部分按钮无法点击。
- 页面关闭状态与实际窗口状态可能不同。
- Bannerlord 主窗口 HWND 临时变为 0 时，旧逻辑可能错误 `Hide()` WebView2 Overlay。
- 多页面功能曾因错误修改/覆盖而丢失，因此后续修复不得删除已有 Page / Consumer / StressLab 功能。
- `84c9ecfe56fbc06b1b81dca261ce8ad5576c6563` 曾在只修改右键菜单的同时误删 `HtmlUiHost.cs` 后续 516 行，导致 `FlushPendingPage`、导航事件处理、Runtime 安装、窗口状态、`Dispose()` 等方法全部消失并产生一串级联编译错误。该提交不是功能设计变更，应视为历史文件损坏点。

此前已经通过二分确认过若干可用版本；后续应把“已确认可用的多页面完整版本”视为功能基线，而不是用简化版本继续开发。

## 4. 已完成的重要稳定性修复

### HtmlUiHost 历史损坏恢复

**已完成代码恢复。**

恢复依据：`84c9ecfe56fbc06b1b81dca261ce8ad5576c6563` 的父版本 `e15685a9dcf1e46d71372aa44a3407f5fe458b04` 中的完整 `HtmlUiHost.cs`。

恢复内容包括：

- `ApplyWindowState()`
- `ConfigureLocalHost()` / ContentRoot 管理
- `InstallFrameworkRuntime()` / `InstallRuntimeErrorForwarder()`
- `OnWebResourceRequested()`
- `OnNavigationStarting()` / `OnNavigationCompleted()`
- `ValidatePage()` / `Navigate()` / `FlushPendingPage()` / `NavigateOnUiThread()`
- HotReload watcher
- InputMode / UI thread 调度
- Command / Request / Event / Response API
- `Dispose()`

恢复提交：

- `c7bbe9fffa1757935b433991ca43f58c2375ab51` — `fix: restore truncated HtmlUiHost implementation`

随后为避免再次直接改动这个大文件，右键菜单功能改由独立生命周期 Patch 保证：

- `62d82f3c37bbb72f5d9609682a5fd5c1e7cfc16a` — 新增 `HtmlUiContextMenuPatch.cs`
- `01e393228cd391c36f5c2cd02df09a767e695a26` — 接入安装/卸载生命周期

因此当前 `HtmlUiHost.cs` 保持历史完整实现，而 `AreDefaultContextMenusEnabled=false` 由独立 Patch 在 `ConfigureAfterWebViewReady()` 后强制设置。

### Page transition race

提交：`8b360c1cee65b0080dc08e2fb42a6507d03f831e`

`Open / Close / CloseCurrent / Unregister` 使用独立 transition lock 串行化，避免：

`Open(A) -> Open(B) -> CloseCurrent -> Navigate(A/B)` 交错导致 `_openId` 与实际 WebView 页面不同步。

导航失败时会回滚打开状态，避免逻辑状态残留。

### ESC 生命周期

提交：

- `a5eefaac06532bbdf0d2c7f7dc04817529e05eca`
- `7e4f37b41136cce85a6fa56831591c86e3582e52`

Framework shutdown 时主动：

- RemoveMessageFilter
- 取消 CoreWebView2 NavigationCompleted
- 清空 EscapePressed
- 清空 Host 引用

### Binding Component 包装

提交：`58681138be16fcfa29f9b416847028bacf281bd4`

不再用 `{ ...component, dispose }` 破坏原 Component 对象，尽量保留 prototype / Symbol / non-enumerable 属性。

### HWND=0 稳定性

**已完成代码收口，待实机回归。**

原问题：Bannerlord 主窗口句柄临时解析失败时，`FollowBannerlordWindow()` 会直接 `Hide()` Overlay；这会错误改变用户请求的可见性。

当前实现增加 `HtmlUiWindowTrackingPatch`：

- requested visibility 已为 false 时保持原关闭路径。
- requested visibility 为 true 且 Bannerlord HWND 暂时无效时，跳过本轮窗口跟随。
- 不调用 `Hide()`。
- 不修改 `_requestedVisible`。
- 下一次 HWND 恢复后自动回到正常同步路径。
- 诊断日志保持低频，不恢复逐帧 tracking 日志。

相关提交：

- `92ed46afebbbe8f35f2c218f077e05623ee1ea73` — `fix: install HWND=0 overlay visibility guard`
- `3c3d6cc6e68487093bee44ef692e4af316a26864` — guard implementation update

### HotReload / Navigation guard

已经加入 HotReload lifecycle/debounce 与 Navigation race guard。相关 patch 的生命周期需要继续审查。

### WebView2 右键菜单

用户明确要求网页 F11 UI 不出现浏览器右键菜单。

当前通过 `HtmlUiContextMenuPatch` 在 `ConfigureAfterWebViewReady()` 完成后设置：

`CoreWebView2.Settings.AreDefaultContextMenusEnabled = false`

不要再次直接修改完整 `HtmlUiHost.cs` 来实现这一功能，以免重复引入历史级联破坏。

### WebView2 ProcessFailed Recovery

**已完成第一版恢复状态机代码，待真实 ProcessFailed 实机回归。**

新增：`src/HtmlUiProcessRecovery.cs`

提交：

- `f0977634ac08c97d9ddfe1b10a390ab669029d36` — `feat: add WebView2 process recovery`
- `90aca808dbdd7037ae9527b85c796997c7426a34` — `feat: enable WebView2 process recovery`

当前恢复流程：

`ProcessFailed -> Recovery lock -> 保留当前 Page -> _webViewReady=false -> 移除旧 WebView2 -> 创建 replacement WebView2 -> 重用环境 -> 失败时重建 Environment -> EnsureCoreWebView2Async -> 调用既有 ConfigureAfterWebViewReady -> Runtime/Bridge/Patch 重新绑定 -> 恢复 pending Page -> 恢复 InputMode -> Running`

额外约束：

- Recovery 期间重复 `ProcessFailed` 只处理一次。
- 恢复时暂时抑制 `Ready` 回调，避免 Consumer 因浏览器重建被误判为 Framework 首次 Ready。
- 恢复失败不伪造 `_webViewReady=true`。
- 恢复失败进入 `Pages.CloseCurrent()` 安全关闭路径。
- 当前实现保持原有 `WinForms STA + Form.Load + BeginInvoke + UI-thread WebView2` 架构，不替换冻结的 Runtime Baseline。

注意：目前没有真实 Bannerlord + WebView2 ProcessFailed 回归证据，因此不能把 Recovery 标记为“验证完成”。

## 5. State Remove

历史审查发现：C# `HtmlUiStateStore.Remove()` 删除了 C# dictionary，但 Runtime 只把 `state:<key>` 当成 `state.set()`，导致 JS `state.has(key)` 仍可能为 true。

本轮已增加 State Remove compatibility patch，并接入 Framework 启动流程。

相关提交：

- `f1ad49b13950aabb7fa97168a51e829c877fc8a0`
- `5e2ddbe4b82e1eea4ce11e353161a332914f1c8c`
- `972091c7e5e13196888cb18017d13d017611c83c`

目标语义：

- `game.state.has(key)` -> false
- `game.state.get(key)` -> undefined
- snapshot 不再包含删除的 key
- 删除通知仍能让 Binding / listener 正确更新
- 后续重新 set 可以恢复

## 6. Request 生命周期硬化

### Owner Dispose -> cancellable requests

**代码已完成，待实机回归。**

`HtmlUiConsumerScope.Dispose()` 当前顺序为：

1. 关闭 Owner 当前页面。
2. `HtmlUiBridge.CancelRequestsByOwner(OwnerId)` 主动取消正在运行的 cancellable request。
3. Unregister Owner Pages / Commands / Requests。
4. Request unregister 内部再次执行 targeted cancellation，覆盖取消扫描与 unregister 之间的竞态窗口。
5. 清理 Owner State / ContentRoot。

因此 Owner Dispose 不再只是删除注册表，而是主动进入 request cancellation 生命周期。

验收：

- Consumer 创建 cancellable request 并阻塞。
- Dispose ConsumerScope。
- handler 收到 `CancellationToken.IsCancellationRequested` / `OperationCanceledException`。
- 浏览器端 Promise 不应等待到超时。
- `HtmlUiBridge.ActiveRequestCount` 最终回到 0。
- 再次注册同名 Request 后不受旧 request 状态影响。

### preCanceledRequests 生命周期

**代码已有 bounded cleanup，待长时间回归。**

当前实现：

- TTL：30 秒。
- 上限：2048 项。
- Cancel 新请求前执行 TTL cleanup。
- 达到上限时执行 force trim。
- 请求最终完成时主动移除对应 pre-cancel 标记。

因此目前不存在无限增长路径；剩余工作是 StressLab 验证高频 cancel / request churn 后集合不会异常膨胀。

### requestCancellable requestId 竞态

**旧方案已移除。当前实现采用独立请求 ID，不再临时替换 `chrome.webview.postMessage`。**

`HtmlUiRequestCancellationPatch` 为 cancellable request 独立生成 `c<timestamp>_<counter>` ID，并先登记本地 pending，再直接发送标准 `request` 消息；响应通过独立的 `__receive` 包装分流。

当前剩余工作是实机验证：

- 高并发 requestCancellable 不串 ID。
- Abort / timeout / response 三者不会重复 settle。
- 普通 `game.request()` 不受影响。
- Page unload 后不会残留 cancellable pending。

## 7. 当前已知剩余结构性问题

### P0/P1 优先

1. **Framework / Consumer 编译回归**
   - `HtmlUiHost.cs` 历史截断已经恢复。
   - GitHub 当前没有可用 Actions workflow run，因此未能提供远程编译通过证据。
   - 下一步必须在本机先编译 Framework，再编译 Consumer TestMod。

2. **HWND=0 实机回归**
   - 代码收口完成。
   - 待 Bannerlord 实机验证：短暂 MainWindowHandle 不可解析时 UI 不应被隐藏；恢复后应自动继续跟随。

3. **WebView2 ProcessFailed Recovery 实机回归**
   - 恢复代码已接入。
   - 待真实 ProcessFailed 触发验证：
     - 当前 Page 保留
     - Runtime / Bridge / i18n / Binding 恢复
     - InputMode 恢复
     - 多 Page 不丢失
     - 恢复失败才进入安全关闭

4. **Request lifecycle StressLab 回归**
   - Owner Dispose / pre-cancel / requestCancellable 代码已经收口。
   - 需要长时间、高并发场景实测。

5. **HotReload / Page lifecycle**
   - 需要继续审查跨导航、reload、unregister、consumer dispose 的生命周期一致性。

### P2

- Consumer TestMod `net472 + net6` 与 Framework `net472` 架构不完全对称；暂时不要擅自删除 net6。
- `HtmlUiHostCancellableExtensions` 兼容层可以继续收敛。
- Framework SubModule 中仍有部分 diagnostics/testing hook，可在稳定性收口后整理。
- WebView2 ProcessFailed 后的恢复需要确保 Runtime、Bridge、Patch、Page 状态全部一致。

## 8. 当前测试基线

Consumer TestMod：

- `HtmlUiConsumerTestMod.consumer.test`
- `HtmlUiConsumerTestMod.consumer.stress`
- F11：打开 Test 页面
- F12：此前被确认不应作为可靠关闭方案；不要重新把 F12 当作核心生命周期机制。
- ESC：全局关闭链路需要继续保证。

典型正常日志链：

`HtmlUiService.OnReady`
→ ContentRoot registered
→ Page registered
→ F11
→ `Pages.Open result=True`
→ `Navigate requested`
→ WebView2 navigation completed
→ UI visible / input captured
→ ESC / CloseCurrent
→ current page null / input Hidden / host invisible

注意：日志中 `i18n DOM audit` 的 item text 曾全部为空，这是过去 UI 异常期间的重要信号；后续 i18n 回归测试不能只看 NavigationCompleted。

## 9. 最近一次异常的关键日志结论

最近一次日志显示：

- Framework 初始化成功。
- WebView2 environment 创建成功。
- `EnsureCoreWebView2Async completed`。
- 所有主要 patch 安装成功。
- Page 注册成功。
- F11 `Pages.Open result=True`。
- WebView2 navigation completed successfully。
- 关闭流程也能把 currentPage 置空、inputMode 置 Hidden。

因此如果未来再次出现“打不开/不能点击/显示异常”，不能只看 `Pages.Open` 返回值；需要继续检查：

- DOM 实际内容
- i18n 注入/Binding
- JS Runtime 是否初始化
- Bridge message 是否到达
- WebView2 focus / foreground
- Page navigation 后 runtime state 是否完整

## 10. 当前开发顺序

严格按以下顺序：

1. ~~收口 HWND=0 残留 Hide 路径~~ **代码完成，待实机回归**。
2. ~~实现 WebView2 ProcessFailed Recovery~~ **代码完成，待真实 ProcessFailed 回归**。
3. ~~修复 `HtmlUiHost.cs` 历史截断导致的级联编译错误~~ **代码完成，待本机编译**。
4. **Framework 编译 → Consumer TestMod 编译 → 完整回归单页面 + 多页面 + ESC + 焦点 + i18n + HWND=0 + ProcessFailed Recovery。**
5. ~~Owner Dispose -> Request cancellation~~ **代码完成，待实机/StressLab 回归**。
6. ~~收口 preCanceledRequests 生命周期~~ **代码完成，待 StressLab 回归**。
7. ~~修正 requestCancellable requestId 捕获竞态~~ **旧方案已移除，当前实现待实机回归**。
8. **HotReload / Page lifecycle 全面回归。**
9. **StressLab 长时间测试。**
10. **Binding / i18n / Runtime 协议最终审查。**
11. **恢复全功能开发。**
12. **v0.44 release checklist / 文档 / 发布收口。**

## 11. 开发原则

- 不要因为测试失败就删除功能或回退到简化版本。
- 不要用“二分”代替代码定位；二分已经完成了它的定位任务。
- 不要覆盖已经验证的多页面功能。
- 不要修改 `main` 作为当前开发方案；`main` 是稳定归档基线。
- 不要把 F12 作为可靠关闭机制。
- 不要恢复逐帧 Window tracking 日志；默认保持低频日志，只有专项排查时才临时打开。
- 每次修复应有明确的代码原因、最小修改范围和提交说明。
- Framework 修改后按约定先重新编译 Framework，再编译 Consumer TestMod。
- 不要为了日志好看而制造假状态；所有 Ready/Visible/Input 状态必须与实际 WebView2 状态一致。
- `HtmlUiHost.cs` 属于回归敏感核心文件；涉及单行功能修改时优先使用独立 Patch，避免再次发生历史级联破坏。

## 12. 下一步交接点

**当前真正的下一步是本机编译与实机稳定性回归，而不是继续无谓重构：**

- Framework 编译
- Consumer TestMod 编译
- F11 打开/ESC 关闭
- 连续打开/关闭
- 多 Page 切换
- i18n 文本与按钮点击
- 失焦/恢复焦点
- Bannerlord 主窗口短暂不可解析
- WebView2 ProcessFailed Recovery
- Recovery 后再次打开/关闭
- Owner Dispose + cancellable request
- requestCancellable 高并发 / Abort / timeout
- StressLab 长时间运行
