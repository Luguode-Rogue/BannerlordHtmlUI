# BannerlordHtmlUI 文档中心

> 当前工作分支：`dev`
> 当前 Framework 版本：以 `BannerlordHtmlUI.csproj` 为准。

本目录描述 BannerlordHtmlUI Framework 的当前规范、API、集成方式、测试方法与长期维护知识。

## 先按任务找

- [文档任务地图](DOCUMENT_MAP.md)：不知道该看哪份文档时先看这里。
- [Framework 模块地图](FRAMEWORK_MODULE_MAP.md)：**修改 C# Framework 之前先看**，明确每个模块负责什么。
- [代码归属硬规则](CODE_PLACEMENT_RULES.md)：**新增/修改代码必须遵守**，禁止把逻辑写进错误区域。
- [项目状态](PROJECT_STATUS.md)：当前到底完成了什么、哪些还没验收。
- [Bug 知识库](BUG_KNOWLEDGE_BASE.md)：遇到故障先查，包含失败方案、根因和代码归属。
- [工程资源放置规则](../../../../BUTR_PROJECT_LAYOUT_RULES.md)：决定 `_Module`、`web`、`UI` 等最终部署路径。

## 文档分层

| 类型 | 入口 | 用途 |
|---|---|---|
| 当前架构 | [ARCHITECTURE_MASTER.md](ARCHITECTURE_MASTER.md) | Framework / Runtime / Consumer 的当前真实架构与职责边界 |
| Framework 模块 | [FRAMEWORK_MODULE_MAP.md](FRAMEWORK_MODULE_MAP.md) | 每个 C# 模块的唯一职责、允许做什么、禁止做什么 |
| 代码归属规则 | [CODE_PLACEMENT_RULES.md](CODE_PLACEMENT_RULES.md) | 防止把输入、窗口、Page、Bridge、Consumer workaround 写错地方 |
| API 契约 | [API.md](API.md) | 对外 C# API 与公共语义 |
| 开发与接入 | [DEVELOPMENT_GUIDE.md](DEVELOPMENT_GUIDE.md) | 新 Consumer / 新页面 / 新能力的标准流程 |
| 前端运行时 | [FRONTEND_GUIDE.md](FRONTEND_GUIDE.md) | State / Command / Request / Binding / i18n / Navigation |
| 测试与回归 | [TESTING_AND_REGRESSION.md](TESTING_AND_REGRESSION.md) | TestMod、StressLab、生命周期和发布前验收 |
| Bug 知识库 | [BUG_KNOWLEDGE_BASE.md](BUG_KNOWLEDGE_BASE.md) | 已解决问题、失败方案、根因和快速定位 |
| 版本变化 | [CHANGELOG.md](CHANGELOG.md) | 汇总历史版本变更；原始 `CHANGELOG_v*.md` 保留 |
| 项目状态 | [PROJECT_STATUS.md](PROJECT_STATUS.md) | 当前里程碑、已完成、未验收、P0/P1 风险 |
| 发布基线 | [RELEASE_GUIDE.md](RELEASE_GUIDE.md) | 发布前收口 |

## 当前事实来源优先级

```text
当前代码 / .csproj
        ↓
最近一次真实实机验证
        ↓
docs/PROJECT_STATUS.md
        ↓
docs/ARCHITECTURE_MASTER.md / FRAMEWORK_MODULE_MAP.md / API.md
        ↓
旧 Handoff / Changelog（历史证据）
```

如果历史 Handoff 与当前代码冲突，以当前代码和最近验证为准；历史文档继续保留用于追溯设计原因和修复过程。

## Framework 修改前的强制入口

```text
FRAMEWORK_MODULE_MAP.md
        ↓
CODE_PLACEMENT_RULES.md
        ↓
BUG_KNOWLEDGE_BASE.md
        ↓
当前代码
```

如果找不到明确 owner：

> **先拆职责/补模块边界，不要把代码临时塞进 Host、Keyboard Patch 或 ConsumerScope。**

## 新建文件与资源

本项目采用 BUTR Bannerlord 项目结构。新增工程文件时先确定最终部署路径，再决定源码位置。

- 最终属于 `Modules/<ModId>/` 根目录的文件：对应 `_Module/<相同相对路径>`。
- Framework / Consumer 的 DLL 旁 Web/UI 资源：先检查该项目的 `SubModule.cs` 与 `.csproj`，按 `Assembly.Location` 和 Deployment Target 决定。
- 不要求用户手工复制文件到游戏 `Modules` 目录；Build/Deploy 应负责部署。

统一资源规则见：[BUTR 工程资源放置规则](../../../../BUTR_PROJECT_LAYOUT_RULES.md)。

## 原有细分文档

细分文档继续保留，因为其中存在已经验证过的实现细节。新开发优先从本索引进入，再按需要下钻。

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
4. Page 生命周期由 PageManager 管理；Consumer 只能管理自己的业务资源。
5. InputMode 只有 InputController 一个 owner。
6. Window facts / geometry 只有 WindowTracker 一个 owner。
7. Browser policy 只有一个 policy owner；不要再各处复制 F12/右键逻辑。
8. `HWND = 0` 不能直接等价为游戏已经关闭。
9. 不把 F12 当作可靠关闭方案；ESC 与页面 Close 是主要验收路径。
10. 默认日志低噪声，不恢复逐帧 Window Tracking 日志。
11. 任何 Overlay / WebView2 窗口样式实验必须通过已验证的回归矩阵。
12. `dev` 是当前开发线；`main` 是发布基线。
