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

### Cancellable Request

Consumers that need actual cancellation may use the cancellable API:

```javascript
const controller = new AbortController();
const result = await game.requestCancellable(
  'getExample',
  {},
  10000,
  controller.signal
);

controller.abort();
```

The cancellation is protocol-level, not merely a local Promise rejection. The browser sends a `cancel` message using the original request id. The C# bridge maps that id to a `CancellationTokenSource` when execution begins and also keeps a short-lived pre-cancel marker for cancellations that arrive before the game-thread callback starts.

C# consumers may register a cancellable handler:

```csharp
RegisterRequest(
    "getExample",
    async (payload, cancellationToken) =>
    {
        await DoWorkAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    });
```

Cancellation may occur before handler execution, during handler execution, during page unload, or during framework shutdown. A cancelled request must not emit a successful late response.

`ActiveRequestCount` is the number of request executions currently holding a `CancellationTokenSource`. Calling `Cancel()` is not itself the terminal state; the active count returns to baseline only after the handler reaches its cleanup path and the bridge removes the request from the active cancellation registry.

### Request execution thread

The bridge queues the initial Request handler invocation onto the Bannerlord game thread. After an `await`, normal C# synchronization semantics apply; a continuation is not guaranteed to resume on the Bannerlord game thread. Consumer code that requires game-thread affinity must explicitly marshal back to the game thread before touching game-thread-only APIs.

Response delivery is separately marshalled to the WebView2 UI thread by `HtmlUiHost.SendResponseAsync()`, so an async Request continuation must not access `CoreWebView2` directly.

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
- Pagehide and Framework shutdown cancel cancellable Requests, but cancellation completion is considered finished only after the handler exits and the bridge removes the active request entry.

## Error and timeout contract

Bridge errors are returned as error strings on the corresponding Response. JS `game.request(name, payload, timeoutMs)` still applies its client-side timeout for requests that remain valid but do not complete; explicit bridge invalidation errors are preferred when the framework already knows that a request cannot complete because its registration was removed.

The Runtime error model additionally exposes a stable machine-readable `error.code` for known bridge failures while preserving the existing human-readable `error.message` for compatibility.
