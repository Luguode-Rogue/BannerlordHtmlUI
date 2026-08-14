# HTML UI Protocol v1

## Command

Fire-and-forget JS → C#.

```javascript
await game.call('exampleCommand', { value: 10 });
```

Normal `game.call()` requests receive a success/error response through the bridge runtime even though the application-level command handler itself is fire-and-forget. Runtime diagnostics (`runtime.error`) are the exception and do not require a request id.

Command registrations are owner-scoped. If a command is unregistered before its queued game-thread callback executes, that stale callback is discarded. A callback that runs after unregistration must not produce a response or invoke a replacement registration with the same name.

## Request

Request/response JS ⇄ C#.

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
