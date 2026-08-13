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

## HtmlUiStateStore

```csharp
Set(key, value)
TryGet(key, out value)
Remove(key)
SnapshotJson()
```

## JS Runtime API

### Command

```javascript
game.call(name, payload)
```

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
    var moduleDirectory = Path.GetDirectoryName(typeof(MySubModule).Assembly.Location);
    HtmlUiService.RegisterContentRoot("MyMod", Path.Combine(moduleDirectory, "UI"));

    HtmlUiService.Pages.Register(new HtmlUiPage("settings", "Settings/index.html")
    {
        ContentRootId = "MyMod",
        HotReload = true
    });
});
```

`InitializeAsync` belongs to the framework module itself. A consumer Mod must not call it.

### Runtime self-test

The bundled framework page exposes `framework.ping` and `framework.incrementTestState`. These are framework diagnostics only and are not intended as application API.


## Request handler threading

`RegisterRequest` handlers are entered from the Bannerlord game-thread dispatcher. Do not retain or access Bannerlord game objects from continuations after an `await`. If a request performs asynchronous work, copy the required game data into plain values first, then await external work and return plain data. For purely game-state reads, prefer a synchronous handler body.
