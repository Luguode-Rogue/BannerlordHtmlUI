# Component and List API

## Keyed lists

`game.app.bind.list(target, stateKey, render, options)` now supports keyed diffing.

```js
const dispose = game.app.bind.list('#heroes', 'heroes', (hero) => {
  const el = document.createElement('div');
  el.textContent = hero.name;
  return el;
}, {
  key: hero => hero.id
});
```

Existing DOM nodes are reused when keys remain stable. Removed keys are disposed. Set `diff: false` to force a full rebuild.

## Components

`game.app.bind.component(target, factory, props)` mounts a small component without introducing a framework dependency.

```js
const component = game.app.bind.component('#card', props => {
  const el = document.createElement('div');
  el.textContent = props.title || '';
  return {
    element: el,
    update(next) { el.textContent = next.title || ''; },
    dispose() { /* cleanup */ }
  };
}, { title: 'Hello' });

component.update({ title: 'World' });
component.dispose();
```

Components remain plain JavaScript. They do not replace the browser DOM or introduce a virtual DOM.
