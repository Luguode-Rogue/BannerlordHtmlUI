# v0.44.0

## Runtime / Initialization
- Fixed a fatal runtime.js initialization-order bug: `createI18n()` touched `window.game` before it was created, crashing the whole runtime IIFE and leaving `window.game` undefined.
- Reordered initialization so `window.game` is created first, then `createI18n()` + `connectGame()` wire the locale listener, then `app`/scope is built.
- Consumer pages now correctly receive `window.game` and all bridge messages work again.

## Native Input / ESC
- Added native ESC fallback so the current page closes even when the WebView2 DOM never receives the key.
- `HtmlUiOverlayForm` handles `WM_KEYDOWN`/`WM_SYSKEYDOWN` for `VK_ESCAPE` and raises `EscapePressed`.
- `HtmlUiHost` subscribes ESC on both the overlay form and the WebView2 control (`KeyDown`/`PreviewKeyDown`), guarded by input mode and current page existence.

## Robustness
- `HtmlUiService` now invokes each consumer's `OnReady` callback in isolation. A single failing consumer (e.g. a bad content-root path) can no longer put the whole Framework into `Faulted` and break every other consumer.
- JS runtime error forwarder is now injected into all future documents via `AddScriptToExecuteOnDocumentCreated`, so errors in consumer pages are forwarded back and logged.

## Page Management
- Added `HtmlUiPageManager.Reload()` and `HtmlUiService.ReloadPage()`.
- Added `HtmlUiPageManager.Count`, `HtmlUiStateStore.Count`, `HtmlUiBridge.CommandCount`/`RequestCount`.
- Added `HtmlUiHost.WebView2Version`, `CurrentUrl`, `PageCount`, `StateCount`, `CommandCount`, `RequestCount`.
- Consumer scope gained `GetState(key)` to read back scoped state through the public API only.

## Overlay / HUD
- Added non-fullscreen transparent overlay support:
  - `HtmlUiPage.OverlayWidth` / `OverlayHeight` (nullable; null keeps full-screen behavior).
  - `HtmlUiPage.Transparent` flag.
  - `HtmlUiHost` sets WebView2 `DefaultBackgroundColor = transparent` (alpha=0).
  - `HtmlUiHost.ComputeOverlayBounds()` places overlay pages at a fixed size anchored bottom-right, letting the game show through transparent areas.
- Consumer second page is now a 360x260 bottom-right translucent HUD demo (resource bars + mini-map placeholder), Passive input mode (mouse passes through).

## Diagnostics
- `HtmlUiDiagnosticsSnapshot` extended with `WebView2Version`, `CurrentOwner`, `CurrentUrl`, `PageCount`, `StateCount`, `CommandCount`, `RequestCount`.
- Diagnostics F10 page renders these automatically.

## Version
- `HtmlUiDiagnostics.FrameworkVersion` updated to `0.44.0` (was `0.43.0`), matching `SubModule.xml`.
