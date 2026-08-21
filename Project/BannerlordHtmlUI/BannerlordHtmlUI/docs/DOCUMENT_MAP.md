# 文档任务地图

这是 BannerlordHtmlUI 的“先去哪看”索引。它不替代各主文档，也不替代原始历史记录。

## 我现在遇到什么问题？

| 问题 | 第一入口 | 第二入口 | 原始证据 |
|---|---|---|---|
| 不知道某段 Framework 代码应该放哪里 | `FRAMEWORK_MODULE_MAP.md` | `CODE_PLACEMENT_RULES.md` | 当前源码 |
| 页面注册/打开失败 | `API.md` | `BUG_KNOWLEDGE_BASE.md` | `Handoff/` 对应 Postmortem / Handoff |
| 页面显示但按钮点不到 | `CODE_PLACEMENT_RULES.md` | `BUG_KNOWLEDGE_BASE.md` | Input / Overlay 历史复盘 |
| 页面自动消失 | `BUG_KNOWLEDGE_BASE.md` | `ARCHITECTURE_MASTER.md` | HWND / lifecycle / navigation 复盘 |
| ESC 无法关闭 | `BUG_KNOWLEDGE_BASE.md` | `TESTING_AND_REGRESSION.md` | ESC 相关历史记录 |
| F12 行为异常 | `BUG_KNOWLEDGE_BASE.md` | `CODE_PLACEMENT_RULES.md` | 历史交接记录 |
| Overlay 变白/不可见但还能点 | `BUG_KNOWLEDGE_BASE.md` | `ARCHITECTURE_MASTER.md` | `BUG_POSTMORTEM_OVERLAY_RENDERING_20260814.md` |
| WebView2 跨线程异常 | `ARCHITECTURE_MASTER.md` | `BUG_KNOWLEDGE_BASE.md` | `BUGFIX_RUNTIME_PATCH_THREADING_20260816.md` |
| Navigation / Reload 竞态 | `API.md` | `BUG_KNOWLEDGE_BASE.md` | `NAVIGATION_RACE_20260815.md` |
| Request 没结束 / 取消失效 | `API.md` | `BUG_KNOWLEDGE_BASE.md` | Request / cancellation 历史复盘 |
| Owner Dispose 后仍有回调 | `ARCHITECTURE_MASTER.md` | `BUG_KNOWLEDGE_BASE.md` | Owner / race 历史记录 |
| Binding / Component 泄漏 | `FRONTEND_GUIDE.md` | `TESTING_AND_REGRESSION.md` | M3/M6 历史记录 |
| i18n / 语言切换异常 | `FRONTEND_GUIDE.md` | `BUG_KNOWLEDGE_BASE.md` | i18n / localization 历史复盘 |
| HTML 资源找不到 | `DEVELOPMENT_GUIDE.md` | `BUG_KNOWLEDGE_BASE.md` | Consumer deployment 历史记录 |
| Consumer 找不到 Framework DLL | `DEVELOPMENT_GUIDE.md` | `BUG_KNOWLEDGE_BASE.md` | launch/module 配置历史记录 |
| 编译出现大量 C# 级联错误 | `BUG_KNOWLEDGE_BASE.md` | `DEVELOPMENT_GUIDE.md` | Formal build Postmortem |
| 要做新 Consumer | `DEVELOPMENT_GUIDE.md` | `API.md` | `docs/CONSUMER_*.md` |
| 要写新 HTML 页面 | `DEVELOPMENT_GUIDE.md` | `FRONTEND_GUIDE.md` | Consumer 示例 |
| 要修改 Framework API | `API.md` | `ARCHITECTURE_MASTER.md` | `PROTOCOL.md` + 历史决策 |
| 要做压力测试 | `TESTING_AND_REGRESSION.md` | `V0.44_RELEASE_CHECKLIST.md` | StressLab / M5/M6 记录 |
| 要准备发布 | `PROJECT_STATUS.md` | `RELEASE_GUIDE.md` / `V0.44_RELEASE_CHECKLIST.md` | Release 历史 |

## 我想知道“当前项目到底是什么状态”

按以下顺序：

