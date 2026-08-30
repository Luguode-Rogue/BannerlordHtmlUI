# GPU disable diagnostic

This branch sets `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--disable-gpu` before HtmlUiService initialization.

Purpose: isolate WebView2 Chromium GPU/D3D composition from the overlay visibility bug.

This is a diagnostic build, not a production fix.
