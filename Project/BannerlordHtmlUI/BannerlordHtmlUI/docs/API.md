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

```csharp
Register(page)
Open(id)
Close(id)
CloseCurrent()
Contains(id)
CurrentId
All
```

页面、Command、Request 都要求唯一的完整注册名。重复注册不会静默覆盖已有对象，而是抛出异常，并保留原注册者。

## HtmlUiStateStore

```csharp
Set(key, value)
TryGet(key, out value)
Remove(key)
SnapshotJson()
```

State 更新通过 `state:<key>` 事件广播。`Remove(key)` 会从 Store 删除键，并在同一个 `state:<key>` 通道发送 `null`，因此 `game.state.subscribe(key, ...)` 和 binding 会同时感知删除；不存在的键不会产生删除事件。

Consumer 推荐通过 `HtmlUiConsumerScope` 注册资源。Scope 会为 Page、Command、Request、State 和 ContentRoot 自动加 Owner 前缀，并在 `Dispose()` 时按拥有关系清理。

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
