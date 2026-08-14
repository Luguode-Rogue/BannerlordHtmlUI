# ARCHITECTURE

```text
Bannerlord
   |
   +-- BannerlordHtmlUI
   |      +-- WebView2 Host
   |      +-- Overlay
   |      +-- Page Manager
   |      +-- Input
   |      +-- State
   |      +-- Command
   |      +-- Request/Response
   |      +-- Event
   |      +-- Localization
   |
   +-- HtmlUiConsumerTestMod
          +-- HTML/CSS/JS
          +-- Bannerlord 游戏逻辑
```

Framework 提供基础设施；Consumer 自己拥有 UI 资源。

目标 JS API：

```js
const app = window.game.app;
await app.call("commandName", payload);
await app.request("requestName", payload);
app.state.subscribe("name", callback);
app.on("eventName", callback);
```

Localization 目标：

```text
Bannerlord Localization
        |
HtmlUiLocalizationService
        |
game.app.i18n
        |
HTML/CSS/JS
```

预期：
- `app.i18n.t(key)`
- `app.i18n.getLanguages()`
- `app.i18n.onLocaleChanged(...)`
- `data-bhui-i18n`

本地化失败不能清空 HTML 默认文本。
