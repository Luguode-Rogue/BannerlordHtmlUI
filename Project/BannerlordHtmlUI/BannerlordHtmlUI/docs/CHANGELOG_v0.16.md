# v0.16

- Added `HtmlUiDiagnostics` and `framework.getDiagnostics`.
- Added `HtmlUiHost.IsWebViewReady`.
- Added `HtmlUiHost.GetWindowState()`.
- WebView2 initialization and process failures are now recorded by diagnostics.
- Added `web/diagnostics.html` as a framework self-test page.
- Enabled WinForms explicitly in the project file.
- Set the project platform target to x64 for the Bannerlord client environment.
- Framework version is now sourced from `HtmlUiDiagnostics.FrameworkVersion`.
