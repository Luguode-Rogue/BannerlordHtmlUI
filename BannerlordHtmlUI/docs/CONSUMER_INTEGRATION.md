# Consumer Mod Integration

## 1. Architecture

Install BannerlordHtmlUI as its own Module. A consumer Mod references `BannerlordHtmlUI.dll` and depends on the `BannerlordHtmlUI` module. The consumer does **not** call `HtmlUiService.InitializeAsync()` and does **not** create its own WebView2 host.

This project is a framework host, not a library that each consumer Mod embeds separately. Bannerlord module dependencies are represented by `<DependedModule Id="..."/>` in `SubModule.xml`.

## 2. Consumer SubModule.xml

```xml
<DependedModules>
  <DependedModule Id="Native" />
  <DependedModule Id="SandBoxCore" />
  <DependedModule Id="Sandbox" />
  <DependedModule Id="BannerlordHtmlUI" />
</DependedModules>
```

The framework should load before the consumer.

## 3. Consumer C#

```csharp
using BannerlordHtmlUI;

protected override void OnSubModuleLoad()
{
    HtmlUiService.OnReady(() =>
    {
        HtmlUiService.Pages.Register(
            new HtmlUiPage("settings", "MyMod/settings.html")
            {
                HotReload = true,
                DefaultInputMode = HtmlUiInputMode.Captured
            });

        HtmlUiService.RegisterCommand("mymod.save", payload =>
        {
            // Bannerlord game logic runs on the framework's game-thread dispatch path.
            HtmlUiService.SendEvent("mymod.saved", new { ok = true });
        });

        HtmlUiService.RegisterRequest("mymod.getData", payload =>
        {
            // Return plain serializable data. Do not return Bannerlord engine objects.
            return Task.FromResult<object>(new { value = 123 });
        });
    });
}
```

Open later from a game-thread-safe location:

```csharp
HtmlUiService.Pages.Open("settings");
HtmlUiService.CaptureInput();
```

## 4. HTML/JS

```javascript
await game.call("mymod.save", {});
const data = await game.request("mymod.getData", {});
game.on("mymod.saved", value => console.log(value));
```

## 5. Rules

1. Only one BannerlordHtmlUI host should exist in the process.
2. Consumer Mods must not call `InitializeAsync`.
3. Consumer Mods should register through `OnReady`.
4. Page paths must remain inside the HTML UI root exposed to that consumer.
5. Request handlers should read/calculate Bannerlord data during their game-thread portion and return plain DTO-style data. Do not retain Bannerlord game objects across an `await`.
6. The framework owns WebView2, window handling, input modes, and browser-thread marshaling.
