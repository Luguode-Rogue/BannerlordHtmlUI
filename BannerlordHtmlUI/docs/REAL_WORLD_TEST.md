# First In-Game Test

This is the first test to run in a real Bannerlord installation.

## Expected flow

1. Build the project with `BannerlordDir` pointing at the game directory.
2. Launch Bannerlord with the `BannerlordHtmlUI` module enabled.
3. Wait for the SubModule to initialize.
4. Open the framework diagnostics page with the framework integration that calls `framework.openDiagnostics`.
5. Check the diagnostics page.

## Pass criteria

- Framework lifecycle reaches `Ready`.
- `HostInitialized` is `true`.
- `WebViewReady` is `true`.
- The diagnostics page renders HTML/CSS correctly.
- `framework.getDiagnostics` returns successfully.
- `framework.lifecycle` is present in state.
- Window visibility/focus values change when Bannerlord is focused or minimized.
- Switching to `Captured` and back to `Passive` does not permanently steal keyboard focus.
- Exiting Bannerlord does not leave a WebView2 process/window behind.

## Failure classification

- No framework lifecycle: SubModule/build/load problem.
- Host initialized but WebView not ready: WebView2 runtime/initialization problem.
- WebView ready but page blank: navigation/local-host/resource problem.
- Page visible but request fails: JS/C# bridge problem.
- Page works but input is wrong: overlay/input problem.
- Everything works until game exit: lifecycle/disposal problem.


## v0.20 first diagnostic run

This build contains a temporary F10 shortcut. Press F10 once after entering the game. Then close the game and inspect `Modules/BannerlordHtmlUI/BannerlordHtmlUI.log`.

Expected sequence includes:

- `===== F10 DIAGNOSTICS OPEN =====`
- `Page open requested: diagnostics`
- `Navigate requested: page=diagnostics`
- `Navigating WebView2 to https://bannerlord-htmlui.local/diagnostics.html`
- `WebView2 navigation completed successfully.`

If the sequence stops, the last line identifies the failing layer.
