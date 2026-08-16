# Runtime / i18n / ESC 修复留档（2026-08-16）

## 1. 修复范围

本次修复针对 `BannerlordHtmlUI` 在 Bannerlord 内嵌 WebView2 环境中的两个实际问题：

1. Consumer Test UI 的 Localization 一直停留在“检测中”。
2. WebView2 获得焦点后按 `ESC` 无法可靠关闭当前 HTML UI。

修复在专用分支 `feature/runtime-modular-i18n-fix` 完成，并经过 Bannerlord 实机验证后合并回 `dev`。

合并提交：`71b31f1f7dda93ff490d7801f61844b16f2ba6d2`

基线：`22fa93e3f82263d71d95a2d511cde2bca6445330`

PR：`#1 fix: modularize runtime and restore i18n/ESC`

---

## 2. i18n 问题：最终根因与修复

### 2.1 原始 Runtime 初始化顺序

旧 `runtime.js` 中，`createI18n()` 在 `window.game` 完成创建之前执行，而 `createI18n()` 内部会立即调用：

```js
window.game.on('framework.i18n.localeChanged', emitLocale);
```

这造成 Runtime 初始化时序错误风险。

早期验证进一步发现，直接在 WebView2 `DocumentCreated` 注入的入口脚本里通过同步 XHR 加载其他 JS 模块也不可靠，因此最终没有采用“运行时动态加载模块”的方案。

### 2.2 最终模块化方案

源码拆成：

```text
web/
├─ runtime-bootstrap.js
├─ runtime-core.js
├─ runtime-i18n.js
└─ runtime.js
```

其中：

- `runtime-core.js`：冻结原有 Runtime Core，避免重写已稳定的 Request / State / Binding / Scope / Lifecycle。
- `runtime-bootstrap.js`：处理 `window.game` 初始化阶段的 listener 注册时序。
- `runtime-i18n.js`：独立承载 i18n API。
- `runtime.js`：保持为最终 DocumentCreated 注入入口。

**关键设计：源码分模块，但运行时仍只注入一个 `runtime.js`。**

构建阶段将三个模块按固定顺序合并到实际部署目录：

```text
runtime-bootstrap.js
        ↓
runtime-core.js
        ↓
runtime-i18n.js
        ↓
<deploy>/web/runtime.js
```

因此 WebView2 DocumentCreated 阶段不再需要同步 XHR / `eval()` 去读取相邻 JS 文件。

### 2.3 `game.app.i18n` 引用问题

模块化后，`runtime-core.js` 已经创建过 `window.game.app`，其中保存了旧的 i18n 实例：

```text
legacy i18n
    ↓
game.app.i18n
```

`runtime-i18n.js` 随后创建新的 i18n 实例并替换：

```text
game.i18n = newI18n
```

如果只替换 `game.i18n`，`game.app.i18n` 仍指向旧实例；而旧实例又会被 Dispose，Consumer 的：

```js
app.i18n.getLocale()
```

就会卡在等待状态。

最终修复为在替换时同步更新：

```js
game.i18n = i18n;
if (game.app && game.app.i18n === legacy) {
    game.app.i18n = i18n;
}
```

Dispose 时也同步清理 `game.app.i18n` 的引用。

### 2.4 实机验收结果

Consumer Test UI 最终显示：

```text
当前语言：简体中文
Title 翻译：HTML UI 框架测试
Bannerlord Localization 已加载，全部测试 Key 均已解析。
```

并且页面生命周期状态为：

```text
pageLifecycle.state = ready
pageId = HtmlUiConsumerTestMod.consumer.test
ownerId = HtmlUiConsumerTestMod
```

因此已确认：

```text
Bannerlord Localization
        ↓
HtmlUiLocalization
        ↓
JS i18n
        ↓
Request
        ↓
WebView2 Bridge
        ↓
HTML
```

链路恢复正常。

---

## 3. ESC 关闭问题：修复方案

### 3.1 原问题

当 WebView2 获得键盘焦点后，单纯依靠外围 WinForms `IMessageFilter` 并不能保证 `ESC` 能稳定到达 Host；同时原过滤器还限制在 `HtmlUiInputMode.Captured`，导致某些页面状态下无法关闭。

### 3.2 最终方案

ESC 现在采用分层处理：

```text
WebView2 keyboard accelerator
        ↓
HtmlUiHost.Pages.CloseCurrent()

若未命中
        ↓
HtmlUiOverlayForm.ProcessCmdKey

再兜底
        ↓
WinForms IMessageFilter
```

WebView2 的 accelerator 入口挂到实际 `CoreWebView2Controller` 的 `AcceleratorKeyPressed` 事件，而不是错误地挂到 WinForms `WebView2` 控件本身。

收到 `Escape` 的 KeyDown 后：

```text
e.Handled = true
        ↓
Pages.CloseCurrent()
```

同时 WinForms 全局 ESC 过滤器放宽为只要求：

```text
host != null
host.IsVisible
```

不再强制要求 `InputMode == Captured`。

### 3.3 实机结果

Consumer Test UI 已能通过 ESC 关闭界面，并可以再次打开进行测试。

---

## 4. 为什么不能只修 Localization C#

此前已实际验证 Bannerlord 原生 Localization 正常：

```text
key = HtmlUiConsumer_Title
text = HTML UI 框架测试
found = true
language = 简体中文
```

因此本次问题不是：

- `language_data.xml`
- strings 注册
- `LocalizedTextManager`
- `HtmlUiLocalization.Translate()`

而是在前端 Runtime / WebView2 Bridge 生命周期。

---

## 5. 后续注意事项

### Runtime 模块化原则

以后修改 Runtime 时：

- 源码可以继续按模块拆分。
- Bannerlord 实际运行时仍应注入一个确定的 `runtime.js`。
- 不要在 `AddScriptToExecuteOnDocumentCreatedAsync` 的脚本中依赖同步 XHR 加载兄弟 JS 文件。
- 不要异步插入多个 script 标签来替代一次性 Runtime 注入，以免重新引入 DocumentCreated / navigation race。

### i18n 引用一致性

修改 i18n 实例时必须同时考虑：

```text
game.i18n
game.app.i18n
```

不能只替换其中一个。

### ESC 输入链

ESC 修复属于 Host / WebView2 输入层，不应依赖 Consumer 页面自己监听 `keydown` 来关闭宿主 UI。

---

## 6. 当前状态

```text
Localization                     ✅ 实机通过
JS → Request → Bridge            ✅ 实机通过
Title Translation                ✅ 实机通过
Runtime 模块化                   ✅ 已合并
ESC Close                        ✅ 实机通过
F11 打开                         ✅ 已验证可重新进入页面

ProcessFailed Recovery           ⏳ 尚未完成完整实机回归
Reload / HotReload               ⏳ 后续回归
Request cancellation             ⏳ 后续回归
HWND=0 guard                     ⏳ 后续回归
StressLab                        ⏳ 后续回归
```
