# PROJECT STATUS

## 已实机验证
- WebView2 初始化
- WebView2 UI Thread
- F10 Diagnostics
- Consumer OnReady
- ContentRoot 注册
- Consumer Page 注册
- F11 打开
- HTML Navigation
- 页面关闭
- F11 再打开
- Captured Input
- Overlay 防闪烁
- Close UI 一次点击关闭
- Close 后 Hidden
- Consumer Shutdown 防御式清理
- Framework 主日志取消逐帧 Window Tracking
- Overlay/WebView2 渲染不可见问题的当前正常基线：`debug/test-root-transparent`

## 当前工作
### M2 Bridge
Bridge 已具备 Command / Request / Response / Event / State 的基础实现。

已完成的静态边界修正：
- State 删除通过 `state:<key>` 同通道发送 `null`，订阅与 binding 可以感知删除。
- 删除不存在的 State Key 不广播事件。
- State 设置相同值时不再重复广播，降低高频状态事件噪声。
- State 对 JSON-like 数组、字典、匿名对象改用内容比较；内容相同但引用不同的值不再误触发 state 事件。
- 高频 scalar State（字符串、数字、bool、日期、GUID 等）使用轻量比较路径，避免不必要的 JSON 转换。
- ConsumerScope 页面 ContentRoot 解析对空值安全，默认使用 consumer `ui` root。
- Bridge 重复注册保护：重复 Command / Request / Page 不再静默覆盖。
- `runtime.error` 等无 id 的 fire-and-forget 消息可以正常诊断，而普通无 id Command 会被拒绝。
- Bridge 协议异常与未知消息类型进入明确的错误路径，不再静默丢弃。
- Handler 返回结果时的 Response 发送失败已隔离；WebView 已关闭等情况下不会把二次发送异常继续逸出到回调线程。
- `HtmlUiPageManager.Count` / `Reload()` 已与文档 API 对齐。
- `HtmlUiStateStore.Count` 已开放给诊断层。

当前重点是完整实机绿灯验收与边界错误传播验证。

### M3 Localization
Bannerlord 原生 Localization -> Framework -> `game.app.i18n` -> HTML。

已完成的静态加固：
- Localization 变量替换支持 primitive、日期、对象与数组，不再对复杂 JSON 输入做危险的 `JValue` 强转。
- `TranslateMany` 对每个 key 的变量对象显式解析。
- JavaScript Runtime 的 i18n API 已有对应的 TypeScript `.d.ts` 声明，包含 `i18n.t/getLocale/getLanguages/bind/formatDate/formatTime/onLocaleChanged`。
- 缺失 Localization key 的 WARN 按“语言 + key”去重，避免页面重复渲染刷屏。

当前继续处理：
- `i18n.bind()` 生命周期与 disposer
- Language Switch 后 DOM 自动刷新
- 异步翻译结果在页面销毁后的防回写

### API 边界
- `HtmlUiPage.RelativePath` 已明确拒绝 rooted/absolute path，并继续拒绝 `..` 越界路径，保持 Page 资源只能落在声明的 ContentRoot 内。

### Diagnostics
- Framework version 已与 v0.44 文档对齐为 `0.44.0`。
- F10 Diagnostics 当前可报告 PageCount / StateCount，避免诊断页面只显示部分运行态。

### Overlay / WebView2
- 当前已有经过实机验证的正常基线：`debug/test-root-transparent`。
- 该基线不是冻结状态；后续允许继续优化 Overlay、窗口层级、透明度、输入与 WebView2 渲染行为。
- 已知“画面不可见但点击区域存在”的复现与修复已经记录，不需要作为后续每轮普通开发的阻塞条件。
- 只有再次修改 Overlay/WebView2 渲染、窗口样式、D3D/Chromium 子窗口层级等相关代码时，才重新执行对应的已知回归测试。

## 待验收
- Command
- Request / Response
- Async Request / timeout
- Event
- State
- State remove / redundant-set 行为
- Two-way binding
- Template / List binding 生命周期
- Input Capture / Release 完整验收
- Localization
- Language Switch
- i18n DOM bind 生命周期与语言切换自动刷新

## 已解决的历史问题
### WebView2 跨线程
曾出现 `CoreWebView2 can only be accessed from the UI thread`。现在 WebView2 访问限制在 UI Thread，并使用缓存状态对外暴露。

### System.Text.Json 依赖
曾出现 `System.Runtime.CompilerServices.Unsafe` 缺失。当前依赖/加载方案已处理。

### Consumer UI 资源未复制
曾因 UI 未进入实际输出路径导致 ContentRoot 注册失败。当前 Consumer 已成功注册并导航。

### Overlay 闪烁
Captured 模式下 Overlay 自身成为前台时，旧逻辑误判为 Bannerlord 失焦并反复 Hide/Show。现已允许 Captured 模式下 Overlay foreground，实机已通过。

### Overlay/WebView2 渲染不可见（2026-08-14）
曾出现“HTML 不可见，但对应按钮区域仍可点击”的问题。已完成多轮 A/B 二分。

最终确认的工程规则：不要随意修改 Chromium/WebView2 内部子窗口的 Win32 extended style。尤其是对 `Chrome_RenderWidgetHostHWND` 设置 `WS_EX_TRANSPARENT` 的实验会重新触发相同的不可见问题。

当前已验证正常基线：`debug/test-root-transparent`。
完整复盘见：`Handoff/BUG_POSTMORTEM_OVERLAY_RENDERING_20260814.md`。

### Shutdown
曾出现 Framework 已关闭而 ConsumerScope 继续访问 HtmlUiService 的 12 条 ERROR。当前已有防御式 Dispose。