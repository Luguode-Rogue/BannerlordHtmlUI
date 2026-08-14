# BannerlordHtmlUI — BUTR template-aligned project layout

This package follows the BUTR Team project structure shown by the user's real template.

At the Modules root:

BannerlordHtmlUI/
├─ BannerlordHtmlUI.slnx
└─ BannerlordHtmlUI/
   ├─ BannerlordHtmlUI.csproj
   ├─ SubModule.cs
   ├─ _Module/SubModule.xml
   ├─ src/              # Framework implementation classes except SubModule.cs
   ├─ web/
   ├─ docs/
   ├─ Properties/
   └─ .vscode/

HtmlUiConsumerTestMod/
├─ HtmlUiConsumerTestMod.slnx
└─ HtmlUiConsumerTestMod/
   ├─ HtmlUiConsumerTestMod.csproj
   ├─ SubModule.cs
   ├─ _Module/SubModule.xml
   ├─ UI/
   ├─ ModuleData/Languages/
   ├─ Properties/
   └─ .vscode/

The package deliberately omits generated `bin` and `obj` folders.
`Bannerlord.BuildResources` is retained so the normal BUTR build/publish pipeline can create the module-level runtime output.
