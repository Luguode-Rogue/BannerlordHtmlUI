# BannerlordHtmlUI 输入模型 v0.13

## 三种宿主模式

- `Hidden`: WebView2 覆盖层隐藏。
- `Passive`: 页面可见，但宿主窗口请求鼠标穿透且不主动抢焦点；适合 HUD、信息面板。
- `Captured`: 页面可见并获得交互焦点；适合菜单、设置、编辑器。

C#：

```csharp
HtmlUiService.SetInputMode(HtmlUiInputMode.Passive);
HtmlUiService.SetInputMode(HtmlUiInputMode.Captured);
HtmlUiService.SetInputMode(HtmlUiInputMode.Hidden);
```

JS：

```javascript
await game.input.passive();
await game.input.capture();
await game.input.release();
await game.input.setMode('Captured');
```

## 页面默认策略

每个 `HtmlUiPage` 可以指定默认输入模式：

```csharp
Pages.Register(new HtmlUiPage("hud", "HUD/index.html")
{
    DefaultInputMode = HtmlUiInputMode.Passive
});

Pages.Register(new HtmlUiPage("settings", "Settings/index.html")
{
    DefaultInputMode = HtmlUiInputMode.Captured
});
```

打开页面后 Framework 会自动应用该页面的默认模式。

## 页面级 Escape 策略

默认情况下，Framework 会把 Escape 解释为“关闭当前 Page”。如果一个页面自己拥有 Escape 状态机，可以显式关闭宿主的 Page-close 行为：

```csharp
Pages.Register(new HtmlUiPage("tacticalmap", "TacticalMap/index.html")
{
    DefaultInputMode = HtmlUiInputMode.Passive,
    CloseOnEscape = false
});
```

此时 Framework 的 Overlay、全局键盘过滤器和 WebView2 `AcceleratorKeyPressed` 都不会消费 Escape，Captured 状态下按键可以继续送入页面自己的 JavaScript `keydown` 处理。

## 关于“区域级穿透”

当前版本做到的是**页面级输入策略**。一个 WebView2 原生窗口的 Win32 命中测试不能直接读取 DOM 的每个元素，因此不能仅靠 `WM_NCHITTEST` 实现可靠的“这个 HTML 按钮拦截、旁边区域把鼠标交给 Bannerlord”的跨窗口区域穿透。

因此 v0.13 不伪装成已经完成 DOM 级穿透。页面内部可以通过 `game.input.capture()` / `release()` 主动切换交互模式；真正的 DOM 命中区域代理会作为后续输入架构单独实现。

## 验收

1. Passive 页面显示时，Bannerlord 可以继续收到游戏输入。
2. Captured 页面打开后，HTML 控件可以正常点击/输入。
3. Release 后页面回到 Passive，而不是强制隐藏页面。
4. Hidden 后覆盖层消失。
5. `CloseOnEscape=false` 页面在 Captured 状态下，Escape 可以交给页面 JavaScript，而不是自动关闭 Page。
6. Bannerlord 最小化、切到其他窗口时覆盖层隐藏；重新回到 Bannerlord 时按原状态恢复。
