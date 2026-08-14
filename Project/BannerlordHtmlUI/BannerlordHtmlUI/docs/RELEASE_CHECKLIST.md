# BannerlordHtmlUI 发布验收清单

## 编译

- [ ] 使用目标 Bannerlord 开发环境成功编译 `BannerlordHtmlUI.dll`。
- [ ] `SubModule.xml` 与 DLL 名称一致。
- [ ] WebView2 Runtime 依赖已经确认。

## 启动

- [ ] Bannerlord 启动后 Module 无异常日志。
- [ ] WebView2 Host 成功初始化。
- [ ] Framework 测试页能够打开。

## Bridge

- [ ] `game.call()` 可以触发 C# Command。
- [ ] Command 异常能返回错误。
- [ ] `game.request()` 能获得 Response。
- [ ] Request 超时能正确失败。
- [ ] C# Event 能到达 JS。
- [ ] 初始 State Snapshot 能加载。
- [ ] State 增量事件能更新 JS 状态。

## 输入

- [ ] CaptureInput 后 HTML 页面获得键盘/窗口焦点。
- [ ] ReleaseInput 后 HTML 宿主隐藏。
- [ ] ReleaseInput 后 Bannerlord 主窗口恢复前台。
- [ ] Alt+Tab 到其他程序时 HTML 宿主隐藏。
- [ ] 返回 Bannerlord 后，仍处于显示状态的页面重新出现。
- [ ] 当前版本不把“ReleaseInput”误认为鼠标穿透；透明穿透需单独验收。

## 宿主窗口

- [ ] 窗口跟随逻辑不会让 HTML 宿主覆盖其他前台程序。
- [ ] Bannerlord 最小化时 HTML 宿主隐藏。
- [ ] 窗口化模式跟随 Bannerlord 主窗口。
- [ ] 无边框模式正常。
- [ ] 分辨率改变后位置/尺寸正确。
- [ ] Alt+Tab 后正确隐藏/恢复。
- [ ] 最小化后恢复正常。
- [ ] 独占全屏模式在目标机器上完成实机验证，或明确列入不支持范围。

## 生命周期

- [ ] 退出游戏时 WebView2 正常释放。
- [ ] 重载 SubModule 不留下后台 UI 线程。
- [ ] WebView2 初始化失败时不会让游戏崩溃。

## v0.12 输入验收

- [ ] `Show()` 显示 Passive UI 且不主动抢焦点
- [ ] `CaptureInput()` 进入 Captured 模式
- [ ] `ReleaseInput()` 回到 Passive 模式
- [ ] `Hide()` 完全隐藏并恢复 Bannerlord 前台
- [ ] Passive 模式的鼠标穿透在 Bannerlord 实机验证


## Framework dependency architecture
- [ ] Consumer Mod declares `BannerlordHtmlUI` as a dependency.
- [ ] Consumer Mod does not instantiate WebView2 or call `InitializeAsync`.
- [ ] Consumer Mod registers UI through `HtmlUiService.OnReady`.
- [ ] Only one BannerlordHtmlUI host exists in the process.
