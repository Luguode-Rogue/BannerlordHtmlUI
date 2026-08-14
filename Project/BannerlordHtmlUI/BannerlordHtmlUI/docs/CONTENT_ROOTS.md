# Content Roots

BannerlordHtmlUI is a separate framework module, but consumer Mods keep their own HTML/CSS/JS files. The framework therefore supports multiple mapped content roots.

## Register a consumer root

```csharp
var moduleDirectory = Path.GetDirectoryName(typeof(MySubModule).Assembly.Location);
var uiRoot = Path.Combine(moduleDirectory, "UI");
HtmlUiService.RegisterContentRoot("MyMod", uiRoot);

HtmlUiService.Pages.Register(
    new HtmlUiPage("main", "Main/index.html")
    {
        ContentRootId = "MyMod",
        HotReload = true
    });
```

The framework maps the root to its own local WebView2 virtual host. The page can use normal relative CSS/JS/image URLs within the root.

## Runtime API

`runtime.js` is automatically injected into every page by BannerlordHtmlUI. Consumer pages do **not** need to copy it or include a `<script src>` tag.

## Root rules

- A root must exist and be a directory.
- Page paths are relative to the selected root.
- `../` traversal is rejected.
- Top-level navigation is restricted to registered BannerlordHtmlUI local hosts.

`RegisterContentRoot()` completes the WebView2 mapping before it returns, so a page can be registered immediately afterward.
