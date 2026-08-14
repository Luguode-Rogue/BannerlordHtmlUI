# BannerlordHtmlUI 使用说明

这是给“使用 BannerlordHtmlUI 制作 HTML UI 的 Mod 作者”看的文档。

## 1. 你最终会怎么开发 UI

UI 使用普通网页文件：

```text
MyMod/
└── UI/
    └── Main/
        ├── index.html
        ├── style.css
        └── app.js
```

HTML/CSS/JS 不需要写 Bannerlord Gauntlet XML。

C# 负责 Bannerlord 游戏逻辑；网页负责显示、交互和布局。

基本通信模型：

```text
HTML / JS
   │
   ├── game.call()       → C# Command
   ├── game.request()    ⇄ C# Request/Response
   ├── game.on()         ← C# Event
   └── game.state        ← C# State
```

---

## 2. 安装 BannerlordHtmlUI

### 2.1 框架本身

把 `BannerlordHtmlUI` 模块安装到 Bannerlord：

```text
Mount & Blade II Bannerlord/
└── Modules/
    └── BannerlordHtmlUI/
```

模块目录至少包含：

```text
BannerlordHtmlUI/
├── SubModule.xml
├── BannerlordHtmlUI.dll
└── web/
```

### 2.2 WebView2 Runtime

运行机器需要可用的 Microsoft Edge WebView2 Runtime；本项目默认使用系统 Runtime。

如果目标机器没有 Runtime，需要在 Mod 发布说明中明确安装要求，或者后续改成 Fixed Version Runtime 部署。

---

## 3. 在你自己的 Mod 中使用

推荐把 BannerlordHtmlUI 当作一个独立 Framework/依赖模块，而不是把 Framework 源码复制进每个 Mod。

你的 Mod 结构可以是：

```text
MyMod/
├── ModuleData/
│   └── SubModule.xml
├── bin/
│   └── Win64_Shipping_Client/
│       └── MyMod.dll
└── UI/
    └── Main/
        ├── index.html
        ├── style.css
        └── app.js
```

你的 C# Mod 引用 BannerlordHtmlUI 程序集，然后在自己的初始化逻辑里使用 `HtmlUiService`。

### 3.1 等待 Framework 就绪

消费者 Mod 使用：

```csharp
HtmlUiService.OnReady(() =>
{
    var moduleDir = Path.GetDirectoryName(typeof(MySubModule).Assembly.Location);
    HtmlUiService.RegisterContentRoot("MyMod", Path.Combine(moduleDir, "UI"));
});
```

消费者 Mod 不应调用 `InitializeAsync()`。Framework 模块自己负责创建唯一的 WebView2 Host。

---

## 4. 注册 HTML 页面

例如：

```csharp
HtmlUiService.Pages.Register(
    new HtmlUiPage("main", "Main/index.html")
    {
        ContentRootId = "MyMod",
        HotReload = true
    });
```

打开：

```csharp
HtmlUiService.Pages.Open("main");
```

关闭：

```csharp
HtmlUiService.Pages.Close("main");
```

关闭当前页面：

```csharp
HtmlUiService.Pages.CloseCurrent();
```

查看当前页面：

```csharp
var current = HtmlUiService.Pages.CurrentId;
```

---

## 5. HTML 页面怎么写

页面使用普通 HTML。

例如：

```html
<!doctype html>
<html>
<head>
    <meta charset="utf-8">
    <link rel="stylesheet" href="style.css">
</head>
<body>
    <button id="hello">发送给 C#</button>
    <div id="result"></div>

    <script src="app.js"></script>
</body>
</html>
```

注意：Framework 会自动注入 `runtime.js`，页面无需手动引用；它提供 `window.game` API。

---

# 6. JS → C#：Command

Command 适合“告诉游戏做某件事”，不需要等待返回值。

JS：

```javascript
game.call("myCommand", {
    value: 123
});
```

C#：

```csharp
HtmlUiService.RegisterCommand("myCommand", payload =>
{
    var value = payload.GetProperty("value").GetInt32();

    // 在这里执行 Bannerlord 游戏逻辑。
});
```

