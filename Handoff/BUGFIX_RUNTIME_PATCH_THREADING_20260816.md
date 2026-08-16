# Runtime Patch Threading + StressLab Snapshot Fix — 2026-08-16

## 发现
实机启动日志出现两类 ERROR：

1. `HtmlUiBindingLifecyclePatch.Install()` 从 Bannerlord/game thread 访问 `WebView2.CoreWebView2`。
2. `HtmlUiErrorModelPatch.Install()` 从 Bannerlord/game thread 访问 `WebView2.CoreWebView2`。

日志同时出现：
- `CoreWebView2 can only be accessed from the UI thread.`
- 内层 `InvalidCastException` / `E_NOINTERFACE`

## 根因
`HtmlUiHost.ConfigureAfterWebViewReady()` 已经在专用 WebView2 UI thread 安装 Runtime-dependent patches。
但 `SubModule.RegisterFrameworkPages()` 又从 Framework `Ready` callback 重新调用了这两个安装方法。

`Ready` callback 运行在 Bannerlord/game side，因此第二次安装访问 `_web.CoreWebView2` 时跨线程，产生 ERROR。

## 修复
从 `BannerlordHtmlUI/SubModule.cs` 删除：
- `HtmlUiBindingLifecyclePatch.Install(HtmlUiService.Host)`
- `HtmlUiErrorModelPatch.Install(HtmlUiService.Host)`

并明确注释：WebView2-dependent Runtime patches 统一由 `HtmlUiHost.ConfigureAfterWebViewReady()` 在 WebView2 UI thread 安装。

## StressLab 另一个问题
StressLab 原本使用：

```js
app.request('framework.getDiagnostics')
```

Consumer `app.request()` 会按 owner/scope 规则解析请求名，因此实际请求变成：

```text
HtmlUiConsumerTestMod.framework.getDiagnostics
```

而 `framework.getDiagnostics` 是 Framework-owned request。

改为：

```js
window.game.request('framework.getDiagnostics')
```

## 验证依据
之前实机日志已经证明：
- `F6 Lifecycle Stress`: 20/20 PASS
- `ERROR/WARN`: 仅启动阶段出现上述两个线程错误
- `ESC`: `CoreWebView2Controller.AcceleratorKeyPressed` 正常触发并关闭页面

下一轮重新启动 Framework 后，预期启动日志不再出现上述两个 `Failed to install ... patch` ERROR；StressLab 的 `Run 1/10/50`、High Frequency、Binding Pressure 的 Snapshot 应该可以正常读取 Framework Diagnostics。
