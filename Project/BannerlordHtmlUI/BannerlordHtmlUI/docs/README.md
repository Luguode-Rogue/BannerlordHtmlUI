# BannerlordHtmlUI 文档中心

> 当前工作分支：`dev`
> 当前 Framework 版本：以 `BannerlordHtmlUI.csproj` 为准，当前为 `0.44.0`。

本目录描述 BannerlordHtmlUI Framework 的**当前规范、API、集成方式、测试方法与长期维护知识**。

## 先按任务找

- [文档任务地图](DOCUMENT_MAP.md)：不知道该看哪份文档时先看这里。
- [项目状态](PROJECT_STATUS.md)：当前到底完成了什么、哪些还没验收。
- [Bug 知识库](BUG_KNOWLEDGE_BASE.md)：遇到故障先查，包含失败方案和快速定位。

## 文档分层

| 类型 | 入口 | 用途 |
|---|---|---|
| 当前架构 | [ARCHITECTURE_MASTER.md](ARCHITECTURE_MASTER.md) | Framework / Runtime / Consumer 的当前真实架构 |
| API 契约 | [API.md](API.md) | 对外 C# API 与公共语义 |
| 开发与接入 | [DEVELOPMENT_GUIDE.md](DEVELOPMENT_GUIDE.md) | 新 Consumer / 新页面 / 新能力的标准流程 |
| 前端运行时 | [FRONTEND_GUIDE.md](FRONTEND_GUIDE.md) | State / Command / Request / Binding / i18n / Navigation |
| 测试与回归 | [TESTING_AND_REGRESSION.md](TESTING_AND_REGRESSION.md) | TestMod、StressLab、生命周期和发布前验收 |
| Bug 知识库 | [BUG_KNOWLEDGE_BASE.md](BUG_KNOWLEDGE_BASE.md) | 已解决问题、失败方案、根因、快速定位入口 |
| 版本变化 | [CHANGELOG.md](CHANGELOG.md) | 汇总历史版本变更；原始 `CHANGELOG_v*.md` 保留 |
| 项目状态 | [PROJECT_STATUS.md](PROJECT_STATUS.md) | 当前里程碑、已完成、未验收、P0/P1 风险 |
| 发布基线 | [RELEASE_GUIDE.md](RELEASE_GUIDE.md) | v0.44 发布前收口 |

## 当前事实来源优先级

```text
当前代码 / .csproj
        ↓
最近一次真实实机验证
        ↓
docs/PROJECT_STATUS.md
        ↓
docs/ARCHITECTURE_MASTER.md / API.md / TESTING_AND_REGRESSION.md
        ↓
旧 Handoff / Changelog（历史证据）
```

如果历史 Handoff 与当前代码冲突，以当前代码和最近验证为准；历史文档继续保留用于追溯设计原因和修复过程。

## 原有细分文档如何使用

现有的细分文档继续保留，因为其中存在已经验证过的实现细节。新开发优先从本索引进入，再按需要下钻到原始文档。

- `TROUBLESHOOTING.md`、`DEBUGGING.md`、`DIAGNOSTICS.md`：现场定位。
- `PROTOCOL.md`、`API.md`、`INPUT.md`、`LIFECYCLE.md`、`THREADING.md`：实现前确认契约。
- `CONSUMER_*.md`：Consumer 接入与交付细节。
- `FRONTEND_*.md`：Runtime / Binding 深入参考。
- `CHANGELOG_v*.md`：历史证据，不作为当前规范唯一来源。

## Bug 经验保留规则

任何已经解决的问题必须保留完整排错链：

`现象 → 触发条件 → 日志/堆栈 → 初始假设 → 排查过程 → 失败方案 → 失败原因 → 根因 → 修复 → 验证 → 适用版本`

本次整理不会以摘要替换原始复盘。`BUG_KNOWLEDGE_BASE.md` 只是统一检索入口。

## 重要工程原则

1. Consumer 不直接创建或管理 WebView2。
2. WebView2 访问必须遵守 UI-thread 边界。
3. Bannerlord GameThread、WebView2 UI thread、Runtime JS 三个执行域必须明确区分。
4. UI 生命周期必须由 Framework / Owner Scope 管理。
5. `HWND = 0` 不能直接等价为游戏已经关闭。
6. 不把 F12 当作可靠关闭方案；ESC 与页面 Close 是主要验收路径。
7. 默认日志低噪声，不恢复逐帧 Window Tracking 日志。
8. 任何 Overlay / WebView2 窗口样式实验必须通过已验证的回归矩阵。
9. `dev` 是当前开发线；`main` 是发布基线。
