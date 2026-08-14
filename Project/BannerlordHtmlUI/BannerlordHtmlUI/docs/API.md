# BannerlordHtmlUI API 参考

## HtmlUiService

| API | 用途 |
|---|---|
| `InitializeAsync(moduleDirectory, webRoot)` | 初始化 Framework Host；仅由框架自身的 SubModule 使用 |
| `OnReady(callback)` | Framework 可用后执行消费者 Mod 的注册逻辑 |
| `IsReady` | 检查 Framework 是否进入 Ready |
| `RegisterContentRoot(id, directory)` | 注册消费者 Mod 的 HTML/CSS/JS 根目录 |
| `IsInitialized` | 检查是否完成初始化 |
| `Pages` | 页面管理器 |
| `State` | 状态存储 |
| `Show()` / `Hide()` | 显示/隐藏 Host |
| `CaptureInput()` | 显示 UI 并把键盘/窗口焦点交给 HTML UI |
| `ReleaseInput()` | 隐藏 UI、释放焦点并尝试把 Bannerlord 主窗口置回前台 |
| `IsVisible` | UI 是否被框架请求显示 |
| `IsInputCaptured` | 是否处于 HTML 输入模式 |
| `OpenDevTools()` | 打开 DevTools |
| `Reload()` | 刷新当前页面 |
| `Tick()` | 在 Bannerlord Tick 中执行游戏线程队列 |
| `RegisterCommand(name, handler)` | 注册 JS Command |
| `RegisterRequest(name, handler)` | 注册 JS Request |
| `SendEvent(name, payload)` | C# → JS 发送事件 |
| `Dispose()` | 释放 Host |

## HtmlUiPage

```csharp
new HtmlUiPage("id", "relative/path/index.html")
{
    HotReload = true,
    Opened = () => { },
    Closed = () => { }
};
```

## HtmlUiPageManager

```text
Register(page)
Open(id)
Close(id)
CloseCurrent()
Contains(id)
Unregister(id)
UnregisterByOwner(ownerId)
CurrentId
Current
All
Count
Reload()
```

页面、Command、Request 都要求唯一的完整注册名。重复注册不会静默覆盖已有对象，而是抛出异常，并保留原注册者。

`Unregister(id)` 如果注销的是当前打开页面，会执行该页面的 `Closed` 回调，并同时发布 `framework.page.lifecycle` 的 `closed` 状态、更新 State、隐藏 Host，保证与 `CloseCurrent()` 的生命周期语义一致。`UnregisterByOwner(ownerId)` 使用相同规则清理所属页面。

## HtmlUiStateStore

```text
Set(key, value)
TryGet(key, out value)
Remove(key)
SnapshotJson()
GetSnapshot()
Count
```

State 更新通过 `state:<key>` 事件广播。`Remove(key)` 会从 Store 删除键，并在同一个 `state:<key>` 通道发送 `null`，因此 `game.state.subscribe(key, ...)` 和 binding 会同时感知删除；不存在的键不会产生删除事件。

对于字符串、数字、布尔值、日期、GUID 等高频标量状态，Store 使用轻量比较路径避免不必要的 JSON 转换；复杂对象仍使用 JSON deep-equality 保持原有变更判断语义。

Consumer 推荐通过 `HtmlUiConsumerScope` 注册资源。Scope 会为 Page、Command、Request、State 和 ContentRoot 自动加 Owner 前缀，并在 `Dispose()` 时按拥有关系清理。需要主动删除自己拥有的状态键时，可以调用 `scope.RemoveState(key)`。

Scope 的 `Opened` / `Closed` Page 回调在 `Dispose()` 期间仍处于 scope 的清理阶段；scope 只有在全部所属资源清理完成后才进入 `IsDisposed == true`。重复调用 `Dispose()` 会直接返回。

Framework 内置 `framework.getStateSnapshot` Request 返回当前 C# StateStore 快照。页面每次导航都会重新创建 JS Runtime，因此 Framework 会在 document bootstrap 阶段恢复该快照，使页面切换/Reload 后 `game.state` 与 C# StateStore 保持一致。

## JS Runtime API

### Command

