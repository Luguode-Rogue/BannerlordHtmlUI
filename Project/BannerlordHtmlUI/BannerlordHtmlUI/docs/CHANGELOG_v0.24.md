# v0.24

- Added `HtmlUiConsumerScope` for consumer-Mod lifecycle ownership.
- Added automatic consumer name scoping for commands, requests, events, and state keys.
- Added content-root ownership/cleanup.
- Added page ownership and bulk cleanup support.
- Added consumer-scope documentation and updated the consumer test Mod.
- Consumer unload now uses a single `Dispose()` call instead of manual unregister lists.
