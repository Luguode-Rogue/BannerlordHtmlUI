# HTML UI Protocol v1

## Command

Fire-and-forget JS → C#.

```javascript
await game.call('exampleCommand', { value: 10 });
```

## Request

Request/response JS ⇄ C#.

```javascript
const result = await game.request('getExample', {});
```

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

Unhandled window errors and unhandled promise rejections are forwarded as `runtime.error` events to the host logger.
