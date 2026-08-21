# Bug 修复经验库

> 统一历史 Bug 检索入口。完整排错过程继续保留在 Handoff/Postmortem；本文只维护可复用结论、代码归属和快速定位。

## 1. 生命周期 / 输入 / Window

### HWND 临时为 0

`HWND = 0` 是暂时无法解析窗口，不等于游戏退出。代码归属：`HtmlUiWindowTracker`。不得在 Host、Keyboard Patch 或 Consumer 各写一套判定。

### 100ms Window Follow

历史 `FollowBannerlordWindow()` 同时承担位置、可见性、焦点判断，制造状态覆盖和卡死风险。新窗口同步统一使用 Event-driven `HtmlUiWindowTracker`；禁止恢复第二个 Timer。

### Passive 抢输入

`Passive` = 可见但 HTML 不拥有输入。只读 Consumer 应使用 Framework `Passive`，不要增加 Consumer Harmony 输入 Patch。

### F12 / DevTools / Right-click

Framework 默认禁止浏览器菜单和 DevTools。Browser policy 只有一个 owner；Keyboard Patch 只是最终安全兜底。F12 不是关闭协议。

### ESC

验收证据：

```text
ESC filter installed
→ Escape detected
→ CloseCurrent completed
→ currentPage=<null>
→ inputMode=Hidden
→ hostVisible=False
```

ESC 安全过滤归 Framework；页面是否允许 ESC 由 Page `CloseOnEscape` 决定。

## 2. WebView2 / Overlay

### UI Thread

`CoreWebView2` 只能由 WebView2 UI thread 访问。Patch 必须等 `EnsureCoreWebView2Async()` 完成后安装。Consumer 不可直接访问 CoreWebView2。

### Overlay 不可见但仍可点击

不要随机修改 Chromium child-window extended style。特别禁止重新引入 `Chrome_RenderWidgetHostHWND + WS_EX_TRANSPARENT` 方案。

### Captured 闪烁

Overlay 自己成为 foreground 不等于游戏失焦。WindowTracker 与 InputController 必须保持窗口事实/输入语义分离。

## 3. Page / Navigation

### Page Id

Page 构造必须保证非空合法 Id，避免 `Dictionary.ContainsKey(null)`。

### Navigation Race

快速 Open/Close/Reload 时旧 Navigation/async completion 不得覆盖新页面状态。代码归属：PageManager + Navigation race guard。

## 4. Bridge / Request / Cancellation

`BridgeRequestCount` 是注册数；`ActiveRequestCount` 才是当前活跃可取消 Request。

Shutdown 先 Cancel，再销毁 Host/WebView2。

Owner 注销必须使用 owner + entry identity，不能“检查后按名字删除”。

取消/timeout/pagehide/runtime shutdown 后的晚到成功结果不得覆盖新状态；维持 request identity / cancellation generation。

## 5. Runtime / Binding / i18n

Component 不得用 object spread 生成替代对象；保留原 prototype/Symbol/non-enumerable 成员。

i18n.bind 必须处理 dispose、pagehide、locale generation、重复 bind、动态 DOM、MutationObserver 清理和 mutation 合并。

翻译异常先区分硬编码与 Key → Runtime → Bridge → Bannerlord Localization 链路断裂，不要仅看 XML。

## 6. Build / Deploy

Consumer 找不到 Framework DLL：检查模块加载顺序与部署目录。

HTML 找不到：以运行时 DLL 的 `Assembly.Location` 与 `.csproj` Deployment Target 为准，不猜源码 bin 目录。

C# 10/net472 保持现有 LangVersion；不要用提高 LangVersion 解决级联编译错误。

## 7. 模块定位速查

| 症状 | 首选模块 |
|---|---|
| HWND / Window / Bounds / Minimize | `HtmlUiWindowTracker` |
| Hidden / Passive / Captured | `HtmlUiInputControllerPatch` |
| ESC / F12 safety | `HtmlUiKeyboardAndDiagnosticsPatch` |
| Right-click / DevTools | Browser policy |
| Page Open/Close/Reload | `HtmlUiPageManager` |
| Request / Cancellation / Owner | `HtmlUiBridge` |
| State | `HtmlUiStateStore` |
| GameThread queue | `GameThreadDispatcher` |
| TacticalMap specific | `New_ZZZF.TacticalMap` |
| CustomSkill specific | `New_ZZZF.CustomSkill` |

## 8. 核心规则

```text
先找状态 owner
→ 再改 owner
→ 检查是否已有第二套 workaround
→ 再测试
```

如果两个模块同时修改同一状态，先拆职责，禁止继续追加第三个补丁点。

完整历史证据仍以 Handoff/Postmortem/CHANGELOG 为准，不把历史摘要当作当前代码事实。
