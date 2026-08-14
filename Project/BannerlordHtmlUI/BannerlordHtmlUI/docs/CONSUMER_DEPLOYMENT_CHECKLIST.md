# Consumer Mod Deployment Checklist

The consumer Mod must not ship WebView2 itself. It depends on BannerlordHtmlUI for the browser host.

Required:
- BannerlordHtmlUI is installed as a separate dependency module.
- Consumer SubModule.xml declares `DependedModule Id="BannerlordHtmlUI"`.
- Consumer output contains only the consumer DLL, ModuleData, and its UI assets.
- Consumer must use only public BannerlordHtmlUI APIs and never access `HtmlUiHost` or WebView2 directly.
- The current public command/request callback payload type is `Newtonsoft.Json.Linq.JToken`; the reference consumer declares that dependency explicitly.

Do not copy Microsoft.Web.WebView2 into the consumer Mod.


## v0.37 runtime rule

Consumer Mods must not reference `System.Text.Json` for BannerlordHtmlUI Command/Request payloads. Use `Newtonsoft.Json.Linq.JToken` in handlers.
