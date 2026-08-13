# Page / Consumer Lifecycle

## Page lifecycle

A page moves through: registered -> opened -> active -> closed -> unregistered.

`CloseCurrent()` clears the active page before invoking the close callback and hiding the host. This prevents re-entrant callbacks from observing a stale current page.

## Consumer scope lifecycle

A consumer scope owns its content roots, pages, commands, requests, and state keys. Disposal order is:

1. Close an active page owned by the scope.
2. Unregister pages.
3. Unregister commands and requests.
4. Remove owned state.
5. Remove owned content roots.

Each cleanup step is isolated so one failure does not prevent the remaining resources from being released.

## UI callback isolation

WebView2 and WinForms objects are UI-thread-only. `HtmlUiHost` catches exceptions from scheduled UI callbacks and records them instead of allowing a single callback to tear down the WinForms message loop.
