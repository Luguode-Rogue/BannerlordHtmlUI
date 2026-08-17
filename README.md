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

## 2. 原来的 Bannerlord UI 制作流程 vs 现在的 HtmlUI 流程

这一节是使用本 Framework 时最重要的开发思维变化。

### 2.1 传统 Gauntlet UI 的典型制作流程

Bannerlord 原生 UI / Mod 传统 UI 一般是：

```text
C# Screen
   ↓
创建 DataSource / VM
   ↓
注册 Gauntlet Layer
   ↓
加载 XML / Prefab
   ↓
XML Widget Tree
   ↓
Widget ↔ VM Property Binding
   ↓
点击 Widget
   ↓
VM Command / Event
   ↓
C# 业务逻辑
```

开发时通常需要同时处理：

```text
Screen 生命周期
Gauntlet Layer
Prefab / XML
Widget
DataSource
VM Property
Binding
Event / Command
```

复杂界面还会继续出现：

```text
Screen
├─ Layer
├─ Widget
├─ 子 Widget
├─ ItemVM
├─ ListVM
└─ Template
```

因此一个界面的“视觉结构”和“业务状态”往往高度绑定在 Gauntlet 层级中。

### 2.2 HtmlUI 的典型制作流程

HtmlUI 推荐把“业务”和“视图”明确分开：

```text
C# Controller / VM / Service
          ↓
      State JSON
          ↓
       HTML UI
          ↓
用户点击 / 输入
          ↓
 Command / Request
          ↓
C# Controller / VM / Service
```

实际开发步骤变成：

```text
1. 找到原 UI 的业务入口
2. 找到 VM / Controller / 数据服务
3. 整理 UI 当前需要的状态
4. 将状态发布成结构化 State
5. 用 HTML / CSS 构建视觉界面
6. 用 Command 连接按钮和操作
7. 用 Request 处理需要返回值的操作
8. 实现输入、焦点和页面生命周期
9. 实机验证
10. 最后再考虑删除旧 Gauntlet UI
```

### 2.3 最关键的变化

传统方式的思路更接近：

```text
“我要制作一个 Bannerlord Widget。”
```

HtmlUI 的思路应该是：

```text
“我要给现有业务系统制作一个新的 Web View。”
```

因此迁移旧 UI 时，**首先研究的是原 UI 的业务状态和操作，而不是先照着 XML 一个 Widget 一个 Widget 地翻译。**

---

## 3. 从原 Gauntlet UI 迁移到 HtmlUI 的标准流程

### 第一步：找到原来的 Screen

例如：

```text
CustomSkillScreen
```

确认它负责什么：

```text
什么时候打开
什么时候关闭
当前页面是什么状态
输入来自哪里
```

### 第二步：找到 VM / Controller

继续追：

```text
CustomSkillScreen
    ↓
CustomSkillScreenVM
    ↓
SkillCatalog / Data / Service
```

重点记录：

```text
Property       → UI 显示状态
Command/Event  → UI 操作
Collection     → 列表
SelectedItem   → 当前选择
Async API      → Request
```

### 第三步：把 UI 所需状态整理成 State

例如技能界面：

```json
{
  "currentHero": {},
  "targetType": 0,
  "slots": [],
  "catalog": [],
  "proficiencies": [],
  "selectedSlot": 2,
  "dirty": true
}
```

### 第四步：HTML 负责 View

```text
State
 ↓
Renderer
 ↓
DOM
```

HTML 不应该重新实现技能规则。

### 第五步：Command 负责动作

例如：

```text
选择技能槽
选择技能
切换目标
应用
撤销
关闭
```

统一走：

```javascript
app.call("selectSlot", { index: 2 });
```

再由 C# 调用原 Controller / VM。

### 第六步：Request 负责查询和异步结果

例如：

```javascript
const result = await app.request("getPlayerInfo");
```

适合：

```text
查询数据
异步计算
需要明确返回值的操作
```

### 第七步：脱离旧 Screen

第一阶段可以：

```text
旧 Screen
   ↓
复用 VM / 业务逻辑
   ↓
HtmlUI
```

当 HTML 已经完整后，可以进一步变成：

```text
HtmlUI Controller
   ↓
VM / Service
   ↓
业务层
```

让旧 `Screen / GauntletLayer` 完全退出运行链。

**这也是复杂 UI 最终推荐的结构。**

---

## 4. 推荐的 Consumer 结构

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

## 5. 最小初始化流程

### 5.1 等待 Framework Ready

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

