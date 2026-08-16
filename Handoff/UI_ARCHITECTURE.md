# HtmlUI UI 架构原则

## 1. 新旧 UI 的关系

BannerlordHtmlUI 支持两种迁移模式，必须明确区分，不能混用。

### A. 并行迁移型 UI

适用于旧 UI 仍需要继续使用、需要 A/B 对照或逐步迁移的功能。

```text
原有逻辑 / 数据
      ├────────→ 旧 Gauntlet UI
      │
      └────────→ HtmlUI
```

特点：

- 旧 UI 保留。
- HtmlUI 作为第二视图并行运行。
- 两者共享业务逻辑、数据和命令入口。
- 新 UI 出问题时，旧 UI 可以继续作为兜底。
- 典型案例：TacticalMap。

TacticalMap 当前采用此模式：旧地图仍显示在左上角，HtmlUI 地图显示在右上角。HtmlUI 不复制 TacticalMap 的地形、编队、指令、镜头等业务逻辑，而是使用原有 Controller / OrderSystem。

### B. HtmlUI 接管型 UI

适用于已经决定放弃旧 UI 的功能。

目标架构：

```text
业务层 / 状态层
        │
        └────────→ HtmlUI
```

旧 Gauntlet Screen 不是新的 UI 宿主，也不是必须创建的中间层。

原则：

- HtmlUI 是唯一正式前台 UI。
- 原 Gauntlet UI 不再显示。
- 不为了 HTML 而启动一个隐藏的旧 Screen。
- 不应该长期让 HtmlUI 通过反射读取旧 Screen/VM 才能工作。
- 可以复用旧 UI 中真正属于业务的数据结构、规则、Controller、Service、Command 和存档逻辑。
- 旧 UI 专用的 Screen、Gauntlet XML、Widget 和纯显示代码应逐步删除。
- 如果旧 Screen 中混有业务逻辑，应先把业务逻辑抽离到独立的 Controller/Service/State，再让 HtmlUI 直接使用。

典型案例：CustomSkill 技能选择界面。当前目标是让 `Shift+M` 直接进入 HtmlUI 技能界面；`CustomSkillScreen` 最终应退出 UI 链，只保留可复用的技能业务逻辑。

## 2. 不要把“新 UI”理解为“把旧 XML 翻译成 HTML”

正确迁移方法是：

```text
旧 UI
  ↓
识别业务逻辑 / 数据状态 / 用户操作
  ↓
抽离成独立的 Controller / Service / State
  ↓
HtmlUi Consumer
  ├─ C# → state
  └─ HTML → command / request
```

HTML 负责：

- 布局
- 视觉表现
- 鼠标/键盘交互
- 状态显示
- 页面切换

C# 负责：

- Bannerlord 游戏 API
- 业务规则
- 数据读写
- 原版/Mod 系统调用
- 权限和生命周期
- 持久化

## 3. 迁移阶段建议

对于复杂旧 UI，推荐：

```text
阶段 1：并行
旧 UI + HtmlUI

阶段 2：HtmlUI 正式入口
旧 UI 隐藏/禁用

阶段 3：抽离业务逻辑
HtmlUI 不再依赖旧 Screen/VM

阶段 4：删除旧 UI
Gauntlet Screen/XML/Widget 清理
```

不要在阶段 1 就删除旧 UI；也不要在阶段 3 以后继续让 HtmlUI 依赖一个仅为了提供 ViewModel 而创建的旧 Screen。

## 4. 输入与 Overlay

HtmlUI 是透明 Overlay 时，必须确保：

- 页面元素 `pointer-events` 正确。
- Captured 模式能够让 WebView2 获得焦点。
- 鼠标第一次按下时必要时重新 capture input。
- Passive 模式必须允许输入穿透到 Bannerlord。
- ESC、页面关闭和 Overlay 显隐生命周期必须继续使用 Framework 的统一机制。

## 5. 接入原则

每一个消费 Mod 的新 UI 都应该明确记录：

```text
UI 模式：Parallel / Takeover
旧 UI 是否保留：Yes / No
业务逻辑位置：...
HtmlUI Page：...
ContentRoot：...
输入模式：Passive / Captured
迁移状态：...
```

这样后续维护时，不会把“暂时并行”误认为“永久双 UI”，也不会把“HTML 接管”错误实现成“隐藏旧 Screen + HTML”。
