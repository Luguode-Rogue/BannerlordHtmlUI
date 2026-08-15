# BannerlordHtmlUI 项目交接文档

> 目标：把当前 `dev` 的真实项目状态完整交给下一次对话/开发者。以后继续工作前应先读本文件，再看 `Handoff/FULL_CODE_AUDIT_20260815.md` 与 `Handoff/V0.44_RELEASE_CHECKLIST.md`。
>
> 更新时间：2026-08-15
> 当前开发分支：`dev`
> 发布基线：`main`
> 另一个长期分支：`trunk`

## 1. 项目目标

BannerlordHtmlUI 是给 Mount & Blade II: Bannerlord 使用的 HTML/WebView2 UI Framework。

核心目标：

- 用 HTML/CSS/JavaScript 构建游戏内 UI。
- WebView2 与 Bannerlord/C# Bridge 之间提供稳定、可作用域隔离的通信。
- 提供 Page、State、Event、Command、Request、Binding、i18n、Diagnostics 等基础能力。
- Consumer Mod 不直接依赖 Framework 内部实现，而是通过 `HtmlUiConsumerScope` 等公共 API 使用。
- Framework 需要处理 WebView2 UI thread、Bannerlord GameThread、生命周期、关闭、导航竞态和运行时异常。

## 2. 当前架构

### Framework

主要区域：

- `HtmlUiService`：Framework 对外服务入口、Ready/Shutdown、公共 API 聚合。
- `HtmlUiHost`：WebView2 Host、Overlay、UI thread、导航、窗口状态、消息转发。
- `HtmlUiOverlayForm`：承载 WebView2 的 WinForms overlay。
- `HtmlUiPageManager`：Page 注册、Open/Close/Navigation 生命周期。
- `HtmlUiStateStore`：C# 侧 State 存储与同步。
- `HtmlUiBridge`：Command/Request/Event/Response 及 cancellation。
- `HtmlUiConsumerScope`：Consumer owner/scoped API。
- `HtmlUiLogger`：Framework 日志。
- `HtmlUiDiagnostics`：运行时诊断快照。
- `GameThreadDispatcher` / UI-thread dispatch：线程边界处理。

### Runtime

主要负责：

- `state` / state bindings
- `event`
- `command`
- `request`
- `requestCancellable`
- i18n/binding
- component 生命周期
- hot reload / navigation race
- runtime error reporting

### Consumer TestMod

项目：`HtmlUiConsumerTestMod`

当前用途不是最终游戏 UI，而是 Framework 的验收、回归与压力测试。

页面：

- `Test/index.html`：基础 API / 生命周期测试。
- `StressLab/index.html`：压力测试。

测试快捷键：

- `F11`：打开普通 Test 页面。
- `F8`：打开 StressLab。
- `F7`：关闭 Consumer 当前页。
- **F12 不作为方案，也不作为验收条件。** 之前已经确认该环境下 F12 不可靠，因此后续不要继续把 F12 当关闭路径。
- 当前主要关闭目标是 **ESC** 以及页面自身 Close 按钮。

## 3. 已完成的重要工作

### WebView2 / 初始化

- WebView2 UI thread 已单独创建。
- `EnsureCoreWebView2Async()` 完成后再安装需要访问 `CoreWebView2` 的 Patch。
- 之前所有 Patch 在错误线程访问 `CoreWebView2` 导致的 `E_NOINTERFACE` / `CoreWebView2 can only be accessed from the UI thread` 已定位并修复。
- Framework 日志已经能确认 Patch 安装成功。

### Page ID / 注册

曾出现过严重 bug：`HtmlUiPage.Id` 只读属性在构造函数中未赋值，导致：

`Dictionary.ContainsKey(null)` → Framework page 注册失败 → Consumer page scoped name 失败 → diagnostics/test page 全不可用。

已修：构造函数必须明确 `Id = id`。

### C# 10 字符串语法

正式环境曾出现一批 `CS1519/CS8803/CS1003/...` 级联错误。

根因：多个 JS 注入 Patch 使用 C# `@"..."` verbatim string，却错误使用 `\"` 转义双引号。

已按 C# 10 合法方式重写，避免将 JS 内容错误解析成 C#。

### Bridge / Cancellation

已完成：

- 普通 Request。
- CancellationToken Request overload。
- Consumer owner-aware request registration。
- Runtime `requestCancellable`。
- Shutdown 时取消活动 Request。
- `ActiveRequestCount` Diagnostics。

当前重要语义：

- `BridgeRequestCount` = 注册的 Request 数量，不等于 pending request。
- `ActiveRequestCount` = 当前仍有 CancellationTokenSource 的活动 Request 数量。
- `CancelAllRequests()` 只触发 Cancel，实际从集合移除依赖 handler `finally`，因此“最终归零”才是正确验收标准。

### ConsumerScope

已完成 owner-scoped API，包括 Page/State/Event/Command/Request/Component 等资源归属概念。

### Binding / Component

发现并修复过一个结构性风险：

