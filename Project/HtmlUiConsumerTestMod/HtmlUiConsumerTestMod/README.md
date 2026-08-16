# HtmlUiConsumerTestMod v0.2

This is a standalone consumer Mod for BannerlordHtmlUI.

## Install

1. Ensure BannerlordHtmlUI is installed and enabled first.
2. Build this project against your existing Bannerlord development references.
3. On the normal `net472` build, the project automatically mirrors the source `UI/` directory into the actual Bannerlord module directory.
4. Enable both `BannerlordHtmlUI` and `HtmlUiConsumerTestMod`.

### Automatic UI deployment

The deployment is controlled by MSBuild properties in `HtmlUiConsumerTestMod.csproj`:

- `GameFolder` = `$(BANNERLORD_GAME_DIR)`
- `ConsumerModuleDeployDir` = `$(GameFolder)\Modules\$(ModuleId)`
- `ConsumerUiSourceDir` = `<project>\UI`
- `ConsumerUiDeployDir` = `$(ConsumerModuleDeployDir)\UI`

For a typical local installation:

`BANNERLORD_GAME_DIR = E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`

so the default UI destination is:

`E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\HtmlUiConsumerTestMod\UI`

All three deployment paths can be overridden from MSBuild without editing the project file. For example:

`/p:ConsumerModuleDeployDir=D:\Bannerlord\Modules\HtmlUiConsumerTestMod`

The copy runs only for `net472`, skips unchanged files, and is disabled for design-time builds.

## Test

- F11 opens the consumer HTML page.
- F12 closes it.
- `HtmlUiConsumerTestMod.log` is written beside the consumer DLL/module and records:
  - module load
  - Framework OnReady
  - content root registration
  - page registration
  - F11/F12 detection
  - page Open/Close result
  - registration failures

The consumer no longer uses System.Text.Json. It matches the current BannerlordHtmlUI JToken public API.
