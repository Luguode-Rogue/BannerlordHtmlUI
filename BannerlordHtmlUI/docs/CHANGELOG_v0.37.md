# v0.37.0

## Regression baseline fix

- Restored the Bannerlord-real-tested WinForms/WebView2 initialization sequence (`Form.Load` + `BeginInvoke` + async `EnsureCoreWebView2Async`).
- `IsWebViewReady` is now a volatile cross-thread state flag and never touches `CoreWebView2` from the Bannerlord game thread.
- Removed `System.Text.Json` from the runtime path; protocol parsing and serialization now use `Newtonsoft.Json`.
- Public Command/Request payload handlers use `Newtonsoft.Json.Linq.JToken`.
- Kept the consumer/front-end APIs from v0.36 unchanged at the JavaScript level.

## C# consumer migration

Replace handlers accepting `System.Text.Json.JsonElement` with `Newtonsoft.Json.Linq.JToken`.
