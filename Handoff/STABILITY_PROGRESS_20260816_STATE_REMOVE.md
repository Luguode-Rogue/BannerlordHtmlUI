# Stability Hardening — State Remove — 2026-08-16

## Completed

`HtmlUiStateStore.Remove(key)` already emits the dedicated protocol event:

`state-remove:<key>`

The current runtime, however, did not consume that event. As a result, a removed key could remain observable through the JS state API.

Implemented:

- Added `HtmlUiStateRemovalPatch`.
- Installed it from the Framework `OnReady` path.
- It is installed on the WebView2 UI thread.
- It is registered both for the current document and future documents.
- `state-remove:<key>` is converted into the existing state notification path so bindings/listeners receive the removal update.
- `game.state.has(key)` now reports `false` after removal.
- `game.state.get(key)` reports `undefined` after removal.
- `game.state.snapshot()` omits removed keys.
- A subsequent `state:<key>` update clears the removal marker and restores normal state behavior.

## Commits

- `f1ad49b13950aabb7fa97168a51e829c877fc8a0` — add state removal compatibility patch.
- `5e2ddbe4b82e1eea4ce11e353161a332914f1c8c` — install the patch from Framework startup.

## Important limitation

This is intentionally a compatibility layer rather than a rewrite of the validated `runtime.js` core. The runtime's internal `Map` still receives the existing `null` notification; the public `game.state` API is normalized to expose true removal semantics. A future runtime protocol revision can replace this shim with native `state-remove` handling.

## Next stability target

Proceed to WebView2 `ProcessFailed` recovery. Do not alter the validated Overlay rendering/window-style path unless a regression is directly reproduced.