```text
BannerlordHtmlUI.csproj
        ↓
PROJECT_STATUS.md
        ↓
ARCHITECTURE_MASTER.md
        ↓
FRAMEWORK_MODULE_MAP.md
        ↓
最近一次真实实机验证
        ↓
旧 Handoff / Changelog（仅历史）
```

## 我想知道“为什么会变成现在这样”

```text
BUG_KNOWLEDGE_BASE.md
        ↓
对应 Handoff/BUGFIX_*.md / BUG_POSTMORTEM_*.md
        ↓
PROJECT_HANDOFF_*.md / FULL_CODE_AUDIT_*.md
```

不要只看最终结论；失败方案和排查路径是长期知识的一部分。

## 我想从旧文档迁移到新文档

### 架构

- `Handoff/ARCHITECTURE.md` → `docs/ARCHITECTURE_MASTER.md`
- `Handoff/UI_ARCHITECTURE.md` → `docs/ARCHITECTURE_MASTER.md` + `docs/FRONTEND_GUIDE.md`
- `docs/ARCHITECTURE.md` → `docs/ARCHITECTURE_MASTER.md`

### 当前状态 / 里程碑

- `Handoff/PROJECT_STATUS.md` → `docs/PROJECT_STATUS.md`
- `Handoff/TEST_STATUS.md` → `docs/TESTING_AND_REGRESSION.md` / `docs/PROJECT_STATUS.md`
- `Handoff/ROADMAP.md` → `docs/PROJECT_STATUS.md` + Release 文档
- `Handoff/M*_*.md` → 历史里程碑证据；当前结论同步到主文档

### API / Protocol

- `docs/API.md` → 当前公共 API
- `docs/PROTOCOL.md` → 通信协议与错误语义
- `docs/INPUT.md` → 输入语义
- `docs/LIFECYCLE.md` → 生命周期细节
- `docs/THREADING.md` → 线程边界细节

### Framework 代码结构

- `docs/FRAMEWORK_MODULE_MAP.md` → 当前 C# 模块职责地图
- `docs/CODE_PLACEMENT_RULES.md` → 强制代码归属规则
- `docs/ARCHITECTURE_MASTER.md` → 模块之间的状态/线程/生命周期契约

### 前端 Runtime

- `docs/FRONTEND_API.md`
- `docs/FRONTEND_APP.md`
- `docs/FRONTEND_BINDING.md`
- `docs/FRONTEND_COMPONENTS.md`
- `docs/FRONTEND_EVENTS.md`
- `docs/FRONTEND_FORMS.md`
- `docs/FRONTEND_LIFECYCLE.md`

以上细分文档继续作为深入参考；统一入口为 `docs/FRONTEND_GUIDE.md`。

### Consumer

- `docs/CONSUMER_SCOPE.md`
- `docs/CONSUMER_INTEGRATION.md`
- `docs/CONSUMER_TEMPLATE.md`
- `docs/CONSUMER_TEST.md`
- `docs/CONSUMER_DEPLOYMENT_CHECKLIST.md`
- `docs/GOLDEN_CONSUMER_EXAMPLE.md`

统一入口为 `docs/DEVELOPMENT_GUIDE.md`。

### Bug / Postmortem

所有 `Handoff/BUGFIX_*.md`、`Handoff/BUG_POSTMORTEM_*.md`、工作日志里的故障复盘，统一从 `docs/BUG_KNOWLEDGE_BASE.md` 检索。

原始文件继续保留，不能用摘要替代。

### Changelog

所有 `CHANGELOG_v*.md` 保持原样，统一从 `docs/CHANGELOG.md` 导航。

## 文档维护规则

新产生的 Bug：

1. 先写完整原始复盘。
2. 将可复用经验补到 `BUG_KNOWLEDGE_BASE.md`。
3. 如果改变了架构/公共语义，再同步 `ARCHITECTURE_MASTER.md` 与 `FRAMEWORK_MODULE_MAP.md`。
4. 如果改变了公共 API，再同步 `API.md`。
5. 如果改变了验收条件，再同步 `TESTING_AND_REGRESSION.md` / `PROJECT_STATUS.md`。
6. 如果改变了代码归属原则，再同步 `CODE_PLACEMENT_RULES.md`。

这样“历史证据”和“当前规范”始终保持双向关联，而不是继续产生孤立 Markdown。
