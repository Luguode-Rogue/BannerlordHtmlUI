# Frontend Components and Form Binding

BannerlordHtmlUI provides a small browser-native component layer. It does not require React, Vue, or another frontend framework.

## Visibility

```javascript
app.bind.hidden('#panel', 'ui.busy');
app.bind.visible('#panel', 'ui.ready');
```

Declarative equivalents:

```html
<div data-bhui-hidden="ui.busy"></div>
<div data-bhui-visible="ui.ready"></div>
```

## CSS classes

```javascript
app.bind.class('#status', 'is-active', 'player.active');
```

The class is added when the state value is truthy.

## Forms

```html
<form id="settings">
  <input name="name">
  <input name="enabled" type="checkbox">
</form>
```

```javascript
const dispose = app.bind.form('#settings', {
  name: 'profile.name',
  enabled: 'profile.enabled'
});
```

This is intentionally state-to-form binding only. User changes should be sent to C# explicitly through `app.call()` or `app.request()`.

## Lists

Lists are rendered by a caller-provided function, keeping markup and business presentation under normal HTML/CSS/JS control:

```javascript
const dispose = app.bind.list('#heroes', 'heroes', (hero, index) => {
  const row = document.createElement('div');
  row.textContent = `${index + 1}. ${hero.name}`;
  return row;
});
```

The renderer may return either an element or `{ element, dispose }` for per-item cleanup.

When the state list changes, the previous children are disposed and the container is rebuilt from the new array. This is deliberately simple and predictable rather than trying to become a virtual-DOM framework.

## Lifecycle and disposal

All binding methods return an unsubscribe/dispose function. A page or component should dispose bindings when it is removed.

The root binding helper also exposes:

```javascript
const dispose = app.bind.apply();
dispose();
```

## Design rule

Framework State remains the authoritative game state. Bindings update the DOM from State; they do not silently mutate game state. Use Commands or Requests for user actions.
