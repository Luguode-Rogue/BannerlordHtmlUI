# BannerlordHtmlUI

BannerlordHtmlUI 是一个面向 **Mount & Blade II: Bannerlord Mod** 的 WebView2 HTML UI Framework。

它的目标不是提供一套固定的视觉主题，而是把 Bannerlord Mod 的 UI 开发方式变成：

```text
C# Mod
  ↓
BannerlordHtmlUI Framework
  ↓
WebView2
  ↓
HTML / CSS / JavaScript
```

Mod 可以继续使用自己的 C# 业务逻辑、VM、数据模型和 Harmony；HTML 负责显示和交互。

当前工程位于 `Project/`：

```text
Project/
├─ BannerlordHtmlUI/          # Framework
└─ HtmlUiConsumerTestMod/     # Framework Consumer / 实机测试 Mod
```

---

## 1. Framework 适合做什么

推荐把 Bannerlord UI 拆成两层：

```text
业务层
├─ 技能规则
├─ 数据模型
├─ 存档
├─ 游戏 API
└─ Harmony Patch

UI 层
├─ HTML
├─ CSS
└─ JavaScript
```

例如一个已有的 Gauntlet UI 可以逐步改造成：

```text
原来的：
CustomScreen
  ↓
Gauntlet XML

HtmlUI 版本：
HtmlUiConsumer
  ↓
State
  ↓
HTML / JS
```

**不要复制业务逻辑。** 最理想的方式是继续复用已有的 Controller / VM / 数据服务，让 HtmlUI 只是新的 View。

---

## 2. 推荐的 Consumer 结构

一个使用 Framework 的 Mod 推荐组织成：

```text
MyMod/
├─ SubModule.cs
├─ UI/
│  ├─ MyHtmlUi.cs
│  ├─ MyHtmlUiCommands.cs        # 可选
│  └─ Html/
│     └─ MyPage/
│        └─ index.html
└─ Directory.Build.targets       # 可选：自动部署 HTML 资源
```

运行时最终布局必须满足 Bannerlord Mod 的实际 DLL 布局：

```text
Modules/MyMod/
├─ SubModule.xml
├─ ModuleData/
└─ bin/Win64_Shipping_Client/
   ├─ MyMod.dll
   └─ MyPageUI/
      └─ index.html
```

Framework / Consumer 的资源路径应以**运行时实际加载 DLL 的 `Assembly.Location`** 为准，而不是猜普通工程 `bin` 目录。

---

## 3. 最小初始化流程

### 3.1 等待 Framework Ready

Consumer 不应该自己创建 WebView2。

推荐：

```csharp
using BannerlordHtmlUI;

public sealed class MyHtmlUi
{
    private bool _registered;
    private HtmlUiConsumerScope _scope;
    private string _pageId;

    public void InitializeOnFrameworkReady()
    {
        HtmlUiService.OnReady(Register);
    }

    private void Register()
    {
        if (_registered || !HtmlUiService.IsReady)
            return;

        // 注册 ContentRoot / Page / Command / Request
        // ...

        _registered = true;
    }
}
```

Framework 不 Ready 时不要提前访问 WebView2。

---

## 4. 注册 HTML 资源

先确定运行时目录：

```csharp
string assemblyDir = Path.GetDirectoryName(
    typeof(MyHtmlUi).Assembly.Location) ?? ".";

string uiRoot = Path.Combine(assemblyDir, "MyPageUI");
```

然后注册 ContentRoot：

```csharp
_scope = HtmlUiService.CreateScope("MyMod.MyUi");
_scope.RegisterContentRoot("myui", uiRoot);
```

注册 Page：

```csharp
_pageId = _scope.RegisterPage(
    new HtmlUiPage("myui.html", "index.html")
    {
        ContentRootId = "myui",
        HotReload = true,
        DefaultInputMode = HtmlUiInputMode.Captured
    });
```

这里有三个不同概念：

```text
MyPageUI          = Windows 实际目录
myui              = Framework ContentRoot 逻辑 ID
myui.html         = Framework Page ID
```

不要混淆。

---

## 5. 打开 / 关闭页面

打开：

```csharp
HtmlUiService.Pages.Open(_pageId);
```

关闭：

```csharp
HtmlUiService.Pages.Close(_pageId);
```

页面生命周期由 Framework 统一管理。Consumer 不应该直接操作 WebView2 Navigate / Dispose。

---

## 6. C# → JavaScript：State

State 适合表示**当前 UI 状态**。

例如：

```csharp
HtmlUiService.State.Set("skills", new
{
    selectedHero = "main_hero",
    activeSlot = 2,
    dirty = true
});
```