原 `component()` 使用对象 spread 返回新对象，可能丢失 prototype / Symbol / non-enumerable methods。

当前应保持原 Component 对象，并只替换/包装 `dispose()`。

### StressLab

StressLab 已建立，当前每轮覆盖：

- DOM node stress
- 独立 Component host
- 普通 Request
- Cancellable Request
- Diagnostics 前后比较
- `activeRequests`
- `pages`
- `states`
- `commands`
- `requests`
- `domChildren`

StressLab 已修复过自身假阳性：每个 Component 现在使用独立 host，且一次只能运行一个 stress cycle。

### 日志

Framework：

`BannerlordHtmlUI.log`

Consumer：

`HtmlUiConsumerTestMod.log`

当前设计：

- 每次模块启动清空旧日志。
- 本次运行期间继续 append。
- Debug 维持默认关闭，避免逐帧/高频窗口 tracking 噪声。

### Consumer 启动配置

`HtmlUiConsumerTestMod/Properties/launchSettings.json` 已恢复：

`BannerlordHtmlUI` 必须出现在 `_MODULES_` 中，并且位于 `$(ModuleId)` 之前。

标准模块序列包含：

`Bannerlord.Harmony*Bannerlord.ButterLib*Bannerlord.UIExtenderEx*Bannerlord.MBOptionScreen*Native*SandBoxCore*CustomBattle*Sandbox*StoryMode*NavalDLC*BannerlordHtmlUI*$(ModuleId)`

以后不要再手工修改；先检查这个文件。

## 4. 最近一次正式环境真实问题及结论

### 问题 A：Framework 程序集找不到

曾出现：

`Could not load file or assembly 'BannerlordHtmlUI, Version=0.44.0.0'`

最终确认 Consumer 的项目启动配置/模块列表缺少 `BannerlordHtmlUI`。

已经恢复。

### 问题 B：WebView2 Patch 全部失败

日志：

`CoreWebView2 can only be accessed from the UI thread`

5 个 Patch 全部在错误线程安装。

已改为 WebView2 UI thread after-ready 路径。

### 问题 C：Framework page 注册失败

日志：

`Value cannot be null. Parameter name: key`

直接根因是 `HtmlUiPage.Id` 没在构造函数中赋值。

已修。

### 问题 D：F11 打开后网页关闭失败 / 页面过一段时间消失

重要日志模式：

`inputMode=Captured`

随后：

`Bannerlord main window could not be resolved. hwnd=0`

然后 Overlay 被隐藏/最终退出。

代码审查已经确认：**MainWindowHandle 临时变成 0 不应该被直接等价成“游戏窗口关闭”。**

这是当前 P0 级核心问题之一，必须继续修复/验证。

### 问题 E：F12

已经反复验证：**F12 不可作为可靠方案。**

以后不要继续围绕 F12 设计验收。

### 问题 F：ESC

目标关闭路径是 ESC / 页面 Close 按钮。

目前框架侧已经在开发 ESC UI-thread filter，但过去一轮运行使用的 DLL 曾未包含该接线，因此日志中看不到相应记录。

交接后必须先确认：

- 当前 DLL 是否来自最新 `dev`。
- 日志是否出现 `Global UI ESC close filter installed.`。
- ESC 是否产生 `UI keyboard close detected: key=Escape`。
- 关闭后 `currentPage=<null>`、`inputMode=Hidden`、`hostVisible=False`。

### 问题 G：翻译不全

已确认两层原因：

1. Test 页面部分用户可见文字此前是硬编码，没有走 i18n。
2. i18n 诊断曾放在 Consumer → JS → Bridge 链路上，实际运行日志里没有执行，诊断盲区很大。

已把一部分固定文本改成 i18n key，并补中英文资源。

后续必须使用 Framework 主动 DOM audit 或明确的 i18n 请求审计，确认：

`key -> locale -> 最终文本`

而不是仅凭 XML 是否存在判断翻译正常。

## 5. 当前正在进行：全代码审查

当前阶段已经从“逐个修 Bug”切换成：

**FULL CODE AUDIT / Stability Hardening**

审查范围：

- WebView2 初始化与 UI thread
- Overlay/window tracking
- PageManager transition race
- Page lifecycle / HotReload
- StateStore / State remove semantics
- Bridge Request/Command/Event/Response
- CancellationToken lifecycle
- ConsumerScope disposal / owner cleanup
- Binding / debounce / throttle / component
- i18n
- Diagnostics
- runtime.js
- Consumer TestMod
- build / launch config
- logging
- release configuration

审计文档：

`Handoff/FULL_CODE_AUDIT_20260815.md`

## 6. 当前已知的代码级风险

### P0 / 必须先处理

1. **MainWindowHandle = 0 的错误隐藏**
   - 临时 HWND=0 时不应直接隐藏正在显示的 UI。
   - 必须区分“暂时无法解析游戏 HWND”和“真正 Framework shutdown / game process exit”。

2. **ESC 关闭链必须在 Framework UI thread 真正生效**
   - 不依赖 Bannerlord Input。
   - 不依赖网页 JS focus。
   - 必须有日志证明 filter install / key detected / close completed。

