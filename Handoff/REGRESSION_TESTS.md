# BannerlordHtmlUI 回归测试清单

> 建立时间：2026-08-16
> 目的：记录已经通过实机测试/二分定位过的高风险回归点，后续 Stability Hardening 与新功能开发不得重新引入这些问题。

## 1. Overlay/WebView2 渲染不可见

### 已知症状
- 页面注册成功。
- WebView2 导航成功。
- 页面实际存在，鼠标仍可能点击到按钮。
- 但 HTML 内容不可见。

### 已确认高风险触发方式
不要默认修改 Chromium/WebView2 内部子窗口的 Win32 extended style，尤其是：
- `Chrome_RenderWidgetHostHWND`
- `WS_EX_TRANSPARENT`

历史二分已经证明，对该子窗口设置不合适的透明/穿透样式可以重新触发“可点击但不可见”。

### 验收
- F11 打开测试页面后，完整 HTML 内容可见。
- 页面按钮可见且可点击。
- 关闭后 Overlay 完全隐藏。
- 重复打开/关闭至少数次后表现一致。

## 2. 多页面生命周期

### 验收路径
- 注册至少 3 个页面。
- A → B → C 依次打开。
- 关闭 C 后重新打开 B。
- 再打开 A。
- 页面均必须正常导航并显示。

### 禁止回归
- 不得因为 PageManager 的 transition lock、navigation guard 或 lifecycle patch 导致第 2/3 页面无法打开。
- 不得出现逻辑 `currentPage` 与实际 WebView 页面不一致。

## 3. F11 / ESC / Close

### 验收
- F11 打开页面。
- ESC 可以关闭页面。
- F11 再次打开。
- 连续重复打开/关闭。
- CloseCurrent 后必须为 `currentPage=<null>`、`visible=False`、`inputMode=Hidden`。

F12 不属于正式可靠关闭机制，仅可作为测试 fallback。

## 4. Bannerlord 主窗口 HWND 短暂为 0

### 规则
主窗口句柄短暂解析为 0 时，不得因为这一瞬态状态直接隐藏一个正常工作的 HTML Overlay。

### 验收
- 打开 UI 后发生 Alt-Tab / 窗口焦点变化。
- Overlay 不得无故消失。
- 只有确认主窗口确实不可用/关闭时才进入最终隐藏路径。

## 5. 右键菜单

### 验收
- F11 打开 HTML 页面。
- 在网页区域点击鼠标右键。
- 不得出现 Chromium/WebView2 默认网页上下文菜单。
- 左键、滚轮、输入捕获仍正常。

当前实现使用 WebView2 `AreDefaultContextMenusEnabled = false`。

## 6. i18n / Binding

### 验收
- 页面首次打开后所有测试按钮和标题均有正确文本。
- 不得出现部分按钮翻译为空。
- 翻译文本存在时对应控件仍可点击。
- 页面重新打开后结果一致。

### 历史风险
Binding Component 包装曾经使用对象展开复制：
`{ ...component, dispose }`
该方式可能丢失 prototype、Symbol 和 non-enumerable 属性。
正式实现应保留原 Component 对象，仅替换 dispose 行为。

## 7. State / Request 生命周期

### 验收
- State.Remove 后 JS `state.has(key)` 必须反映真正删除语义。
- ConsumerScope Dispose 后，不得继续留下 owner 的活动 Request。
- cancellable request 的取消、超时、owner 销毁后均不得产生长期残留。

## 8. WebView2 ProcessFailed / Shutdown

### 验收
- Framework 正常初始化。
- WebView2 ProcessFailed 时不得留下假性的 Ready 状态。
- Framework Shutdown 后 ESC filter、NavigationCompleted handler、ConsumerScope 等必须释放。
- 重新启动下一次游戏时不得受到上一实例残留状态影响。

## 9. HotReload / Page Reload 生命周期

### 验收
- 手动 `Pages.Reload()` 仅允许在当前 Page、Host Visible、WebView2 Ready 时执行。
- Reload 开始时发布 `framework.page.lifecycle = reloading`。
- NavigationCompleted 成功后再次发布 `framework.page.lifecycle = ready`。
- Reload 过程中立即 `CloseCurrent()`，不得让旧页面的 reload 完成后把生命周期重新写回 `ready`。
- Reload 过程中快速再次触发文件变化，应被 75ms debounce；不能产生多次并发 Reload。
- 页面关闭后残留 FileSystemWatcher 触发的 reload 必须被忽略。

## 10. 日志约束

正式版本不要恢复逐帧 Window tracking 日志。仅在状态实际变化、异常或明确诊断模式下记录。

应保留：
- 初始化/失败
- Page Open / Close / Navigate
- NavigationCompleted
- 输入模式关键状态变化
- JS runtime error
- ProcessFailed
- Shutdown
- 明确的回归实验结果

## 回归原则

任何涉及 Overlay、WebView2 子窗口、PageManager、Input、i18n Binding、HotReload 的改动，都必须以已经验证正常的版本为基线，并尽量保持单变量修改。

若出现“页面可导航、可点击但不可见”，优先检查 Overlay/WebView2 合成链和 Chromium 子窗口样式，不要重新把问题归因到页面注册或 URL。
