# HTML UI Protocol v1

## Command

JS → C# command handler。应用层 handler 本身不返回异步结果，但 `game.call()` 仍会得到一次成功/错误 Response，因此可以用于需要确认“已接收/执行”的普通 UI 操作。

```javascript
await game.call('exampleCommand', { value: 10 });
```

框架内部的 `runtime.error` 诊断消息为特殊 fire-and-forget Command，不需要 request id，也不要求 Response。

Command registrations are owner-scoped. If a command is unregistered before its queued game-thread callback executes, that stale callback is discarded. A callback that runs after unregistration must not produce a success response or invoke a replacement registration with the same name.

## Request

Request/response JS ⇄ C#。

```javascript
const result = await game.request('getExample', {});
```

A request must contain a non-empty request id. Missing ids are rejected by the host bridge.

Request registrations are owner-scoped. If a request is unregistered before its queued game-thread callback starts, the callback is discarded. If a request is unregistered while its asynchronous handler is awaiting, its eventual result is discarded and is not delivered to a later registration using the same request name.

This prevents a disposed Consumer Scope from receiving stale work and prevents an old asynchronous handler from replying to a newer registration.

## Event

C# → JS.

```javascript
game.on('exampleChanged', data => console.log(data));
```

Event subscriptions follow the returned disposer/lifecycle of the owning runtime scope.

## State

```javascript
const value = game.state.get('example.value');
game.state.subscribe('example.value', value => console.log(value));
```

State removal publishes `null` on the corresponding `state:<key>` channel so subscribers and bindings can observe deletion. Repeating a logically unchanged JSON-like value does not produce another state notification.

## Runtime errors

Unhandled window errors and unhandled promise rejections are forwarded as `runtime.error` commands to the host logger. They are intentionally fire-and-forget and use no request id.

## Protocol validation

The current protocol version is `1`. Messages with another version are rejected and, when they contain a request id, receive an error response. Unknown message types are also rejected instead of being silently ignored.

Unknown Command / Request names are returned as bridge errors. Duplicate Command / Request registrations are rejected on the C# side so one Consumer cannot silently replace another registration.

## Lifecycle / stale work

Page navigation, Reload, ConsumerScope disposal, and Framework shutdown may invalidate in-flight application work.

- A stale Command callback must not invoke a replacement registration with the same name.
- A stale Request callback receives an explicit bridge error instead of waiting for the JS timeout.
- An asynchronous Request result that becomes stale after unregistration is not delivered to a later registration with the same name.
- Consumers that need work to survive page replacement should own that work in C# or another longer-lived application scope rather than relying on a page-local JS Promise.

## Error and timeout contract

Bridge errors are returned as error strings on the corresponding Response. JS `game.request(name, payload, timeoutMs)` still applies its client-side timeout for requests that remain valid but do not complete; explicit bridge invalidation errors are preferred when the framework already knows that a request cannot complete because its registration was removed.
