# Frontend Runtime 统一指南

当前前端相关文档分散在 `FRONTEND_API.md`、`FRONTEND_APP.md`、`FRONTEND_BINDING.md`、`FRONTEND_COMPONENTS.md`、`FRONTEND_EVENTS.md`、`FRONTEND_FORMS.md`、`FRONTEND_LIFECYCLE.md`、`INPUT.md`、`PROTOCOL.md` 等文件。

本页不删除这些原始文档，只提供统一理解顺序。

## State

表示“UI 现在是什么状态”。推荐 C# 发布结构化对象，由 Runtime 统一同步。

重点规则：

- 相同值不应重复广播。
- JSON-like 数组、字典、对象需要内容比较。
- State remove 必须有明确 delete 语义；当前契约以实际 API/回归测试结果为准。
- 页面 Runtime 建立时需要 State snapshot hydration。

## Command

用于无结果动作：点击、选择、切换、保存、关闭等。

Command 重复注册不得静默覆盖其他 Owner。

## Request

用于有结果操作，可同步或异步。`requestCancellable()` 支持 AbortSignal、timeout、pagehide 和 runtime shutdown cancellation。

取消后晚到结果不得覆盖后续请求或页面状态。

## Event

用于 Framework/Consumer 广播事件。Event listener 必须具备 pagehide/dispose 生命周期。

## Binding

当前已覆盖：

- State binding
- Two-way binding
- keyed List
- Template
- Component
- i18n.bind
- dynamic DOM binding
- debounce/throttle

生命周期必须有 disposer。MutationObserver、timer、locale listener、child component 都必须在 dispose/pagehide 时释放。

连续 DOM mutation 应合并 refresh pass，避免同一周期重复翻译和 DOM 写回。

## i18n

标准链路：

```text
Bannerlord Localization
 → Framework Localization
 → game.app.i18n
 → HTML/JS
```

前端 API 目标包括：

- `i18n.t`
- `getLocale`
- `getLanguages`
- `onLocaleChanged`
- `formatDate`
- `formatTime`
- `i18n.bind`

本地化失败不能清空已有默认文本。

## Component

Component 对象可能携带 prototype、Symbol 或 non-enumerable 方法。不要使用 object spread 创建替代对象。应保留原对象，只包装 dispose 生命周期。

## Navigation

复杂菜单优先：

```text
一个 Page
+ JS View modules
+ navigation stack
```

不要因为子菜单就重复 Navigate。减少 Page/Runtime/Input 生命周期切换。

## Runtime Error

统一 Error model 应至少可提供：

```text
code
raw
operation
requestName
message
```

`runtime.error` 这类无 id fire-and-forget 消息与普通无 id Command 必须区分处理。

## Frontend 生命周期

```text
document-created
 → bootstrap
 → game/app available
 → Runtime Ready
 → bind/state hydration
 → user interaction
 → pagehide
 → dispose
```

任何长期持有的 listener、observer、timer、request、component 都必须有明确回收路径。
