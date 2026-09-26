# Frontend lifecycle and error handling

## Page lifecycle

Every HTML page exposes:

```js
console.log(game.page.id);
console.log(game.page.ownerId);
console.log(game.page.lifecycle);
```

Listen for lifecycle changes:

```js
game.page.onLifecycle(info => {
    console.log(info.state, info.pageId, info.ownerId);
});
```

Possible states used by the framework are `loading`, `opening`, `ready`, and `closed`.

For consumer pages, the scoped helper is also available:

```js
game.scope().pageLifecycle.on(info => {
    console.log(info.state);
});
```

## Frontend errors

Capture uncaught errors and unhandled promise rejections:

```js
game.errors.on(error => {
    console.error('UI error:', error);
});

console.log(game.errors.last);
```

Errors are isolated to the page runtime. They are also reported to the C# framework log through the existing `runtime.error` bridge.

## State restoration

`game.ready()` is a single-flight operation: runtime starts it automatically, and every
consumer call returns the same Promise. It restores retained state through the versioned
snapshot protocol; state events that race with the snapshot cannot be overwritten by an
older response.

Use `watch` for the normal “initial value plus later updates” pattern:

```js
const app = game.app;
const off = app.state.watch('player', renderPlayer);
```

Surface 文档不需要自行解决挂载竞态。iframe 导航完成后，Framework 会把当前带 revision 的 retained-state 快照直接注入该文档；`watch` 对实时更新立即交付，并用快照补齐挂载前已经发布的首值。

Use `game.refreshState()` only when an explicit resynchronization is required. Calling
`game.ready()` repeatedly no longer performs repeated full-state requests.

Surface code may report completion of its own subscriptions and first render separately
from runtime readiness:

```js
await game.surface.ready({ component: 'MissionHud' });
```
