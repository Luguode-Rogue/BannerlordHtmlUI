# v0.11.0

## 宿主窗口与输入

- HTML 宿主只在 Bannerlord 主窗口处于前台、可见且未最小化时跟随显示。
- Alt+Tab 到其他应用时宿主隐藏。
- Bannerlord 恢复前台后，若页面仍处于显示状态，宿主恢复。
- `CaptureInput()` 明确进入 HTML 交互模式。
- `ReleaseInput()` 隐藏宿主并尝试将 Bannerlord 主窗口恢复为前台。
- 增加 `HtmlUiHost.IsVisible` / `IsInputCaptured` 状态。

## API 文档

- 补充输入模式语义。
- 明确当前版本尚未实现真正的跨进程鼠标透明穿透。
- 增加窗口模式与实机验收说明。