重要：Command 最终会通过 `GameThreadDispatcher` 排到 Bannerlord 的 `OnApplicationTick` 中执行。不要从 WebView2 回调线程直接操作 Bannerlord 游戏对象。

---

# 7. JS ⇄ C#：Request / Response

Request 适合需要返回值的操作。

JS：

```javascript
const result = await game.request("getPlayerInfo", {});
console.log(result);
```

C#：

```csharp
HtmlUiService.RegisterRequest("getPlayerInfo", payload =>
{
    return Task.FromResult<object>(new
    {
        name = "Player",
        value = 123
    });
});
```

返回的数据会被 JSON 序列化后交给 JS。

调用默认有超时保护：

```javascript
game.request("getPlayerInfo", {}, 5000);
```

超时会抛出 JS `Error`。

---

# 8. C# → JS：Event

当前 Host 提供事件通道，可通过 C# 发送事件给当前页面。

JS：

```javascript
game.on("myEvent", payload => {
    console.log(payload);
});
```

事件名称建议使用稳定、明确的命名，例如：

```text
player.changed
mission.started
settings.updated
inventory.changed
```

不要把 Bannerlord 类型名直接当作协议名称。网页协议应保持独立于游戏内部 API。

---

# 9. C# → JS：State

State 用于持续存在的 UI 状态。

C#：

```csharp
HtmlUiService.State.Set("player.gold", 5000);
HtmlUiService.State.Set("player.name", "Test");
```

JS：

```javascript
const gold = game.state.get("player.gold");
```

订阅变化：

```javascript
game.state.subscribe("player.gold", value => {
    document.querySelector("#gold").textContent = value;
});
```

读取当前状态快照：

```javascript
const snapshot = game.state.snapshot();
```

State key 建议使用点号命名：

```text
player.gold
player.name
mission.id
mission.isRunning
settings.showMinimap
```

---

# 10. 输入控制

默认情况下，HTML UI 不应该长期抢占游戏输入。

需要交互时：

```csharp
HtmlUiService.CaptureInput();
```

退出 UI 交互模式：

```csharp
HtmlUiService.ReleaseInput();
```

典型流程：

```text
打开 UI
    ↓
显示页面
    ↓
CaptureInput
    ↓
玩家操作 HTML
    ↓
关闭页面
    ↓
ReleaseInput
```

当前实现属于框架基础版本；输入与 Bannerlord 原生输入层的更细粒度兼容仍需要实机验证。

---

# 11. DevTools

开发阶段可以打开 Chromium DevTools：

```csharp
HtmlUiService.OpenDevTools();
```

然后可以像普通网页一样检查：

```text
Elements
Console
Network
Sources
Performance
```

注意：DevTools 只应该在开发版本启用。

---

# 12. 热重载

页面注册时：

```csharp
HtmlUiService.Pages.Register(
    new HtmlUiPage("main", "Main/index.html")
    {
        ContentRootId = "MyMod",
        HotReload = true
    });
```

宿主也需要开启：

```csharp
HtmlUiService.Host.HotReloadEnabled = true;
```

这样修改 `html/css/js` 文件后，页面可以自动刷新。

当前热重载通过 `FileSystemWatcher` 实现，因此连续写文件时可能触发多次刷新；生产版本建议关闭。

---

# 13. 页面生命周期

`HtmlUiPage` 可以挂接简单生命周期回调：

```csharp
var page = new HtmlUiPage("main", "Main/index.html")
{
    Opened = () =>
    {
        // 页面打开
    },
    Closed = () =>
    {
        // 页面关闭
    }
};

HtmlUiService.Pages.Register(page);
```

页面切换流程：

```text
Open(A)
 ↓
A.Opened
 ↓
Open(B)
 ↓
A.Closed
 ↓
B.Opened
```

---

# 14. 建议的 Mod 页面结构

大型 Mod 推荐：

