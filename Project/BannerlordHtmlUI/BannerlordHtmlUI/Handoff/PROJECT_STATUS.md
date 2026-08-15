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
- 高频 scalar State 使用轻量比较路径，避免不必要的 JSON 转换。
- ConsumerScope 页面 ContentRoot 解析对空值安全，默认使用 consumer `ui` root。
- Bridge 重复注册保护：重复 Command / Request / Page 不再静默覆盖。
- `runtime.error` 等无 id 的 fire-and-forget 消息可以正常诊断，而普通无 id Command 会被拒绝。
- Bridge 协议异常与未知消息类型进入明确的错误路径，不再静默丢弃。
- Handler 返回结果时的 Response 发送失败已隔离；WebView 已关闭等情况下不会把二次发送异常继续逸出到回调线程。
- 页面切换、ConsumerScope Dispose 与 Request/Command 注销发生竞态时，旧 handler 不再静默丢弃请求；有效注册消失后会尽早返回明确错误，过期异步结果不会覆盖后续注册。
- Bridge 按名称注销已增加 owner 校验，并使用 owner + entry identity 的原子删除路径，避免误删其他 Consumer/Framework 注册以及检查后换绑造成的竞态删除。
- `UnregisterByOwner()` 也已改为 owner + entry identity 的原子删除，不会因为 Scope 清理期间同名注册换绑而误删新 Owner 的 entry。
- `HtmlUiPageManager.Count` / `Reload()` 已与文档 API 对齐。
- `HtmlUiStateStore.Count` 已开放给诊断层。
- `HtmlUiConsumerScope.RemoveState(key)` 已提供 Consumer 自有 State 的主动删除路径，并同步移除 Scope 的拥有记录。
- Consumer 测试页已加入 State 删除回归入口，并监听 `name` State 变化验证 `null` 删除传播。

### M3 Localization / Binding
Bannerlord 原生 Localization -> Framework -> `game.app.i18n` -> HTML。

已完成的静态加固：
- Localization 变量替换支持 primitive、日期、对象与数组。
- `TranslateMany` 对每个 key 的变量对象显式解析。
- TypeScript `.d.ts` 已同步 Runtime 实际暴露的 Binding API，包括 `twoWayValue`、`twoWayChecked`、`group`、`debounce`、`throttle`。
- 缺失 Localization key 的 WARN 按“语言 + key”去重，避免页面重复渲染刷屏。
- `i18n.bind()` 生命周期已具备 disposer、pagehide 自动清理、locale generation 防旧结果回写，并对同一刷新周期的同 key 翻译请求做合并。
- Keyed `bind.list()` 复用已有 child 时会调用 child `update(item, index, generation)`。
- `i18n.bind()` 支持动态 DOM：新增匹配节点自动加入绑定，Localization 属性变化自动重新绑定，删除节点/子树会回收对应 binding，dispose/pagehide 会断开 MutationObserver。
- 同一 `root` 重复调用 `i18n.bind()` 时会先自动 dispose 旧 binding，避免重复 locale listener / MutationObserver 累积。
- 多个连续 DOM mutation 会在 microtask 内合并为一次 refresh pass。

### State Bootstrap
- Framework 内置 `framework.getStateSnapshot` 用于页面 Runtime 初始 hydration。
- document-created bootstrap 已改为短时间重试等待 `window.game` / `game.request` 建立，不再依赖单次 microtask。
- 当前代码不新增高频运行日志；失败只在实际异常时通过页面 console 诊断。

### M6 Stability / Timer lifecycle
已发现并修复一个真实的 Binding scheduler 串扰问题：
- Runtime 原有 `scheduleWriter()` 把 debounce/throttle 状态挂在共享函数对象上，并用 DOM element 作为唯一 key。
- 两个独立 Binder 如果作用于同一个 element，会共享 timer；一个 Binder 的 dispose/新事件可能清掉另一个 Binder 的 debounce/throttle。
- 新增 `HtmlUiBindingSchedulerPatch`，在 Runtime 建立后隔离每个 Binder 的 scheduler 状态。
- `twoWayValue` / `twoWayChecked` 的 scheduler 现在按 Binder 实例隔离，并强制关闭 Runtime 内部 scheduler，避免双重计时。
- Binder 显式 disposer 和 `binder.dispose()` 都会清理该 Binder 的 debounce/throttle timer。
- `window.game.scope()` 创建的新 Scope Binder 也会自动套用该隔离层。
- Patch 已在 `SubModule.RegisterFrameworkPages()` 安装；新增 C# 文件位于 `src`，SDK 项目会自动编译。
- 这次修复没有修改 Overlay/WebView2/D3D/Chromium 子窗口样式。

