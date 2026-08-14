# PROJECT STATUS

## 已实机验证
- WebView2 初始化
- WebView2 UI Thread
- F10 Diagnostics
- Consumer OnReady
- ContentRoot 注册
- Consumer Page 注册
- F11 打开
- HTML Navigation
- 页面关闭
- F11 再打开
- Captured Input
- Overlay 防闪烁
- Close UI 一次点击关闭
- Close 后 Hidden
- Consumer Shutdown 防御式清理
- Framework 主日志取消逐帧 Window Tracking

## 当前工作
Bannerlord 原生 Localization -> Framework -> game.app.i18n -> HTML

## 待验收
- Command
- Request / Response
- Event
- State
- Two-way binding
- Input Capture / Release 完整验收
- Localization
- Language Switch

## 已解决的历史问题
### WebView2 跨线程
曾出现 `CoreWebView2 can only be accessed from the UI thread`。现在 WebView2 访问限制在 UI Thread，并使用缓存状态对外暴露。

### System.Text.Json 依赖
曾出现 `System.Runtime.CompilerServices.Unsafe` 缺失。当前依赖/加载方案已处理。

### Consumer UI 资源未复制
曾因 UI 未进入实际输出路径导致 ContentRoot 注册失败。当前 Consumer 已成功注册并导航。

### Overlay 闪烁
Captured 模式下 Overlay 自身成为前台时，旧逻辑误判为 Bannerlord 失焦并反复 Hide/Show。现已允许 Captured 模式下 Overlay foreground，实机已通过。

### Shutdown
曾出现 Framework 已关闭而 ConsumerScope 继续访问 HtmlUiService 的 12 条 ERROR。当前已有防御式 Dispose。
