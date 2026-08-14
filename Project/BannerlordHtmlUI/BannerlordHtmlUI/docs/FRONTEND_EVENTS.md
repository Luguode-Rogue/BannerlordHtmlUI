# Frontend Events and Templates

## Event delegation

Use `game.app.bind.delegate(root, eventName, selector, handler)` to attach one listener to a stable container and route events to matching descendants.

```js
const off = game.app.bind.delegate('#list', 'click', '[data-id]', (event, target) => {
    game.app.call('select', { id: target.dataset.id });
});
```

`game.app.bind.events()` accepts a map of event names to one or more `{ selector, handler, options }` definitions.

## Template rendering

`game.app.bind.template(container, stateKey, render, options)` renders an array from Framework State and reuses keyed elements between updates.

```js
const dispose = game.app.bind.template('#heroes', 'heroes', (hero) => {
    const el = document.createElement('div');
    el.dataset.id = hero.id;
    el.textContent = hero.name;
    return {
        element: el,
        update(next) { el.textContent = next.name; },
        dispose() {}
    };
}, { key: hero => hero.id });
```

A template result may expose `update(item, index)` and `dispose()` for per-item lifecycle.
