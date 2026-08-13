# v0.17

## Framework/API hardening

- Added `HtmlUiService.IsReady`.
- Added `HtmlUiService.OnReady(Action)` for consumer Mod integration without racing initialization.
- Added command-existence checks for framework bootstrap.
- Moved framework page registration behind the formal Ready event.
- Blocked WebView2 navigation outside the framework local virtual host.
- Rejected unsafe `../` page paths.
- Documented the framework-as-separate-module architecture and hard dependency model.
- Kept consumer Mod code independent from `HtmlUiHost` internals.

## Compatibility note

No gameplay-specific Bannerlord APIs were added in this release.