当前重点：Binding locale refresh / disposal 压力场景、Template/List 长期运行、Request timeout / late response / shutdown race。

### API 边界
- `HtmlUiPage.RelativePath` 明确拒绝 rooted/absolute path，并继续拒绝 `..` 越界路径。
- Public API 文档已同步 `PageManager.Count/Current/Reload`、`StateStore.Count/GetSnapshot`、`ConsumerScope.RemoveState`。
- Command / Request / timeout / lifecycle 语义已开始固化到 Protocol/API 文档。
- Public API 下一阶段需要冻结 v0.44 对外语义：错误码、timeout、disposer、页面生命周期和 ownership 规则必须写成稳定契约。

### Diagnostics
- Framework version 与 v0.44 文档对齐为 `0.44.0`。
- F10 Diagnostics 可报告 PageCount / StateCount。
- Diagnostics 页面采用 500ms 轻量自动刷新，并带 in-flight 防重入与 unload disposer。
- Diagnostics snapshot 包含 `SnapshotUtc`、`CurrentPageOwner`、`CurrentPagePath`。
- 下一阶段增加 Bridge/Binding 运行态计数与失败摘要，但继续避免逐帧日志。

### Overlay / WebView2
- 当前已有经过实机验证的正常基线：`debug/test-root-transparent`。
- 该基线不是冻结状态；后续允许继续优化 Overlay、窗口层级、透明度、输入与 WebView2 渲染行为。
- 已知“画面不可见但点击区域存在”的复现与修复已经记录，不需要作为后续每轮普通开发的阻塞条件。
- 只有再次修改 Overlay/WebView2 渲染、窗口样式、D3D/Chromium 子窗口层级等相关代码时，才重新执行对应的已知回归测试。

## 后续里程碑
### M3 收口
- [x] `i18n.bind()` 多次调用幂等 / root 级 disposer 去重
- [x] 动态 DOM 删除节点后的 binding 清理
- [x] 连续 DOM mutation refresh 合并
- [ ] Binding locale refresh / disposal 压力验证
- [ ] Template / List 长期运行与 key diff 验收
- [ ] 大量 DOM Binding 性能检查

### M4 API / Protocol Stabilization
- [x] Command 基础成功/错误语义与 fire-and-forget runtime.error 区分写入文档
- [x] Owner / Scope 生命周期主要规则已有文档
- [x] Owner-scope 批量注销竞态已做 entry-identity 防护
- [ ] 完整统一错误模型（Command/Request/Timeout/Protocol）
- [ ] Timeout、取消、页面卸载语义最终冻结
- [ ] Page / ContentRoot / Reload 资源安全边界最终审计
- [ ] TypeScript `.d.ts` 与实际 Runtime API 一致性最终审计

### M5 Consumer / Diagnostics
- [ ] Consumer TestMod 覆盖 Command / Request / Event / State / Binding / i18n / Page 切换
- [ ] Diagnostics 增加非高频的运行态摘要：注册数、pending request、binding scope 数、错误摘要
- [ ] 新增明确的 smoke / lifecycle 回归入口，尽量一次操作覆盖一组生命周期

### M6 Stability / Performance
- [ ] 高频 State / Event 压力场景
- [ ] 大量 DOM Binding 场景
- [ ] 多 Page 快速切换 / Reload
- [ ] Request timeout / late response / shutdown race
- [x] Binding debounce/throttle 的 Binder 间 timer 串扰静态修复
- [ ] 长时间运行的 disposer / observer / timer 泄漏最终审计
- [ ] Framework 主线程 / WebView2 UI thread 边界最终审计

### M7 Release Baseline
- [ ] 清理遗留 debug 分支与实验开关
- [ ] 正常 Overlay/WebView2 基线对应回归矩阵
- [ ] 发布版日志默认低噪声，仅错误、生命周期和关键诊断输出
- [ ] API / Protocol / Consumer 接入文档完成
- [ ] 形成 v0.44 release checklist

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
- 多次 i18n.bind/dispose 的生命周期压力场景
- Two-way binding debounce/throttle 多 Binder 隔离
- Bridge 竞态下的旧 handler / 过期 response 行为

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
曾出现 Framework 已关闭而 ConsumerScope 继续访问 HtmlUiService 的 ERROR。当前已有防御式 Dispose。
