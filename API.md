# BannerlordHtmlUI API

当前开发线：`dev`。

## Framework facade

主要入口：

```text
HtmlUiService.InitializeAsync(moduleDirectory, webRoot)   # Framework 自用
HtmlUiService.OnReady(callback)
HtmlUiService.Pages
HtmlUiService.State
HtmlUiService.Show() / Hide()
HtmlUiService.CaptureInput() / ReleaseInput()
HtmlUiService.Reload()
HtmlUiService.RegisterCommand(...)
HtmlUiService.RegisterRequest(...)
HtmlUiService.SendEvent(...)
HtmlUiService.Dispose()
```

Consumer 应优先使用 `HtmlUiService`、`HtmlUiConsumerScope`、`HtmlUiPage`、`HtmlUiPageManager`、`HtmlUiStateStore`；不要依赖 `HtmlUiHost` 等实现类型。

## Page

```csharp
new HtmlUiPage("id", "relative/path/index.html")
{
    HotReload = true,
    Opened = () => { },
    Closed = () => { }
};
```

Page 生命周期统一由 `HtmlUiPageManager` 管理：Register / Open / Close / CloseCurrent / Reload / Unregister。

## JS

```javascript
game.call(name, payload, timeoutMs)
await game.request(name, payload, timeoutMs)
game.on(name, handler)
game.state.get(key)
game.state.subscribe(key, handler)
game.state.snapshot()
```

可取消 Request：

```javascript
const controller = new AbortController();
const p = game.requestCancellable('name', payload, 10000, controller.signal);
controller.abort();
```

Cancellation、timeout、pagehide、runtime shutdown 后的晚到结果不得覆盖新状态。

## State

```text
Set / TryGet / Remove / Snapshot
```

`Remove` 必须产生明确的 delete 语义；页面 Runtime 建立后通过 snapshot hydration 恢复 C# State。

## Binding / i18n

支持 State binding、two-way binding、list/template/component、i18n.bind、dynamic DOM binding、debounce/throttle。

长期 listener、MutationObserver、timer、request、component 必须有 dispose/pagehide 回收路径。

## Threading

Request handler 从 GameThread dispatcher 进入。`await` 之后不保证仍处于 GameThread；如果继续访问 Bannerlord API，必须显式回 `GameThreadDispatcher`。

`CoreWebView2` 只能在 WebView2 UI thread 使用。

## Browser policy

默认：

```text
Context Menu = disabled
DevTools/F12 = disabled
```

`framework.openDevTools` 等内部诊断入口也必须服从同一 DevTools policy。
