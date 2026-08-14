# HTML UI Protocol v1

## Command

Fire-and-forget JS → C#.

```javascript
await game.call('exampleCommand', { value: 10 });
```

Normal `game.call()` requests receive a success/error response through the bridge runtime even though the application-level command handler itself is fire-and-forget. Runtime diagnostics (`runtime.error`) are the exception and do not require a request id.

## Request

Request/response JS ⇄ C#.

```javascript
const result = await game.request('getExample', {});
```

A request must contain a non-empty request id. Missing ids are rejected by the host bridge.

## Event

C# → JS.

```javascript
game.on('exampleChanged', data => console.log(data));
```

## State

```javascript
const value = game.state.get('example.value');
game.state.subscribe('example.value', value => console.log(value));
```

## Runtime errors

Unhandled window errors and unhandled promise rejections are forwarded as `runtime.error` commands to the host logger. They are intentionally fire-and-forget and use no request id.

## Protocol validation

The current protocol version is `1`. Messages with another version are rejected and, when they contain a request id, receive an error response. Unknown message types are also rejected instead of being silently ignored.

Unknown Command / Request names are returned as bridge errors. Duplicate Command / Request registrations are rejected on the C# side so one Consumer cannot silently replace another registration.