## 6. 注册 HTML 资源

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

## 7. 打开 / 关闭页面

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

## 8. C# → JavaScript：State

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

## 9. JavaScript → C#：Command

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

## 10. JavaScript → C#：Request

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

## 11. Request Cancellation

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

## 12. Command / Request / State 怎么选

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

## 13. 多级菜单不需要多个 HTML Page

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

## 14. JavaScript 也应该模块化

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

## 15. 输入与焦点

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

## 16. ESC / 页面关闭

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

## 17. 透明 Overlay

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

## 18. Framework 生命周期

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

## 19. Owner Scope

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

## 20. HTML 资源部署

推荐通过 `Directory.Build.targets` 自动复制：

```text
工程 UI
  ↓ Build
bin/Win64_Shipping_Client/<UiDirectory>/
```

例如：

```text
工程/MyMod/UI/Html/Inventory/
        ↓
Modules/MyMod/bin/Win64_Shipping_Client/InventoryUI/
```

如果没有自动部署，则必须手动复制。

最重要的原则：

> **不要依据源码目录、Solution 目录或普通 MSBuild `bin` 目录猜运行时资源位置。**

Framework 使用的是加载到游戏里的 DLL 路径，因此最终资源必须和实际运行时 DLL 布局匹配。

---

## 21. 常见问题排查

### 页面注册成功，但页面不存在

检查：

```text
ContentRoot 实际目录
index.html 是否存在
Assembly.Location
运行时资源目录
```

### 页面显示，但是按钮没有反应

检查：

```text
InputMode
WebView Focus
app.input.capture()
pointer-events
Command 是否注册
```

### HTML 一打开游戏画面变白

检查：

```text
Overlay transparency
WebView2 background
宿主窗口合成
```

### i18n 显示 Key 而不是翻译

先检查完整链路：

```text
HTML i18n
 ↓
window.game.request
 ↓
postMessage
 ↓
WebMessageReceived
 ↓
framework.i18n.translate
 ↓
Bannerlord Localization
```

不要一开始修改 Bannerlord 的 language XML；如果 Bannerlord Localization 本身已经能解析 Key，应优先检查 JS → Bridge 链。

### Request 越来越多

检查：

```text
Request Owner
Cancellation
Timeout
Page Close
Owner Dispose
```

不要单纯增加 Timeout。

---

## 22. 推荐的复杂 UI 架构

对于技能系统、角色编辑器、装备界面等复杂 UI，推荐：

```text
                   C#
                    │
            Controller / VM
                    │
             State / Command
                    │
             BannerlordHtmlUI
                    │
                 WebView2
                    │
        ┌───────────┴───────────┐
        │                       │
      HTML                     JS
        │                       │
      CSS              State / Navigation
```

如果界面有多级菜单：

```text
一个 index.html
        ↓
内部 View State
        ↓
Navigation Stack
        ↓
多个 JS View 模块
```

这样可以让一个复杂 UI 在不增加 Page 数量的情况下拥有：

```text
主界面
→ 子菜单
→ 列表
→ 详情
→ 编辑
→ 确认
```

---

## 23. 当前 Framework 实机验证范围

已经实际验证：

```text
Lifecycle Open / Close / Reopen
F6 Lifecycle Stress
ESC Close
F12 / F7 Close
State
Command
Request
Cancellable Request
Request timeout / abort 路径
Binding
Two-way Binding
List Binding
Template Binding
Dynamic DOM Binding
Binder Dispose
I18n
i18n.bind
HWND=0 稳定性保护
Page Reload 生命周期
HotReload 生命周期
透明 Overlay
```

当前明确跳过：

```text
Pagehide / Reload Binding 专项测试
Live Language Switch 专项测试
```

这些不应被误认为已经专项验收。

---

## 24. 开发时的核心原则

```text
1. Consumer 不直接创建 WebView2。
2. Framework Ready 之后再注册页面。
3. 业务逻辑留在 C#，HTML 负责 View。
4. 状态优先 State，操作优先 Command，查询优先 Request。
5. 复杂多级 UI 优先一个 Page + 内部 Navigation。
6. JS 可以、也应该拆成多个模块。
7. Owner 必须负责生命周期和 Request 清理。
8. 资源路径以运行时 DLL 的 Assembly.Location 为准。
9. 透明 Overlay 属于 Host / Framework 能力，不要让 Consumer 自己复制 WebView2 逻辑。
10. 迁移旧 Gauntlet UI 时先复用业务层，最后才删除旧 UI。
```