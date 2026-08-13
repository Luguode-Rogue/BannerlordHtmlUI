# Frontend Application API

`window.game.app` is the recommended frontend entry point for a page. It groups the page's command/request API, state, events, lifecycle, errors, input, and page navigation.

For a consumer page owned by `MyMod`:

```javascript
const app = game.app;

await app.call('save', { value: 123 });
const data = await app.request('load');
const off = app.on('changed', value => console.log(value));
const offState = app.state.subscribe('counter', value => console.log(value));

app.lifecycle.on(info => console.log(info.state));
app.errors.on(error => console.error(error));

await app.pages.open('settings');
```

The framework still supports the lower-level `game.call`, `game.request`, `game.on`, `game.state`, and `game.scope()` APIs for compatibility. New consumer pages should prefer `game.app`.

`game.app` is scoped automatically from the page owner query metadata. Framework-owned pages receive an unscoped app that targets framework commands/events/state.
