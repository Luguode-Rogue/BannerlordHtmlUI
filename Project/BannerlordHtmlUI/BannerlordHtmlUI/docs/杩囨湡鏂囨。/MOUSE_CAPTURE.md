# Mouse-only input capture

`HtmlUiInputMode.MouseCaptured` is a page-host input mode for overlays that must receive mouse input without taking keyboard focus away from the game.

| Mode | Mouse | Keyboard | WebView focus |
| --- | --- | --- | --- |
| `Hidden` | No | No | No |
| `Passive` | Bannerlord | Bannerlord | No |
| `Captured` | HTMLUI | HTMLUI | Yes |
| `MouseCaptured` | HTMLUI | Bannerlord | No |

Use `HtmlUiMouseCapture.Capture()` when the page needs mouse interaction while the game must continue to receive normal keyboard input. Consumers should handle their own HTMLUI-owned hotkeys on the game side when using this mode.

`MouseCaptured` does not enable the WebView2 default context menu. Consumers that implement right-click commands should prevent the page context menu and handle the pointer event themselves.
