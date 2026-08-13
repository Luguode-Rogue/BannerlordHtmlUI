# v0.21

## WebView2 initialization deadlock fix

The WinForms UI thread now starts `Application.Run()` before asynchronous WebView2 environment/control initialization. Initialization is triggered from the form `Shown` event and no longer blocks the UI thread with `GetAwaiter().GetResult()`.

This fixes the real-world failure mode where `HtmlUiService` remained in `Initializing`, `WebViewReady` stayed false, and framework pages such as `diagnostics` were never registered because the `Ready` callback never fired.

### Expected log sequence

```text
WebView2 UI thread message loop is running; starting asynchronous WebView2 initialization.
WebView2 environment created.
EnsureCoreWebView2Async completed.
WebView2 ready. Host is operational.
```
