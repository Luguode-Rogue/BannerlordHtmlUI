# M4 / v0.44 JavaScript Error Model

日期：2026-08-16
分支：`feature/framework-finalization-m3-m4`

## 目标

把 Framework 已有的 Bridge 字符串错误，在 Consumer 可见的 JS API 边界统一稳定为 `BannerlordHtmlUiError`，同时保持 Protocol v1 的原始 Response 错误格式兼容。

## 设计

底层 Bridge 继续返回原有字符串错误，避免破坏协议兼容；DocumentCreated 阶段注入 `HtmlUiErrorModelPatch`，在：

- `game.call()`
- `game.request()`
- Consumer Scope 创建后的 `call/request`

返回 Promise rejected 时，将错误转换为：

```text
name        = BannerlordHtmlUiError
code        = 稳定机器码
raw         = 原始错误文本
operation   = command | request
requestName = 对应 Command / Request 名称
```

## 稳定错误码

| code | 语义 |
|---|---|
| `COMMAND_TIMEOUT` | Command 客户端超时 |
| `REQUEST_TIMEOUT` | Request 客户端超时 |
| `COMMAND_UNKNOWN` | 未注册 Command |
| `REQUEST_UNKNOWN` | 未注册 Request |
| `COMMAND_STALE` | Command 在排队执行前已注销 |
| `COMMAND_UNREGISTERED` | Command 执行期间注销 |
| `REQUEST_STALE` | Request 在执行前已注销 |
| `REQUEST_UNREGISTERED` | Request 异步执行期间注销 |
| `PROTOCOL_UNSUPPORTED_VERSION` | Protocol version 不受支持 |
| `PROTOCOL_UNKNOWN_TYPE` | 未知消息类型 |
| `RUNTIME_DISPOSED` | Runtime 已释放 |
| `PAGE_UNLOADED` | 页面已卸载 |
| `COMMAND_HANDLER_ERROR` | Command handler 抛错或产生未分类 Command 错误 |
| `REQUEST_HANDLER_ERROR` | Request handler 抛错或产生未分类 Request 错误 |
| `BRIDGE_ERROR` | 未分类 Bridge 错误 |

## 兼容性

- Protocol v1 不变。
- C# Bridge 不要求 Consumer 修改已有 handler 签名。
- `Error.message` 保留原始人类可读错误文本。
- 旧 Consumer 不读取 `error.code` 时行为保持兼容。
- 新 Consumer 应优先基于 `error.code` 分支，不应匹配完整中文/英文错误字符串。

## 边界

该 Patch 是公共 API 的错误模型适配层，不修改 Runtime Core 内部 Request/Response 状态机；核心协议仍以 `docs/PROTOCOL.md` 为准。

## 验收标准

```text
await game.request(unknown)
→ error.name === 'BannerlordHtmlUiError'
→ error.code === 'REQUEST_UNKNOWN'

await game.call(unknown)
→ error.code === 'COMMAND_UNKNOWN'

request timeout
→ error.code === 'REQUEST_TIMEOUT'

runtime disposed
→ error.code === 'RUNTIME_DISPOSED'

stale registration
→ error.code === 'REQUEST_STALE' / 'COMMAND_STALE'
```

实机验收应补充到 Consumer StressLab / M5 回归矩阵。
