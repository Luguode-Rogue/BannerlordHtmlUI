# Runtime Baseline

The following host behaviors are treated as frozen regression-sensitive code:

- .NET Framework 4.7.2 / Bannerlord runtime compatibility.
- WinForms STA WebView2 host.
- Form.Load + BeginInvoke asynchronous WebView2 startup.
- `volatile bool _webViewReady`; game-thread code must never dereference `CoreWebView2`.
- WebView2 operations are confined to the UI thread.
- JSON runtime uses Newtonsoft.Json, not System.Text.Json.

Feature work must not replace these pieces without a new real-Bannerlord regression test.
