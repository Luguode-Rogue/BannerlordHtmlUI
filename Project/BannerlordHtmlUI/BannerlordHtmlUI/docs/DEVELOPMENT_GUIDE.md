# Framework 开发指南

## 1. Framework 与 Consumer 分工

Framework：

- WebView2 Host
- Overlay
- Page 生命周期
- Bridge
- State
- Input
- Diagnostics
- Runtime / i18n / Binding
- 线程边界与故障恢复

Consumer：

- Bannerlord 业务逻辑
- Controller / VM / Service
- HTML/CSS/JS 资源
- Page UI
- 游戏数据转换
- Command / Request handler

Consumer 不复制 Framework 内部 WebView2 逻辑。

## 2. 新 UI 的标准接入流程

```text
找到业务入口
→ 找到 VM/Controller/Service
→ 定义 UI State
→ 创建 ConsumerScope
→ 注册 ContentRoot
→ 注册 Page
→ 注册 Command/Request/Event
→ 页面打开
→ 实现 State/Binding
→ 实现 Input / ESC / Close
→ 实机验证
```

迁移旧 Gauntlet UI 时，应先复用业务层，再逐步替换 View；不要把旧 XML 的 Widget Tree 原样翻译成一套重复业务逻辑。

## 3. ContentRoot

运行时资源路径以实际加载 DLL 的 `Assembly.Location` 为准。

```csharp
var assemblyDir = Path.GetDirectoryName(typeof(MyUi).Assembly.Location) ?? ".";
var uiRoot = Path.Combine(assemblyDir, "MyPageUI");
_scope.RegisterContentRoot("myui", uiRoot);
```

Page ID、ContentRoot ID、Windows 实际目录是三个不同概念，不能混用。

## 4. API 选择

```text
State   → 当前状态
Command → 执行动作，无需结果
Request → 执行动作并返回结果
Event   → 广播运行时事件
```

大量 UI 数据优先结构化 State，不要为每个文本字段增加 Request。

## 5. Owner Scope

一个 Consumer 使用一个明确的 `HtmlUiConsumerScope`。页面关闭、Consumer shutdown、Framework shutdown 时，由 Owner 统一释放自己的资源。

不要在 Dispose 时按名称删除“当前同名资源”；必须由 Owner/entry identity 保护。

## 6. 异步规则

Request handler 初始调用在 Bannerlord game thread，但 `await` 之后不保证仍在 game thread。

因此：

- Bannerlord Game API：显式回 GameThread
- CoreWebView2：回 WebView2 UI thread
- 纯数据计算：可留在异步线程
- Response/PostMessage：由 Framework 负责 UI thread 调度

## 7. 输入

交互页面使用 Captured Input。出现页面能看但无法点击时，依次检查：

```text
InputMode
→ WebView Focus
→ app.input.capture()
→ pointer-events
→ Overlay 层级
→ Command 注册
```

ESC 主页面关闭，子菜单优先返回上一级。

## 8. 日志

正常模式：低噪声。

应记录：

- Framework Ready/Shutdown
- Page 注册/Open/Close 的关键转换
- Navigation failure
- Runtime error
- WebView2 ProcessFailed
- ESC close 诊断

不要恢复逐帧 Window Tracking DEBUG。

## 9. 修改后的最低验证

修改 WebView2/Overlay/Input/Page lifecycle：

```text
Framework rebuild
→ Consumer rebuild
→ F11 Open
→ 页面完整显示
→ ESC Close
→ 再次 F11 Open
```

修改 Bridge/Request/State：

```text
Command
Request
Cancellation
State set/remove
Owner Dispose
```

修改 Binding/i18n：

```text
State hydration
DOM bind
locale change
pagehide/dispose
长期运行
```

## 10. 不允许重复踩的规则

- 不把 `HWND=0` 当成窗口关闭。
- 不把 F12 当作关闭协议。
- 不直接从 Consumer 创建 WebView2。
- 不在错误线程访问 CoreWebView2。
- 不以 JSON/JS 对象引用相等代替需要的内容比较。
- 不用 object spread 替换带 prototype/非枚举成员的 Component。
- 不用随机 Win32 style 实验解决 Overlay 渲染问题。
- 不为解决 C# 10 语法问题提高 LangVersion。