3. **WebView2 ProcessFailed 后恢复策略**
   - 当前已有 `RenderProcessUnresponsive` 实例。
   - 需要明确：记录 → 重建 WebView2 / Overlay → 重新绑定 Patch → 恢复当前 page 或安全关闭。
   - 不允许简单让 Framework silent death。

### P1 / 下一批

1. PageManager Open/Close transition race。
2. `State.Remove` 的 Runtime 语义当前可能是 `state.set(key, null)` 而不是 delete。
3. ConsumerScope Dispose 时是否完整取消 owned active Request。
4. `preCanceledRequests` 长期运行是否会积累。
5. `requestCancellable` 通过临时替换 `webview.postMessage` 捕获 requestId 的竞态风险。
6. HotReload 页面关闭/重新打开时资源是否完整回收。
7. WebView2 `CoreWebView2` 事件重建/解绑是否完整。
8. `EnsureUiThread` 在 disposed 状态下 Task 是否可能永不完成。
9. Global keyboard filter / message filter 生命周期必须配对 install/uninstall。

### P2 / 工程收口

1. Consumer TestMod 当前多 target (`net472` + `net6`)，而 Framework 正式运行目标为 `net472`；需要确认 net6 是否仍有真实用途，否则简化。
2. `HtmlUiHostCancellableExtensions` 是否可以合并回正式 API，避免兼容层继续增生。
3. Diagnostics / testing hooks 是否应该继续保留在 Framework SubModule，还是全部下沉到 TestMod。
4. Release build 的 debug/test surface 最终是否要完全关闭。
5. 分支最终清理到 `main / trunk / dev`。

## 7. 接下来严格按这个顺序做

### 第一优先级

- 修 MainWindowHandle=0 隐藏竞态。
- 完成 ESC UI-thread close 的真实接线和生命周期。
- 明确 WebView2 ProcessFailed recovery 策略。

### 第二优先级

- 修 State Remove 为真正 delete 语义。
- 修 ConsumerScope owner request cancellation。
- 修 preCanceledRequests 生命周期。
- 审核 cancellable request requestId 捕获方式。

### 第三优先级

- HotReload / Page lifecycle。
- Binding / i18n / component 全面审计。
- Thread boundary 全面审计。

### 第四优先级

- Consumer net6 target 是否删除。
- Cleanup test-only APIs。
- Release configuration。
- Branch cleanup。

### 第五优先级：实机回归

代码收口后再让用户做：

1. F11 → 等待页面完全显示 → ESC。
2. F11 → 页面切换/关闭/再次打开。
3. F8 → StressLab → Run 10。
4. StressLab 长时间 Run 50。
5. Page Reload / Binding / Component 高负荷。
6. Framework shutdown / game exit。

**不是必须的测试不要打断用户当前工作。**

## 8. 当前日志位置

Framework：

`E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\BannerlordHtmlUI\BannerlordHtmlUI.log`

Consumer：

`E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\HtmlUiConsumerTestMod\bin\Win64_Shipping_Client\HtmlUiConsumerTestMod.log`

每次进入游戏应该自动清空旧日志。

## 9. 运行时日志最低验收标准

正常启动：

- WebView2 UI thread starting
- EnsureCoreWebView2Async completed
- 所有 Runtime Patch installed
- Navigation race guard installed
- Framework / diagnostics pages registered
- Consumer pages registered

F11 打开后：

- Page open requested
- Navigate requested
- Page open state committed
- navigation completed
- inputMode=Captured

ESC 关闭应看到：

- ESC filter installed
- Escape detected
- CloseCurrent requested/executed
- currentPage=<null>
- inputMode=Hidden
- hostVisible=False

如果页面异常：

- JS runtime error / unhandledrejection 必须能进入 Framework log。
- WebView2 ProcessFailed 必须有明确记录。
- 不允许只看到 `Window state changed` 而没有真正的生命周期原因。

## 10. 重要工程约束

- 所有开发改动继续写 `dev`，不要直接写 `main`。
- `main` 是发布基线，不用于实验。
- `trunk` 保留。
- 不要把 F12 当可靠关闭方案。
- 用户明确表示：非必须测试暂时不要打断；需要用户做时再喊。
- 不要重新引入逐帧 / 高频 Window tracking DEBUG 日志。
- 修改 Framework 后，先 Rebuild Framework，再 Rebuild Consumer。
- Consumer 正式运行依赖 `BannerlordHtmlUI` 模块必须位于 `$(ModuleId)` 之前。
- 不要为了通过编译提高 LangVersion；Framework 当前兼容目标为 C# 10 / net472。

## 11. 当前一句话状态

**主体 Framework 功能已经基本成型，但目前处于“全代码审查 + Stability Hardening”，重点不是继续加功能，而是清掉窗口生命周期、ESC、State、Request cancellation、WebView2 failure recovery 和页面竞态这些结构性风险，然后再进入 v0.44 发布回归。**
