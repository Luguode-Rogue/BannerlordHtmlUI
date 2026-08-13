# Frontend State Binding

BannerlordHtmlUI provides a small DOM binding layer without requiring React, Vue, or another frontend framework.

## Imperative binding

```javascript
const app = game.app;

app.bind.text('#gold', 'player.gold');
app.bind.value('#name', 'player.name');
app.bind.checked('#enabled', 'player.enabled');
app.bind.disabled('#saveButton', 'ui.busy');
app.bind.attr('#portrait', 'src', 'player.portrait');
```

Bindings update when the corresponding Framework State key changes. The methods return an unsubscribe function.

For consumer scopes, keys are automatically scoped to the current owner:

```javascript
app.bind.text('#gold', 'gold');
// resolves to MyMod.gold for a MyMod page
```

## Declarative binding

The following attributes are supported:

```html
<span data-bhui-text="player.gold"></span>
<input data-bhui-value="player.name">
<input type="checkbox" data-bhui-checked="player.enabled">
<button data-bhui-disabled="ui.busy">Save</button>
```

Initialize them with:

```javascript
const dispose = game.app.bind.apply();
```

Dispose the bindings when a page or component is removed:

```javascript
dispose();
```

## Important semantics

This layer is intentionally **one-way**: Bannerlord/C# State is the source of truth. Updating an input element does not automatically write back to State. Use a Command or Request for user edits so game logic remains authoritative.

This keeps game state changes explicit and avoids accidental feedback loops.

## v0.31 component helpers

Additional helpers are available from `game.app.bind`:

```javascript
app.bind.hidden('#panel', 'ui.busy');
app.bind.visible('#panel', 'ui.ready');
app.bind.class('#status', 'is-active', 'player.active');
app.bind.form('#settings', { name: 'profile.name' });
app.bind.list('#heroes', 'heroes', (hero, index) => {
  const row = document.createElement('div');
  row.textContent = `${index + 1}. ${hero.name}`;
  return row;
});
```

See `FRONTEND_COMPONENTS.md` for lifecycle and disposal rules.
