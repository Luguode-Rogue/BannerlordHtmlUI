# Logging Policy

The framework intentionally avoids per-frame logging in normal operation.

## Normal logs

- WebView2 initialization milestones
- Page registration/open/close/navigation
- Window state changes when the state actually changes
- Errors and warnings

## Suppressed by default

The window follow timer runs every 100 ms, but it no longer writes a log line every tick.
This keeps `BannerlordHtmlUI.log` practical to attach and inspect during real gameplay.

## Shutdown

Consumer scope cleanup after framework shutdown is logged as `INFO`, not `ERROR`, because it is an expected teardown ordering condition.