```javascript
game.call(name, payload)
```

Command 如果由普通应用代码调用，会得到成功或错误结果；框架内部的 `runtime.error` 诊断消息为 fire-and-forget。

### Request

```javascript
await game.request(name, payload, timeoutMs)
```

已注销或在执行期间被注销的 Command/Request 不再无声丢弃。若仍有对应请求 ID，Bridge 会立即返回明确错误，使 JS 不必等待默认超时。

### Event

```javascript
const unsubscribe = game.on(name, handler);
unsubscribe();
```

### State

```javascript
game.state.get(key)
game.state.subscribe(key, handler)
game.state.snapshot()
```

### I18n

```javascript
await game.i18n.t('my.key')
await game.i18n.t('my.key', { count: 3 })
await game.i18n.getLocale()
game.i18n.getLanguages()
game.i18n.formatDate(value)
game.i18n.formatTime(value)
```

Pages can also use attribute-based binding:

```html
<span data-bhui-i18n="my.key"></span>
<input data-bhui-i18n-placeholder="my.placeholder">
<div data-bhui-i18n-title="my.title"></div>
<img data-bhui-i18n-alt="my.alt">
```

`game.i18n.bind(root)` applies the current translations and returns a disposer. Locale-change handling is part of the framework runtime; the binding automatically reapplies translations for the elements captured by that binding. If the binding root or its elements are destroyed, dispose it from the owning page/component; the runtime also releases the binding on `pagehide`. Asynchronous translation results from an older generation are ignored after a newer refresh or disposal.

### Binding

The runtime exposes state binders through `game.bind` (or `game.app.bind` for consumers):

```javascript
game.bind.text('#title', 'ui.title')
game.bind.value('#name', 'player.name')
game.bind.checked('#enabled', 'settings.enabled')
game.bind.disabled('#submit', 'ui.submitDisabled')
game.bind.hidden('#panel', 'ui.panelHidden')
game.bind.visible('#panel', 'ui.panelVisible')
game.bind.attr('#link', 'href', 'ui.url')
```

Two-way binding supports optional debounce/throttle scheduling:

```javascript
game.bind.twoWayValue('#name', 'player.name', (value) => {
  // Persist or forward value to the game.
}, { debounce: 150 });
```

List/template/component helpers return disposers and should be disposed when their owning page/component is destroyed. `game.bind.dispose()` removes bindings registered by that binder instance. Individual disposer functions are currently idempotent but may remain referenced internally until the parent binder is disposed; avoid creating unbounded numbers of short-lived binder instances in a long-lived page. This is a runtime implementation optimization target, not a public API semantic requirement.

## 协议

协议定义见 `docs/PROTOCOL.md`。

当前协议版本：`1`。

框架版本和通信协议版本是两个不同概念，升级框架实现时不应无意义地修改协议版本。

## Public API boundary

Consumer Mods should use `HtmlUiService`, `HtmlUiCommands`, `HtmlUiPage`, `HtmlUiPageManager`, and `HtmlUiStateStore`.
`HtmlUiHost` is an implementation type and should not be required by consumer code.

### Recommended consumer startup pattern

```csharp
HtmlUiService.OnReady(() =>
{
    var scope = HtmlUiService.CreateScope("MyMod");
    var moduleDirectory = Path.GetDirectoryName(typeof(MySubModule).Assembly.Location);
    var rootId = scope.RegisterContentRoot("ui", Path.Combine(moduleDirectory, "UI"));

    scope.RegisterPage(new HtmlUiPage("settings", "Settings/index.html")
    {
        ContentRootId = rootId,
        HotReload = true
    });
});
```

`InitializeAsync` belongs to the framework module itself. A consumer Mod must not call it.

### Runtime self-test

The bundled framework page exposes `framework.ping` and `framework.incrementTestState`. These are framework diagnostics only and are not intended as application API.

## Request handler threading

`RegisterRequest` handlers are entered from the Bannerlord game-thread dispatcher. Do not retain or access Bannerlord game objects from continuations after an `await`. If a request performs asynchronous work, copy the required game data into plain values first, then await external work and return plain data. For purely game-state reads, prefer a synchronous handler body.
