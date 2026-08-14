# DEBUGGING

使用 BUTR 模板的 `Properties/launchSettings.json` 和 `.vscode/launch.json`。

主要平台：
- x64
- net472

运行 profile：
- Bannerlord
- BLSE Bannerlord
- BLSE Bannerlord With Crash Reporter

`BANNERLORD_GAME_DIR` 由 BUTR/MSBuild 环境提供。

Framework 默认日志只记录关键状态、Warning、Error，不记录每帧 Window Tracking。
