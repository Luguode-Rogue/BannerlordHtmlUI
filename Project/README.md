# BannerlordHtmlUI — BUTR template-aligned project layout

This package follows the BUTR Team project structure shown by the user's real template.

For the authoritative resource-placement and deployment rules, see:

[`Project/BUTR_PROJECT_LAYOUT_RULES.md`](BUTR_PROJECT_LAYOUT_RULES.md)

That document is the single source of truth for:

- `_Module/` as the final Bannerlord Mod-root mirror
- Mod-root files versus assembly-relative runtime assets
- Framework `web/` deployment
- Consumer `UI/` deployment
- `Assembly.Location`-based runtime lookup
- `.csproj` Build/Deploy targets

## Project structure

At the Modules root:

```text
BannerlordHtmlUI/
├─ BannerlordHtmlUI.slnx
└─ BannerlordHtmlUI/
   ├─ BannerlordHtmlUI.csproj
   ├─ SubModule.cs
   ├─ _Module/
   ├─ src/
   ├─ web/
   ├─ docs/
   ├─ Properties/
   └─ .vscode/

HtmlUiConsumerTestMod/
├─ HtmlUiConsumerTestMod.slnx
└─ HtmlUiConsumerTestMod/
   ├─ HtmlUiConsumerTestMod.csproj
   ├─ SubModule.cs
   ├─ _Module/
   ├─ UI/
   ├─ ModuleData/Languages/
   ├─ Properties/
   └─ .vscode/
```

The package deliberately omits generated `bin` and `obj` folders.
`Bannerlord.BuildResources` is retained so the normal BUTR build/publish pipeline can create the module-level runtime output.

## Path questions

Do not create a second resource-placement rule here. When adding or moving a file, first determine its intended final Bannerlord path using `Project/BUTR_PROJECT_LAYOUT_RULES.md`, then verify the specific Consumer `.csproj` and `Assembly.Location` path resolution.

For example, a Consumer UI may be sourced from the project `UI/` directory and deployed beside the Consumer DLL under `bin/<GameBinariesFolder>/UI/`, while Mod-root files such as `SubModule.xml` or `ModuleData/*` belong to `_Module/` according to the canonical layout rules.

When debugging a path issue, compare:

```text
工程源路径
    ↓
构建/部署 Target
    ↓
Modules/<ModId>/最终路径
    ↓
代码实际读取路径
```

The exact placement rule is maintained only in `Project/BUTR_PROJECT_LAYOUT_RULES.md`.