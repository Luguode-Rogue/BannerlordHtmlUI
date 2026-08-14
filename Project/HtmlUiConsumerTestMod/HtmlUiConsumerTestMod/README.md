# HtmlUiConsumerTestMod v0.2

This is a standalone consumer Mod for BannerlordHtmlUI.

## Install

1. Ensure BannerlordHtmlUI is installed and enabled first.
2. Build this project against your existing Bannerlord development references.
3. Copy the resulting `HtmlUiConsumerTestMod` module to `Modules/`.
4. Enable both `BannerlordHtmlUI` and `HtmlUiConsumerTestMod`.

## Test

- F11 opens the consumer HTML page.
- F12 closes it.
- `HtmlUiConsumerTestMod.log` is written beside the consumer DLL/module and records:
  - module load
  - Framework OnReady
  - content root registration
  - page registration
  - F11/F12 detection
  - page Open/Close result
  - registration failures

The consumer no longer uses System.Text.Json. It matches the current BannerlordHtmlUI JToken public API.
