# Golden Consumer Example

`examples/HtmlUiConsumerTestMod` is the canonical example for a third-party Mod consuming BannerlordHtmlUI.

The page should use:

```javascript
const app = game.app;
```

rather than manually constructing `OwnerId.` prefixes.

The example demonstrates:

- content-root registration
- page registration
- Command
- Request/Response
- Event
- State binding
- two-way form input
- input capture/release
- page lifecycle
- frontend error reporting
- consumer Scope cleanup

This example intentionally avoids WebView2 and `HtmlUiHost` internals.
