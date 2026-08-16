# Runtime 模块化 / i18n 修复分支

分支：`feature/runtime-modular-i18n-fix`

基线：`dev` @ `22fa93e3f82263d71d95a2d511cde2bca6445330`

## 目的

该分支专门处理 BannerlordHtmlUI 当前 Runtime 的初始化顺序问题，并为后续维护建立模块化 JavaScript 结构。

当前核心故障是旧 `runtime.js` 在 `window.game` 完成创建之前执行 `createI18n()`，而 `createI18n()` 会立即调用 `window.game.on(...)`。这会使 Runtime 存在初始化时序异常风险，并导致后续 `window.game → i18n → request → postMessage` 链无法可靠建立。

## 分支原则

- `dev` 保持不动，本分支所有修改只用于 Runtime 模块化与 i18n 修复。
- 旧 Runtime 核心代码首先原样冻结为 `runtime-core.js`，避免为了拆分而重写已经稳定的 Request / State / Binding / Scope / Lifecycle 逻辑。
- `runtime.js` 仅作为轻量入口，负责按固定顺序加载模块。
- `runtime-bootstrap.js` 负责解决 `window.game` 在旧 Core 初始化期间的监听注册时序问题。
- `runtime-i18n.js` 独立承载新的 i18n API 实现，并在 Core 成功建立后接管 `game.i18n`。
- WebView2 仍只注入一个入口脚本；模块之间使用同步、确定性的加载顺序，避免新增异步 script loading race。

## 当前预期加载顺序

```text
runtime.js
    ↓
runtime-bootstrap.js
    ↓
runtime-core.js
    ↓
runtime-i18n.js
    ↓
HtmlUiI18nBindingPatch / 其他现有 Runtime Patch
```

## 验收重点

1. Runtime 可以正常建立 `window.game`。
2. `window.game.i18n` 能正常建立。
3. `app.i18n.getLocale()` 能进入 `framework.i18n.getLocale` Request。
4. `app.i18n.t('HtmlUiConsumer_Title')` 能进入 `framework.i18n.translate` Request。
5. Chrome/WebView2 `postMessage()` 与 C# `HtmlUiBridge.OnWebMessageReceived()` 恢复完整往返。
6. State / Command / Request / Binding / Lifecycle 不因模块化回归。
7. F11 / ESC / Reload / HotReload 后 Runtime 可以重新初始化。

## 暂不处理

ProcessFailed 实机 Recovery、StressLab 长测、完整多页面回归等继续沿用 `dev` 的后续计划，在本分支先不扩展范围。
