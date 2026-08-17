# BannerlordHtmlUI UI 开发流程

本文区分两种完全不同的开发任务：

1. **从零制作一个新的 UI 功能**。
2. **把已经存在的 Gauntlet UI 迁移成 HtmlUI**。

两者都使用 `State / Command / Request`，但设计起点不同。

---

## 一、从零制作新 UI：HtmlUI-first

这类功能没有旧 Gauntlet UI 可以照搬，因此不要先设计 Screen、GauntletLayer、Prefab，再想办法改成 HTML。

推荐直接按 HtmlUI 的结构设计。

### 1. 先定义业务目标

先写清楚：

```text
用户看到什么？
用户可以操作什么？
哪些数据需要实时刷新？
哪些操作需要返回结果？
页面有哪些子界面？
```

例如新技能配置器：

```text
目标选择
技能槽
技能目录
搜索
技能详情
应用 / 撤销
```

### 2. 先设计 C# Controller / Service

C# 负责：

```text
游戏 API
数据读取
规则验证
业务操作
状态保存
```

不要把游戏规则写进 JS。

推荐结构：

```text
MyFeatureController
├─ GetState()
├─ SelectTarget()
├─ SelectItem()
├─ Apply()
├─ Undo()
└─ Close()
```

### 3. 设计 State

一次定义页面需要的结构化状态：

```json
{
  "target": {},
  "items": [],
  "selectedItem": null,
  "dirty": false
}
```

通常一个页面使用一个主 State，再根据规模拆成少量子 State。

不要把每个文本框都设计成独立 Request。

### 4. 设计 Command / Request

规则：

```text
改变游戏状态      → Command
需要返回值        → Request
持续展示的数据    → State
```

例如：

```text
selectItem       Command
apply            Command
undo             Command
getPreview       Request
```

### 5. 设计 HTML 页面

推荐先做一个稳定的：

```text
index.html
```

多级菜单使用页面内部状态：

```text
main
catalog
detail
settings
```

而不是每一级都创建新的 HtmlUi Page。

### 6. 再拆 CSS / JS

推荐：

```text
MyUI/
├─ index.html
├─ css/
│  └─ feature.css
└─ js/
   ├─ app.js
   ├─ state.js
   ├─ commands.js
   ├─ navigation.js
   └─ views/
```

### 7. 接 Framework 生命周期

```text
Framework Ready
→ Register ContentRoot
→ Register Page
→ Register Command / Request
→ Open Page
→ Publish State
→ Close / Dispose
```

### 8. 最后做输入与焦点

确认：

```text
Captured input
pointerdown → capture
按钮 pointer-events:auto
ESC 子菜单返回
主页面 ESC 关闭
```

### 新功能开发的核心思想

```text
业务系统先设计
      ↓
State / Command / Request
      ↓
HTML View
```

没有必要为了“像 Bannerlord 原版”而强行创建 Gauntlet Screen。

---

## 二、已有 Gauntlet UI 迁移：Migration-first

已有 UI 的重点不是重新发明功能，而是把**原来的业务逻辑与视图脱钩**。

### 1. 先找 Screen

例如：

```text
CustomSkillScreen
```

确认：

```text
打开入口
关闭入口
生命周期
输入
```

### 2. 再追 DataSource / VM

例如：

```text
CustomSkillScreen
  ↓
CustomSkillScreenVM
  ↓
SkillCatalog / Data / Service
```

把原 VM 的内容分类：

```text
Property       → State
Command/Event  → Command
查询函数       → Request
Collection     → State list
```

### 3. 先做 HTML 并行版

第一阶段建议：

```text
旧 Gauntlet UI
        │
        ├── 原业务 VM
        │
        └── HtmlUI View
```

目的是确认：

```text
数据正确
交互正确
生命周期正确
```

### 4. 再脱离旧 Screen

当功能完整后：

```text
旧 Screen
   ↓ 删除 UI 宿主
HtmlUI Controller
   ↓
原 VM / Service / 业务层
```

这时 HTML 不应该再通过 `CustomSkillScreen` 获取 VM。

### 5. 最后删除旧 Gauntlet UI

只有完成：

```text
功能完整
输入完整
ESC 完整
生命周期完整
回归测试完整
```

才删除：

```text
Gauntlet Screen
Gauntlet Layer
XML / Prefab
```

---

## 三、两种流程的区别

| 项目 | 从零制作新 UI | 迁移已有 UI |
|---|---|---|
| 起点 | 业务需求 | 已有 Screen / VM |
| 是否需要 Gauntlet Screen | 不需要 | 初期可能需要 |
| 状态设计 | 先设计 | 从 VM 整理 |
| Command | 先设计 | 映射已有命令 |
| Request | 按需求设计 | 映射已有查询/异步操作 |
| HTML | 一开始就是正式 View | 先并行，后接管 |
| 旧 UI | 不存在 | 最后再删除 |
| 重构重点 | 业务与 UI 分层 | UI 与业务脱钩 |

---

## 四、推荐的最终架构

无论哪一种流程，完成以后都应尽量收敛到：

```text
                 ┌──────────────┐
                 │ Game / Mod   │
                 │ Business     │
                 └──────┬───────┘
                        ↓
               Controller / Service
                        ↓
               State / Command / Request
                        ↓
                 BannerlordHtmlUI
                        ↓
                  HTML / CSS / JS
```

HTML 是 View，不负责游戏规则。

---

## 五、何时应该新建多个 HTML Page

默认不要。

优先：

```text
一个 Page
+
多个 View
+
内部 navigation stack
```

只有在以下情况才考虑拆 Page：

```text
完全独立的功能
独立生命周期
需要不同 Owner
需要完全不同的资源上下文
```

技能配置、角色编辑器、复杂设置页等多级菜单，通常一个 HTML Page 就足够。
