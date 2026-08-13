# v0.28

- Added page lifecycle events: opening, ready, closed.
- Added `game.page.lifecycle` and `game.page.onLifecycle(...)`.
- Added scoped `pageLifecycle.on(...)`.
- Added frontend error API: `game.errors.on(...)` and `game.errors.last`.
- Runtime now turns uncaught errors and unhandled promise rejections into structured frontend errors.
- Page lifecycle is restored from the C# state snapshot after reload.
- Added regression documentation for lifecycle and error handling.
