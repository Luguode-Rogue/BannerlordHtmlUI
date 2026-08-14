# v0.15

- Added HtmlUiLifecycleState for framework lifecycle.
- Added window-state publication through HtmlUiStateStore.
- Added `HtmlUiHost.WindowStateChanged`.
- Added `HtmlUiService.NotifyGameContext()` for version-neutral consumer-owned game context state.
- Added JS lifecycle helpers.
- Fixed host window-state tracking so foreground/visibility/minimized/bounds changes are observable.
- Kept gameplay-specific state out of the core framework.
