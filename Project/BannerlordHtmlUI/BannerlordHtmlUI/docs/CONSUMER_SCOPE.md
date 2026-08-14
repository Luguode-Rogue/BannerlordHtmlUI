# Consumer Scope

`HtmlUiConsumerScope` is the recommended integration boundary for a Bannerlord Mod that uses BannerlordHtmlUI.

## Why use a scope?

A scope gives one Mod ownership of:

- content roots;
- pages;
- commands;
- requests;
- state keys;
- events emitted by that Mod.

Names registered through a scope are automatically prefixed with `OwnerId + "."` to prevent collisions.

## Example

```csharp
private HtmlUiConsumerScope _ui;

private void RegisterUi()
{
    _ui = HtmlUiService.CreateScope("MyMod");
    _ui.RegisterContentRoot("ui", Path.Combine(ModuleDirectory, "UI"));

    _ui.RegisterPage(new HtmlUiPage("main", "index.html")
    {
        ContentRootId = _ui.ContentRootName("ui")
    });

    _ui.RegisterCommand("save", payload => { });
    _ui.RegisterRequest("load", payload => Task.FromResult<object>(new { ok = true }));
    _ui.SetState("ready", true);
    _ui.SendEvent("saved", new { ok = true });
}

protected override void OnSubModuleUnloaded()
{
    _ui?.Dispose();
}
```

The HTML side uses the scoped names:

```javascript
const prefix = "MyMod.";
game.call(prefix + "save", {});
game.request(prefix + "load", {});
game.on(prefix + "saved", handler);
game.state.subscribe(prefix + "ready", handler);
```

`Dispose()` unregisters the owned resources and removes owned state keys. The Framework's own registrations are unaffected.

## Automatic page and root scoping

`RegisterContentRoot("ui", ...)` returns `OwnerId.ui`. `RegisterPage(new HtmlUiPage("main", ...))` returns `OwnerId.main`. The scoped names are global identifiers used by the Framework and by the HTML bridge.
