# v0.12

- 新增 `HtmlUiInputMode`：Hidden / Passive / Captured。
- `Show()` 改为显示 Passive UI，不再自动抢夺焦点。
- `ReleaseInput()` 改为释放输入并保持 UI 可见。
- 新增 `SetInputMode()` 公共 API。
- 增加被动窗口的 `WS_EX_NOACTIVATE` 与 `HTTRANSPARENT` 支持。
- 增加窗口状态说明与实机验收注意事项。
- 保留 CaptureInput 作为主动交互模式。

- 修正自定义 OverlayForm 与 Win32 API 的实际接线。
