# BannerlordHtmlUI

BannerlordHtmlUI 是面向 Mount & Blade II: Bannerlord Mod 的 WebView2 HTML UI Framework。

当前开发线：`dev`。稳定基线：`main`。

## 当前开发入口

| 文件 | 用途 |
|---|---|
| `ARCHITECTURE_MASTER.md` | 当前架构、唯一状态 Owner、代码归属硬规则 |
| `BUG_KNOWLEDGE_BASE.md` | 历史 Bug 的可复用结论与定位入口 |
| `API.md` | Framework Public API 与 Consumer 契约 |
| `DEVELOPMENT_GUIDE.md` | Framework/Consumer 分工、修改流程、最低回归 |
| `PROJECT_STATUS.md` | 当前状态、未验证项、风险和回归矩阵 |

辅助文档仍在 `Project/BannerlordHtmlUI/BannerlordHtmlUI/docs/`：Frontend Runtime、ChangeLog 等。

历史排错现场统一在 `Handoff/`，不作为当前规范入口。

## 强制阅读顺序

```text
ARCHITECTURE_MASTER
        ↓
BUG_KNOWLEDGE_BASE
        ↓
当前代码
        ↓
PROJECT_STATUS 对应回归项
```

## 工程规则

资源与 BUTR 工程布局以 `Project/BUTR_PROJECT_LAYOUT_RULES.md` 为准。

## 核心原则

- Framework 只提供通用 UI 基础设施；Consumer 保留游戏业务。
- Bannerlord GameThread、WebView2 UI thread、JS Runtime 必须明确区分。
- `HWND = 0` 不等于游戏退出。
- InputMode 只能由 `HtmlUiInputControllerPatch` 负责。
- WindowTracker 只负责窗口事实和 Bounds。
- PageManager 只负责 Page lifecycle。
- Browser policy 只由 Browser policy owner 负责。
- 禁止新增第二套独立的窗口同步/轮询机制覆盖既有状态 Owner。
- 禁止 Consumer 在 Framework 层增加专用 Patch。
- 未实机测试的功能不得标成已修复。
