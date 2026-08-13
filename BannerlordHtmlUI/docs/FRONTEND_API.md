# BannerlordHtmlUI Frontend API

## 1. Global API

Every page receives `window.game` from BannerlordHtmlUI.

```js
await game.call("framework.ping", { value: 1 });
const data = await game.request("framework.getDiagnostics");
game.on("some.event", payload => {});
```

## 2. Consumer scope

A consumer page registered by `HtmlUiConsumerScope.RegisterPage(...)` automatically receives its owner ID through the page URL. The preferred API is therefore:

```js
const app = game.scope();

await app.call("save", { id: 123 });
const item = await app.request("load", { id: 123 });

const off = app.on("saved", payload => {
    console.log(payload);
});

const offState = app.state.subscribe("counter", value => {
    console.log(value);
});
```

No `OwnerId.` prefix needs to be written in page code.

For a page that is not owned by a consumer scope, use an explicit owner:

```js
const app = game.scope("MyMod");
```

## 3. State

A scoped state key:

```csharp
scope.SetState("counter", 10);
```

is exposed as:

```js
app.state.get("counter");
app.state.subscribe("counter", value => {});
```

The raw global API is still available as `game.state` for Framework-level state.

## 4. Events

C#:

```csharp
scope.SendEvent("saved", new { id = 123 });
```

JS:

```js
app.on("saved", data => {
    console.log(data.id);
});
```

## 5. TypeScript

The repository includes `web/bannerlord-html-ui.d.ts` as a lightweight editor/type hint file. Copy it into a consumer project's frontend source tree when using TypeScript.