JS：

```javascript
app.state.subscribe("skills", state => {
    render(state);
});
```

推荐：

```text
游戏数据变化
    ↓
C# Controller / VM
    ↓
State.Set()
    ↓
HTML 自动刷新
```

不要为了每一个文本字段都注册一个独立 Request。

对于列表、状态面板、技能槽、角色信息等，优先使用一个结构化 State。

---

## 7. JavaScript → C#：Command

Command 适合**执行操作，不需要等待返回值**的场景。

C#：

```csharp
_scope.RegisterCommand("selectSlot", payload =>
{
    int index = payload?["index"]?.ToObject<int>() ?? -1;
    controller.SelectSlot(index);
});
```

JS：

```javascript
app.call("selectSlot", { index: 2 });
```

典型用途：

```text
按钮点击
技能槽选择
切换选项
打开/关闭子界面
应用设置
撤销
```

Command 不适合需要明确异步结果的操作。

---

## 8. JavaScript → C#：Request

Request 适合需要返回结果的操作。

C#：

```csharp
_scope.RegisterRequest("getPlayerInfo", payload =>
{
    return Task.FromResult<object>(new
    {
        name = Hero.MainHero?.Name?.ToString(),
        level = 10
    });
});
```

JS：

```javascript
const result = await app.request("getPlayerInfo");
console.log(result);
```

Request 支持 Framework 的生命周期管理、取消、超时以及 Owner Dispose 清理。

---

## 9. Request Cancellation

对于可能持续较长时间的 Request，可以使用可取消 Request。

推荐思路：

```text
Owner Dispose
    ↓
CancelRequestsByOwner()
    ↓
CancellationToken
    ↓
Request handler 停止
```

HTML 页面关闭、Consumer 卸载、Owner 销毁后，不应继续留下活跃 Request。

不要自己维护一套与 Framework 冲突的 Request 生命周期。

---

## 10. Command / Request / State 怎么选

可以按下面规则快速判断：

| 需求 | 推荐机制 |
|---|---|
| 更新 UI 状态 | State |
| 点击按钮执行操作 | Command |
| 请求数据并等待结果 | Request |
| 大量列表/面板数据 | State |
| 异步数据查询 | Request |
| 页面关闭后的清理 | Owner Dispose / 生命周期 |

一句话：

```text
State = UI 现在是什么状态
Command = 请执行这个动作
Request = 请执行这个动作并把结果给我
```

---

## 11. 多级菜单不需要多个 HTML Page

一个复杂的 Mod UI 完全可以只有一个：

```text
MyPageUI/
└─ index.html
```

然后在 JS 内部管理 View：

```javascript
const uiState = {
    view: "main",
    subView: null,
    selectedTarget: null,
    selectedSlot: null
};
```

例如：

```text
主界面
  ↓
技能槽
  ↓
技能目录
  ↓
技能详情
```

整个过程中仍然只有一个 WebView / 一个 Page。

推荐多级 UI 使用：

```text
一个 HTML Page
+
多个 JS 模块
+
内部 navigation stack
```

而不是让每一级菜单都重新 Navigate。

这样可以减少 Navigation / Runtime / Input 生命周期切换。

---

## 12. JavaScript 也应该模块化

“一个 HTML”不等于“所有 JS 都塞进一个 `<script>`”。

推荐：

```text
MyPageUI/
├─ index.html
├─ css/
│  └─ page.css
└─ js/
   ├─ app.js
   ├─ state.js
   ├─ navigation.js
   ├─ commands.js
   └─ views/
      ├─ main.js
      ├─ catalog.js
      └─ detail.js
```

对于大型 UI，这比增加多个 Page 更容易维护。

---

## 13. 输入与焦点

Bannerlord HtmlUI 是 Overlay UI，鼠标和键盘焦点非常重要。

当页面需要交互时：

```javascript
function ensureInput() {
    try {
        window.focus?.();
        app.input?.capture?.();
    } catch (_) {}
}

document.addEventListener("pointerdown", ensureInput, true);
```

页面应该使用：

```text
DefaultInputMode = Captured
```

按钮和可点击元素应明确允许鼠标事件：

```css
pointer-events: auto;
```

如果出现：

```text
页面能看到
但按钮完全点不到
```

优先检查：

```text
1. InputMode
2. WebView Focus
3. app.input.capture()
4. pointer-events
5. Overlay 层是否挡住了页面
```

不要一开始就怀疑 Command 注册。

---

## 14. ESC / 页面关闭

