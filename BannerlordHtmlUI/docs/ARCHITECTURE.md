# Architecture

## Threads

```text
Bannerlord thread
    │
    ├── HtmlUiService.Tick()
    │       └── GameThreadDispatcher.Drain()
    │
    └── gameplay handlers

WebView2 STA thread
    │
    ├── browser initialization
    ├── JS bridge
    └── page rendering
```

WebView callbacks never execute gameplay handlers directly. They enqueue work into `GameThreadDispatcher`.

## Layers

- `HtmlUiService`: public framework facade.
- `HtmlUiHost`: WebView2/WinForms host.
- `HtmlUiBridge`: protocol transport and handler routing.
- `HtmlUiStateStore`: generic state/value synchronization.
- `HtmlUiPageManager`: logical page registration and lifecycle.
- `GameThreadDispatcher`: cross-thread handoff into Bannerlord's tick.
- `HtmlUiDevTools`: local development support.
- `HtmlUiLogger`: framework logging.

## Browser protocol

Message envelope:

```json
{
  "version": 1,
  "type": "request",
  "id": "uuid",
  "name": "example",
  "payload": {}
}
```

Supported types:

- `command`
- `request`
- `event`
- `state:set`
- `state:get`

## Page lifecycle

`Registered → Open → Navigated → Closed`.

Pages are logical entries. v0.10 keeps a single WebView host and navigates it between page URLs. A future version can add multiple concurrent browser hosts if required.

## Threading boundary

WebView2 callbacks execute on the WebView2 UI thread. Framework consumers must treat those callbacks as untrusted input and let Bannerlord game logic run through `GameThreadDispatcher` / `HtmlUiService.Tick()`.

## Public vs internal types

`HtmlUiService`, `HtmlUiCommands`, `HtmlUiPage`, `HtmlUiPageManager`, and `HtmlUiStateStore` form the consumer-facing layer. `HtmlUiHost`, `HtmlUiBridge`, and Win32/WebView2 integration remain implementation details.
