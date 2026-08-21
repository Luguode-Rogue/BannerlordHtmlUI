# BannerlordHtmlUI 文档中心

> 当前开发线：`dev`。版本事实以 `BannerlordHtmlUI.csproj` 为准。

日常只维护以下长期入口：

| 文档 | 用途 |
|---|---|
| `ARCHITECTURE_MASTER.md` | 当前架构、模块职责、代码归属规则、线程/输入/窗口边界 |
| `API.md` | Public API、协议语义、Consumer 接入契约 |
| `DEVELOPMENT_GUIDE.md` | 开发流程、Framework/Consumer 分工、修改规则 |
| `FRONTEND_GUIDE.md` | Runtime / State / Binding / i18n / Component |
| `BUG_KNOWLEDGE_BASE.md` | 已知 Bug、失败方案、根因、快速定位 |
| `PROJECT_STATUS.md` | 当前进度、未验收项、回归矩阵、风险 |
| `CHANGELOG.md` | 历史版本索引；`CHANGELOG_v*.md` 继续保留 |

## 查问题

```text
当前状态/验收       → PROJECT_STATUS.md
代码应该写哪里      → ARCHITECTURE_MASTER.md
API 怎么用           → API.md
新功能怎么接入      → DEVELOPMENT_GUIDE.md
Runtime 前端问题    → FRONTEND_GUIDE.md
历史 Bug / 失败方案 → BUG_KNOWLEDGE_BASE.md
历史版本            → CHANGELOG.md / Handoff
```

## Framework 修改强制顺序

```text
ARCHITECTURE_MASTER
        ↓
BUG_KNOWLEDGE_BASE
        ↓
当前代码
        ↓
对应回归项
```

找不到明确 owner 时，先拆职责，不得把逻辑塞进“最方便修改”的文件。

## 文档治理

当前规范只维护上述长期入口；历史 Handoff、Postmortem、Changelog 不删除、不改写为当前规范。

资源路径仍以 `../../../../BUTR_PROJECT_LAYOUT_RULES.md` 为统一工程规则来源。
