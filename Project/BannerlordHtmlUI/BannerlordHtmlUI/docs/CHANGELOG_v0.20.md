# v0.20.0

## Purpose
This release is a diagnostic/stability build for the formal Bannerlord **net472 + WinForms + WebView2** environment.

## Changes
- Added a temporary F10 diagnostic shortcut in `SubModule.OnApplicationTick`.
- Added structured logging around `Pages.Open()` and WebView2 navigation.
- Added deferred page opening when WebView2 is not yet ready.
- Added WebView2 `NavigationCompleted` diagnostics.
- Added `NavigationInProgress` to the diagnostics snapshot.
- Removed the redundant manual `runtime.js` include from `diagnostics.html`; the framework injects runtime.js.
- Applied formal environment compatibility fixes: WinForms Timer, `IndexOf` in place of unavailable `Contains` overload, and protected `OnApplicationTick`.

## Log
`Modules/BannerlordHtmlUI/BannerlordHtmlUI.log`