```text
MyMod/
└── UI/
    ├── Main/
    │   ├── index.html
    │   ├── style.css
    │   └── app.js
    │
    ├── Settings/
    │   ├── index.html
    │   ├── style.css
    │   └── app.js
    │
    └── Debug/
        ├── index.html
        ├── style.css
        └── app.js
```

不要把 C# 逻辑塞进 JS。

推荐：

```text
HTML/CSS/JS
    UI、交互、动画、布局

C#
    Bannerlord API、游戏状态、存档、Mission、Agent、Hero 等

Bridge
    两者之间的协议
```

---

# 15. 一个完整的最小例子

C#：

```csharp
HtmlUiService.Pages.Register(
    new HtmlUiPage("hello", "Hello/index.html")
    {
        HotReload = true
    });

HtmlUiService.RegisterRequest("getGreeting", payload =>
{
    return Task.FromResult<object>(new
    {
        text = "Hello from Bannerlord"
    });
});

HtmlUiService.RegisterCommand("setValue", payload =>
{
    var value = payload.GetProperty("value").GetInt32();

    // 修改你的游戏数据。

    HtmlUiService.State.Set("example.value", value);
});

HtmlUiService.Pages.Open("hello");
HtmlUiService.CaptureInput();
```

HTML：

```html
<button id="load">读取</button>
<input id="value" type="range" min="0" max="100" value="50">
<div id="output"></div>

<script>
document.querySelector('#load').addEventListener('click', async () => {
    const result = await game.request('getGreeting');
    document.querySelector('#output').textContent = result.text;
});

document.querySelector('#value').addEventListener('input', event => {
    game.call('setValue', {
        value: Number(event.target.value)
    });
});

game.state.subscribe('example.value', value => {
    document.querySelector('#output').textContent = `Value: ${value}`;
});
</script>
```

这就是本框架最核心的开发模式：**网页负责 UI，C# 负责游戏，Bridge 负责通信。**

---

# 16. 当前版本边界

当前框架已经具备：

- HTML/CSS/JS 页面
- WebView2 宿主
- Bannerlord SubModule 接入
- Command
- Request/Response
- Event
- State Binding
- 页面注册/打开/关闭
- 基础输入捕获
- DevTools
- 热重载
- 日志
- JS 运行时错误转发
- Bannerlord 游戏线程调度

仍需要实机重点验证：

- 不同 Bannerlord 图形模式
- 全屏 / 无边框窗口 / 窗口化
- Alt+Tab
- 游戏窗口移动与缩放
- 游戏输入与 WebView2 输入的精确冲突处理
- Bannerlord 不同版本的程序集兼容
- WebView2 Runtime 缺失/升级/损坏时的行为

这些属于 Framework 的实机兼容性阶段，不属于 HTML API 本身。

## 10. 输入模式和窗口行为

BannerlordHtmlUI 当前使用一个独立的 WebView2 窗口作为 HTML UI 宿主。框架会每 100ms 检查 Bannerlord 主窗口状态，并在 Bannerlord 处于前台且未最小化时让宿主窗口跟随主窗口位置和尺寸。

推荐的交互流程是：

```csharp
HtmlUiService.Pages.Open("main");
HtmlUiService.CaptureInput();
```

用户完成 UI 操作后：

```csharp
HtmlUiService.ReleaseInput();
```

当前 `ReleaseInput()` 的语义是：**结束 HTML UI 交互模式，同时隐藏宿主窗口，并尝试将 Bannerlord 主窗口置回前台**。这不是“保持网页可见但鼠标完全穿透网页”。真正的透明鼠标穿透将在后续输入层版本中单独实现。

窗口行为：

- Bannerlord 最小化或不在前台时，HTML 宿主会暂时隐藏。
- Bannerlord 恢复并重新成为前台窗口后，若页面仍被请求显示，宿主会重新出现。
- 当前架构主要面向窗口化/无边框窗口模式。独占全屏是否正常覆盖必须在目标机器实机验收。
- Alt+Tab 时不会把 HTML 宿主留在其他应用上方。

