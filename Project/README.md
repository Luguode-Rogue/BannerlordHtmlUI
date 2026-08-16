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
   ├─ web/              # Framework web source assets
   ├─ docs/
   ├─ Properties/
   └─ .vscode/

HtmlUiConsumerTestMod/
├─ HtmlUiConsumerTestMod.slnx
└─ HtmlUiConsumerTestMod/
   ├─ HtmlUiConsumerTestMod.csproj
   ├─ SubModule.cs
   ├─ _Module/SubModule.xml
   ├─ UI/                # Consumer UI source assets
   ├─ ModuleData/Languages/
   ├─ Properties/
   └─ .vscode/

The package deliberately omits generated `bin` and `obj` folders.
`Bannerlord.BuildResources` is retained so the normal BUTR build/publish pipeline can create the module-level runtime output.

## Runtime deployment layout — important

Do not infer the game's runtime asset paths from the repository source layout or from the default MSBuild output folder. The Bannerlord module has two different destinations:

```text
Modules/<ModId>/
├─ SubModule.xml
├─ ModuleData/
└─ bin/<GameBinariesFolder>/
   ├─ <ModId>.dll
   └─ web/ or UI/
```

### Framework

`BannerlordHtmlUI.SubModule` obtains the loaded Framework assembly directory from `Assembly.Location` and then resolves `web` beside that assembly. Therefore the actual runtime web root is:

```text
Modules/BannerlordHtmlUI/bin/<GameBinariesFolder>/web/
```

The Framework project explicitly deploys repository `web/` to that directory after the `net472` build. An old `Modules/BannerlordHtmlUI/web/` directory is not a valid runtime web root and is removed by the deployment target when present.

### Consumer TestMod

`HtmlUiConsumerTestMod.SubModule` obtains its own DLL directory from `Assembly.Location` and registers `<DLL directory>/UI` as the content root. Therefore the actual runtime UI root is:

```text
Modules/HtmlUiConsumerTestMod/bin/<GameBinariesFolder>/UI/
```

Consumer `ModuleData` is different: it belongs at the Mod root:

```text
Modules/HtmlUiConsumerTestMod/ModuleData/
```

The Consumer project explicitly deploys these assets to those destinations and removes the two legacy incorrect locations:

```text
Modules/HtmlUiConsumerTestMod/UI/
Modules/HtmlUiConsumerTestMod/bin/<GameBinariesFolder>/ModuleData/
```

### Build/deployment rule

For local Bannerlord development, the recommended flow is:

1. Build the `net472` target.
2. Let the project deployment targets copy runtime assets into the actual module layout.
3. Run Bannerlord from `BANNERLORD_GAME_DIR`.
4. Do not manually copy `web/`, `UI/`, or `ModuleData` into alternate locations.

When debugging path issues, verify the loaded DLL location first. Runtime content roots are derived from `Assembly.Location`, not from the repository directory.
