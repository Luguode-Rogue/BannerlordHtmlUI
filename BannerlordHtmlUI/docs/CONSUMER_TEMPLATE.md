# Consumer Mod Template

BannerlordHtmlUI is intended to be a framework dependency, not a UI implementation tied to a specific gameplay Mod.

## Minimum integration

1. Reference `BannerlordHtmlUI.dll`.
2. Declare `<DependedModule Id="BannerlordHtmlUI" />`.
3. In `OnSubModuleLoad`, register an `HtmlUiService.OnReady(...)` callback.
4. Register your own content root.
5. Register pages under that root.
6. Register Commands/Requests and publish State/Events.
7. Unregister pages/handlers during `OnSubModuleUnloaded`.

Example:

```csharp
protected override void OnSubModuleLoad()
{
    HtmlUiService.OnReady(RegisterUi);
}

private void RegisterUi()
{
    HtmlUiService.RegisterContentRoot(
        "MyMod",
        Path.Combine(ModuleDirectory, "UI"));

    HtmlUiService.Pages.Register(
        new HtmlUiPage("main", "Main/index.html")
        {
            ContentRootId = "MyMod"
        });
}
```

## Web code

Every Framework-managed page receives the runtime automatically. Do not copy `runtime.js` into your Mod just to obtain `window.game`.

```javascript
const result = await game.request('my.request', { value: 1 });
game.call('my.command', { value: 2 });
game.on('my.event', data => { /* ... */ });
game.state.subscribe('my.state', value => { /* ... */ });
```

## Resource isolation

Each consumer Mod has its own content-root id and virtual host. A page may only navigate inside its registered root. Do not use absolute filesystem paths from HTML/JS.

## Lifecycle rule

Do not register consumer UI before `HtmlUiService.IsReady` / the `OnReady` callback. Always unregister pages, commands, and requests when the consumer Mod unloads.
