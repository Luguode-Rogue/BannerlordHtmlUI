# BannerlordHtmlUI Diagnostics

v0.16 adds a framework self-check endpoint and a diagnostics page.

## Endpoint

JavaScript:

```js
const diagnostics = await game.request("framework.getDiagnostics");
```

The result reports:

- framework/protocol version
- framework lifecycle
- WebView readiness
- current input mode
- current page
- DevTools/Hot Reload flags
- Bannerlord window visibility/focus/minimized state
- last recorded WebView2 process error

## Why this exists

When the framework is first tested inside Bannerlord, the first question should be **which layer failed**:

1. Bannerlord SubModule loading
2. HTML UI initialization
3. WebView2 initialization
4. local page navigation
5. JavaScript runtime
6. C# ↔ JS bridge
7. input/window handling

The diagnostics endpoint exposes enough state to distinguish those layers without attaching a debugger first.

## Diagnostics page

`web/diagnostics.html` is a framework-only test page. It contains no application-specific Mod logic.

A consumer Mod can register it like any other page:

```csharp
HtmlUiService.Pages.Register(
    new HtmlUiPage("diagnostics", "diagnostics.html") { HotReload = true });
```

Then:

```csharp
HtmlUiService.Pages.Open("diagnostics");
```
