# M4 / v0.44 Public API Freeze Checklist

日期：2026-08-16
主开发分支：`dev`

## 冻结目标

v0.44 的目标不是冻结实现细节，而是冻结 Consumer Mod 可以依赖的行为契约。

## Public API

### C# Consumer

- `HtmlUiService.OnReady`
- `HtmlUiService.IsReady`
- `HtmlUiService.RegisterContentRoot`
- `HtmlUiService.CreateScope`
- `HtmlUiService.Pages`
- `HtmlUiService.State`
- `HtmlUiPage`
- `HtmlUiPageManager`
- `HtmlUiStateStore`
- `HtmlUiConsumerScope`

Consumer 不应直接依赖 `HtmlUiHost`、`HtmlUiBridge` 等 implementation type。

### JS Consumer

- `game.call`
- `game.request`
- `game.requestCancellable`
- `game.on`
- `game.state`
- `game.i18n`
- `game.bind`
- `game.input`
- `game.pages`
- `game.page`
- `game.lifecycle`
- `game.errors`
- `game.ready`
- `game.scope`

## 稳定语义

### Ownership

Consumer 的 Page / Command / Request / State / ContentRoot 使用 Scope ownership 管理。Scope Dispose 后不得再接受新的注册，并清理其拥有资源。

### Request

- 普通 `request()` 超时由 JS 客户端 timeout 保证最终拒绝。
- 已知的 stale / unregistered 情况由 Bridge 尽早返回明确错误。
- 可取消 Request 使用 AbortSignal，并允许 pagehide / runtime shutdown 触发取消。
- 取消后的成功结果不得回到 JS。

### Page Lifecycle

- Page navigation/reload 会形成新的 JS Runtime。
- 页面离开时 page-local Promise 不得永久 pending。
- Page / Scope Dispose 必须回收其所属资源。

### Binding

- Individual disposer 幂等。
- Binder Dispose 必须注销 State/Event listener，并回收 list/template/component child。
- pagehide 必须回收页面 Binder。
- i18n DOM Binding 必须回收 MutationObserver、locale listener 与 translation cache。

### Errors

JS Promise rejection 暴露 `BannerlordHtmlUiError`，稳定使用 `error.code` 分支，不匹配完整错误文本。

稳定错误码见：`Handoff/M4_ERROR_MODEL_20260816.md`。

## Protocol

当前 Protocol version：`1`。

框架版本与 Protocol version 分离。仅实现内部修复不得随意提高 Protocol version。

## 不冻结的实现细节

- WebView2 Host 内部对象结构
- C# Patch 的具体实现方式
- Runtime 模块拆分方式
- Overlay Win32 实现细节
- Diagnostics 内部采样方式

## Release 前必须完成

- [ ] Consumer 全部公开 API 与 `.d.ts` 一致
- [ ] Public API 文档与实际实现一致
- [ ] Error code 全部可从实际 Promise rejection 获取
- [ ] Request cancellation 实机回归
- [ ] Binding lifecycle 实机回归
- [ ] Language switch 实机回归
- [ ] StressLab 长测
- [ ] 多 Page / Reload 长测
- [ ] Release 日志默认低噪声
