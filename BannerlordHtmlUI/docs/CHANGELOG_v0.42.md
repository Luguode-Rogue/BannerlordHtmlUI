# v0.42.0

## Runtime logging

- Removed high-frequency per-tick `Window tracking:` log output.
- Window state changes are still logged only when the externally visible state changes.
- Warnings such as `Bannerlord main window could not be resolved` remain logged once per condition transition.

## Consumer shutdown

- `HtmlUiConsumerScope.Dispose()` now exits cleanly when BannerlordHtmlUI has already been shut down.
- Normal framework shutdown no longer emits a burst of `BannerlordHtmlUI is not initialized` cleanup errors.
- Local scope bookkeeping is still cleared even when the framework is already gone.
