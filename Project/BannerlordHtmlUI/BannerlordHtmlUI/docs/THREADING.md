# BannerlordHtmlUI 线程模型

Framework 明确分成两个线程域：

```text
WebView2 / UI STA
    │
    ├── HTML / JS
    ├── WebView2 COM API
    └── Browser response/event dispatch

Bannerlord Game Thread
    │
    ├── Mission
    ├── Agent
    ├── Hero
    └── Mod gameplay logic
```

## 规则

1. JS 消息从 WebView2 到达后，不直接执行 Bannerlord 游戏逻辑。
2. Command / Request 会进入 `GameThreadDispatcher`。
3. 游戏逻辑处理完成后，Response/Event 必须重新回到 WebView2 UI 线程。
4. `HtmlUiHost` 内部负责把 WebView2 API 调用 marshal 回 UI 线程。
5. Mod 作者可以在游戏线程安全地调用 `HtmlUiService.SendEvent` / `State.Set`；Framework 会负责回到浏览器线程。

## 为什么这样做

WebView2 对其浏览器对象存在线程关联；Bannerlord 的游戏对象同样不能被任意后台线程安全操作。Framework 因此不允许把两个线程域混在一起。

## 验收

- Request handler 可以访问 Bannerlord 游戏对象。
- Request 完成后 HTML 能收到 response。
- Command handler 抛异常时网页收到失败 response。
- State/Event 从游戏线程发布不会直接调用 WebView2。


### Request handlers

A request begins on the Bannerlord game-thread dispatcher. An `async` request handler may continue on another thread after an `await`; therefore Bannerlord API objects must not be accessed after that await unless the consumer explicitly marshals back to the game thread.
