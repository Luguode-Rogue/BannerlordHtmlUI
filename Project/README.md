# BannerlordHtmlUI — BUTR template-aligned project layout

This package follows the BUTR Team project structure shown by the user's real template.

At the Modules root:

BannerlordHtmlUI/
├─ BannerlordHtmlUI.slnx
└─ BannerlordHtmlUI/
   ├─ BannerlordHtmlUI.csproj
   ├─ SubModule.cs
   ├─ _Module/           # mirrors the final Bannerlord module root
   ├─ src/               # Framework implementation classes except SubModule.cs
   ├─ web/               # Framework web source assets
   ├─ docs/
   ├─ Properties/
   └─ .vscode/

HtmlUiConsumerTestMod/
├─ HtmlUiConsumerTestMod.slnx
└─ HtmlUiConsumerTestMod/
   ├─ HtmlUiConsumerTestMod.csproj
   ├─ SubModule.cs
   ├─ _Module/           # mirrors the final Bannerlord module root
   ├─ UI/                # Consumer UI source assets
   ├─ ModuleData/Languages/
   ├─ Properties/
   └─ .vscode/

The package deliberately omits generated `bin` and `obj` folders.
`Bannerlord.BuildResources` is retained so the normal BUTR build/publish pipeline can create the module-level runtime output.

## `_Module/` is the final Mod-root mirror

BUTR projects use `_Module/` as the project-side image of the final Bannerlord Mod directory.

The rule is simple:

```text
Project/_Module/<relative path>
        ↓ build/deploy
Modules/<ModId>/<relative path>
```

Therefore, any file that must exist directly under the installed Bannerlord module root should be placed in `_Module/` with the **same relative path** it should have after deployment.

For example, if the final Mod must contain:

```text
Modules/MyMod/
├─ SubModule.xml
├─ ModuleData/
│  └─ Languages/
│     └─ zh-CN.xml
└─ GUI/
   └─ Prefabs/
      └─ Example.xml
```

the project should contain:

```text
Project/MyMod/
└─ _Module/
   ├─ SubModule.xml
   ├─ ModuleData/
   │  └─ Languages/
   │     └─ zh-CN.xml
   └─ GUI/
      └─ Prefabs/
         └─ Example.xml
```

**Do not ask users to manually copy these files into the game's `Modules/<ModId>/` directory.** Put them under `_Module/` at the matching path and let the BUTR build/deployment pipeline handle the final Mod layout.

This rule applies to framework or consumer files that belong in the Mod root, such as `SubModule.xml`, `ModuleData/*`, `GUI/*`, language files, XML data, and other runtime assets. It does not mean source code or every project asset belongs in `_Module/`; use the file's intended final runtime location to decide.

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

1. Put Mod-root files under `_Module/` using the final Mod-relative path.
2. Build the project and let the BUTR/build deployment targets copy `_Module` and runtime assets into the actual module layout.
3. Run Bannerlord from `BANNERLORD_GAME_DIR`.
4. Do not manually copy `SubModule.xml`, `ModuleData`, `GUI`, language/XML data, or other Mod-root assets into alternate locations.

When debugging path issues, distinguish two questions:

- **Mod-root file:** check its `_Module/<relative path>` source location and final `Modules/<ModId>/<relative path>` deployment location.
- **Assembly-relative runtime asset:** verify the loaded DLL location first; Framework web content and Consumer UI are resolved from `Assembly.Location` as documented above.
