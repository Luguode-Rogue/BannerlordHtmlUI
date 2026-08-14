# v0.14

- Fixed browser response dispatch so WebView2 API calls always return to the WebView2 UI thread.
- Added `docs/THREADING.md`.
- Explicitly separated Bannerlord game-thread work from the WebView2 STA domain.
