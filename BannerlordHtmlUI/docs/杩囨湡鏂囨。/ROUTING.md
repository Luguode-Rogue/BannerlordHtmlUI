# Page Routing and Context

## Page context

Every loaded page receives framework metadata in the URL and exposes it through `game.page`:

```js
console.log(game.page.id);
console.log(game.page.ownerId);
console.log(game.page.isConsumer());
```

## Consumer-scoped navigation

A consumer page can navigate only within its own scope through:

```js
const app = game.scope();
await app.pages.open("settings");
await app.pages.close();
```

The framework converts these calls into the consumer's scoped page ids. The page manager still performs normal page registration and content-root validation.

The query parameters `__bannerlord_htmlui_owner` and `__bannerlord_htmlui_page` are framework metadata, not an authentication boundary.