Framework 已提供 WebView2 AcceleratorKeyPressed / ESC 关闭路径。

页面内部也可以处理多级菜单：

```javascript
window.addEventListener("keydown", e => {
    if (e.key === "Escape") {
        if (navigation.canGoBack()) {
            navigation.back();
        } else {
            app.call("close");
        }
        e.preventDefault();
    }
});
```

推荐：

```text
子菜单 → ESC 返回上一级
主页面 → ESC 关闭 HtmlUI
```

不要让每个页面自己处理 WebView2 的销毁。

---

## 15. 透明 Overlay

普通 UI 可以使用不透明 WebView。

如果 UI 需要覆盖在 Bannerlord 游戏画面之上，例如：

```text
战术地图
HUD
技能面板
浮动窗口
```

则可以使用透明 Overlay。

核心概念：

```text
HTML transparent background
        ↓
WebView2 transparent background
        ↓
Overlay host transparent composition
        ↓
看到 Bannerlord 游戏画面
```

Consumer **不要自己直接引用 WebView2 类型**来实现这件事。透明 Overlay 属于 Framework / Host 能力；Consumer 通过 Framework 提供的方式启用。

当前 TacticalMap 已经实际验证过透明 Overlay。

---

## 16. 如何把现有 Gauntlet UI 改成 HtmlUI

推荐按下面顺序做。

### 第一步：找原 UI 的 Screen

例如：

```text
MyScreen
```

### 第二步：找它背后的 VM / Controller

找到：

```text
MyScreenVM
MyItemVM
MyData
```

先理解：

```text
哪些是状态
哪些是命令
哪些是异步操作
```

### 第三步：不要复制业务逻辑

例如原来：

```csharp
vm.SelectSkill(index);
vm.Apply();
vm.Undo();
```

HTML 应该调用同一套逻辑：

```text
HTML button
    ↓
Command
    ↓
原 Controller / VM
```

而不是把 `SelectSkill()` 再写一遍 JavaScript。

### 第四步：把 VM 状态转成 JSON State

例如：

```json
{
  "hero": {},
  "slots": [],
  "catalog": [],
  "selectedSlot": 2,
  "dirty": true
}
```

### 第五步：HTML 负责显示

```text
State
 ↓
Renderer
 ↓
DOM
```

### 第六步：用 Command / Request 处理交互

```text
点击
 ↓
app.call()
 ↓
C# Controller
 ↓
State 更新
 ↓
HTML 刷新
```

### 第七步：最后才考虑删除旧 Gauntlet UI

推荐先：

```text
HTML UI
    ↓
实机验证
    ↓
功能完整
    ↓
输入完整
    ↓
生命周期完整
    ↓
再删除旧 UI
```

不要一开始就把旧系统删掉。

---

## 17. Framework 生命周期

Consumer 需要理解的生命周期是：

```text
Framework Startup
    ↓
WebView2 Initialization
    ↓
HtmlUiService Ready
    ↓
Consumer Register
    ↓
Page Open
    ↓
NavigationCompleted
    ↓
Runtime Ready
    ↓
State / Command / Request 工作
    ↓
Page Close
    ↓
Owner Dispose / Request Cancel
```

Reload / HotReload 会重新经历页面 Runtime 的建立过程，因此不要在 JS 中永久假设某个 Runtime 对象永远不会重建。

---

## 18. Owner Scope

每个 Consumer 推荐拥有自己的：

```csharp
HtmlUiConsumerScope
```

例如：

```text
New_ZZZF.TacticalMap
New_ZZZF.CustomSkill
MyMod.Inventory
MyMod.CharacterEditor
```

Owner 的作用是把：

```text
Page
ContentRoot
Command
Request
State
生命周期清理
```

隔离开。

这样一个 Mod 被卸载或页面关闭时，不会影响其他 Consumer。

---

## 19. HTML 资源部署

### 推荐：自动复制

Consumer 可以在 `Directory.Build.targets` 中定义：

```xml
<PropertyGroup>
  <MyUiSource>$(MSBuildProjectDirectory)\UI\Html</MyUiSource>
  <MyUiDeploy>$(TargetDir)MyUI</MyUiDeploy>
</PropertyGroup>

<Target Name="DeployMyHtmlUi" AfterTargets="Build">
  <MakeDir Directories="$(MyUiDeploy)" />
  <ItemGroup>
    <_HtmlFiles Include="$(MyUiSource)\**\*" />
  </ItemGroup>
  <Copy
    SourceFiles="@(_HtmlFiles)"
    DestinationFiles="@(_HtmlFiles->'$(MyUiDeploy)\%(RecursiveDir)%(Filename)%(Extension)')"
    SkipUnchangedFiles="true" />
</Target>
```

