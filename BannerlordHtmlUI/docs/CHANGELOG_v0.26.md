# v0.26

- Consumer scopes now close their active page before unregistering owned resources.
- Consumer cleanup isolates failures per resource instead of aborting on the first exception.
- Page close/open callbacks are now exception-isolated.
- `HtmlUiPageManager.CurrentId` is synchronized with its page registry.
- UI-thread callbacks are guarded so one callback exception cannot terminate the WebView2 WinForms message loop.
- Scope disposal logs explicit completion for easier diagnostics.
