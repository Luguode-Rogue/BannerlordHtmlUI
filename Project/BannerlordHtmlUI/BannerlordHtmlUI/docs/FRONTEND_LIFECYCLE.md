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

On every page load or reload, `game.ready()` requests `framework.getStateSnapshot`. This restores Framework and consumer state that still exists in the C# state store.
