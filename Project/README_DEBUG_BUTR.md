# BannerlordHtmlUI / Consumer TestMod — BUTR Layout

Both projects use the BUTR Team layout:

Modules/<ModName>/<ModName>.slnx
Modules/<ModName>/<ModName>/<ModName>.csproj

The package intentionally excludes generated `bin/` and `obj/` folders.

`Properties/launchSettings.json` and `.vscode/launch.json` are based on the
BUTR template supplied by the developer in this project.

For the current local game environment, configure `BANNERLORD_GAME_DIR`
according to the BUTR template / your existing VS environment.
