# BannerlordHtmlUI 调试

本项目按 BUTR Bannerlord Module 模板组织。

## Visual Studio

选择：
- Platform: x64
- Target Framework: net472

然后通过项目的 `Properties/launchSettings.json` 使用 Bannerlord / BLSE Bannerlord 配置。

## VS Code

`.vscode/launch.json` 保留：
- Start Debugging [net472]
- Attach to Bannerlord [net472]
- Attach to BLSE Bannerlord [net472]
- net6 Xbox 条目（与 BUTR 模板一致）

## 注意

`bin/` 和 `obj/` 是构建产物，不应作为源码/版本控制内容。
