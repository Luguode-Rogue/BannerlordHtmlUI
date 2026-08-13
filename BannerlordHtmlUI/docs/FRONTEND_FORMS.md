# Frontend Forms and Input Binding

## Two-way value binding

```javascript
const off = game.app.bind.twoWayValue(
  '#name',
  'profile.name',
  (value) => game.app.call('profile.setName', { value }),
  { event: 'input', debounce: 150 }
);
```

The Framework remains state-authoritative: UI input invokes a command/request; the C# side updates State; State then updates the control.

## Two-way checkbox binding

```javascript
const off = game.app.bind.twoWayChecked(
  '#enabled',
  'profile.enabled',
  value => game.app.call('profile.setEnabled', { value }),
  { throttle: 100 }
);
```

## Group disposal

```javascript
const dispose = game.app.bind.group(offA, offB, offC);
```

## Debounce / throttle

`bind.debounce(writer, milliseconds)` and `bind.throttle(writer, milliseconds)` wrap input writers. They do not mutate Framework State directly.
