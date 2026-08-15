# Stability Hardening Progress — 2026-08-16

## Current baseline

The `dev` branch is being advanced from the already-working multi-page baseline. `main` remains the stable/archive baseline and is not being used as the active development target.

Do not rework the already validated Overlay/WebView2 rendering path unless a regression is directly reproduced there.

## Completed in the current stability pass

- Page transition serialization (`Open` / `Close` / `CloseCurrent` / `Unregister`).
- Navigation failure rollback so an invalid page does not remain logically open.
- Global ESC message-filter installation/uninstallation symmetry.
- Binding component wrapping without losing prototype / symbol / non-enumerable properties.
- Protection against temporary Bannerlord `HWND == 0` incorrectly hiding an active page.
- Navigation race guard with navigation identity checks.
- Navigation patch lifecycle cleanup when the Host is disposed.
- Navigation patch re-install support for a newly created Host instead of trusting stale process-global installation state.

## Current code audit finding

`HtmlUiHost` currently subscribes to `CoreWebView2.ProcessFailed`, but the handler only records the failure and emits diagnostics. It does **not** yet perform recovery.

Therefore the next stability feature is not another UI-layer workaround. It is a controlled WebView2 recovery state machine.

## ProcessFailed recovery design

Required state transitions:

```text
Operational
   |
   | ProcessFailed
   v
Recovering
   |
   +--> reject/ignore new navigation while recovering
   |
   +--> detach old CoreWebView2 event handlers
   +--> dispose failed WebView2 instance
   +--> create a fresh WebView2 instance on the existing UI thread
   +--> EnsureCoreWebView2Async
   +--> reinstall virtual-host mappings
   +--> reinstall framework runtime / error forwarder
   +--> recreate/reattach Bridge
   +--> reinstall runtime patches exactly once
   +--> restore readiness
   +--> restore the previously requested page if one existed
   v
Operational

Failure during recovery
   |
   v
Faulted / hidden
```

### Important invariants

1. Recovery must happen only on the WebView2 UI thread.
2. A failed old `CoreWebView2` instance must not receive events after replacement.
3. Runtime patches must have symmetric uninstall/reinstall behavior.
4. A stale navigation completion must never restore an older page.
5. `requestedVisible` and `inputMode` are logical state and must not be lost merely because the Chromium process failed.
6. A failed recovery must leave the overlay hidden and the public readiness state false rather than presenting a half-initialized WebView.
7. Recovery must not create a second WebView2 UI thread or a second WinForms message loop.
8. Recovery must not restore a page that the consumer closed while recovery was in progress.

## Next implementation order

1. Introduce an explicit Host recovery generation/state token.
2. Extract WebView2 event subscription/unsubscription into symmetric methods.
3. Extract WebView2 instance creation/configuration from initial startup so recovery can reuse the exact same path.
4. Replace the current `ProcessFailed` logging-only handler with a serialized recovery request.
5. Rebind the Bridge and runtime after successful recreation.
6. Revalidate PageManager navigation identity against the recovery generation.
7. Only then add StressLab ProcessFailed/reinitialize diagnostics.

## Explicitly deferred

- No changes to normal i18n rendering.
- No changes to the validated Overlay Win32 styles.
- No F12-based lifecycle semantics.
- No restoration of per-frame Window Tracking logs.
- No unrelated new public API until the recovery lifecycle is stable.