### 验证

编译以后确认：

```text
Modules/MyMod/bin/Win64_Shipping_Client/MyUI/index.html
```

存在。

如果目录不存在，优先检查资源部署，而不是检查 Bridge。

---

## 20. 调试顺序

出现“页面不工作”时，按这个顺序检查：

```text
① Framework 是否 Ready
② Consumer Register 是否执行
③ ContentRoot 是否存在
④ Page 是否注册
⑤ Pages.Open 是否成功
⑥ NavigationCompleted 是否成功
⑦ Runtime 是否建立
⑧ JS app 是否初始化
⑨ app.call / app.request 是否发出
⑩ C# Command / Request 是否命中
⑪ State 是否回到 JS
⑫ Input / Focus 是否正常
```

不要跳过前面的层直接修改业务代码。

---

## 21. 常见错误

### DirectoryNotFoundException

例如：

```text
...\bin\Win64_Shipping_Client\MyUI
```

不存在。

通常是资源没有部署到 `Assembly.Location` 对应目录。

### 页面出现但鼠标不能点

优先检查：

```text
InputMode
Focus
app.input.capture()
pointer-events
Overlay 层
```

### Request timeout

优先判断：

```text
JS request 是否真的 postMessage
WebMessageReceived 是否命中
Request name 是否注册
页面是否正在 Reload
```

不要首先修改业务 Handler。

### Localization 显示 Key 而不是翻译

先确认：

```text
Bannerlord Localization 本身是否解析正确
```

如果 C# `Translate()` 已经得到正确文本，再检查：

```text
JS i18n request
Bridge
State / DOM binding
```

### WebView2 ProcessFailed

Framework 会进行 Recovery；Consumer 不应该自行创建第二套 WebView2。

---

## 22. 当前已实机验证的能力

当前 Framework 已在 Bannerlord Consumer TestMod 上验证过：

```text
✅ 页面注册 / 打开 / 关闭
✅ WebView2 Runtime
✅ Localization
✅ State
✅ Command
✅ Request
✅ Request cancellation
✅ Owner Dispose
✅ Page Reload / HotReload 基础生命周期
✅ ESC 关闭
✅ Input Capture
✅ Binding
✅ Two-way Binding
✅ i18n.bind
✅ Dynamic DOM declarative binding
✅ List binding
✅ Template binding
✅ Binder Dispose
✅ 高频 State/Event
✅ 500 项 Binding pressure
✅ 50-round StressLab
✅ 20-round Lifecycle Stress
✅ Request active count 稳定性测试
✅ HWND=0 tracking guard
```

已经明确跳过的项目：

```text
⏭ Pagehide / Reload Binding 专项测试
⏭ Live Language Switch 专项测试
```

这两个不是当前 Framework 验收阻塞项。

---

## 23. 推荐的实际开发模式

对于一个新 Mod UI，推荐直接从下面的模板开始：

```text
1. 创建 ConsumerScope
2. 创建 ContentRoot
3. 注册一个 Page
4. 创建一个 index.html
5. 用 State 表示完整 UI 状态
6. 用 Command 处理按钮/交互
7. 用 Request 处理需要返回值的调用
8. HTML 内部用 View State 做多级菜单
9. JS 拆模块，HTML 页面数量尽量少
10. 通过 Directory.Build.targets 自动部署
11. 完成实机生命周期/输入测试
12. 再逐步移除旧 Gauntlet UI
```

最终推荐架构：

```text
Bannerlord Mod
│
├─ Game / Business Logic
│
├─ Controller / VM
│     │
│     └──── HtmlUi Consumer
│              │
│              ├─ State
│              ├─ Command
│              └─ Request
│                    │
│                    ▼
│              WebView2 Runtime
│                    │
│                    ▼
│              index.html
│              ├─ CSS
│              └─ JS modules
│
└─ BannerlordHtmlUI Framework
```

这个结构可以让一个复杂的 Bannerlord Mod UI 在不重写核心游戏逻辑的情况下，从 Gauntlet 逐步迁移到 HTML UI。

---

## 24. 当前工程文档

更详细的 Framework 生命周期、交接和测试资料位于：

```text
Handoff/
Project/
```

Consumer 的实机测试项目位于：

```text
Project/HtmlUiConsumerTestMod/
```

TacticalMap / CustomSkill 等外部 Consumer 示例应作为“真实使用案例”参考，而不是 Framework 本体的强制实现方式。
