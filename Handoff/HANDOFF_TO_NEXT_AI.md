# HANDOFF TO NEXT AI

请先阅读本目录中的：
- README.md
- PROJECT_STATUS.md
- ARCHITECTURE.md
- DECISIONS.md
- KNOWN_ISSUES.md
- TEST_STATUS.md
- ROADMAP.md

然后阅读两个工程。

## 不要
- 不要重做 BUTR 目录结构
- 不要合并两个 Mod
- 不要随意删除 net472/net6
- 不要擅自修改已经通过的 Overlay / Input 生命周期
- 不要默认建立独立 JSON 本地化系统
- 不要重新开启逐帧 Window Tracking
- 不要根据源码目录猜测实际游戏 UI 输出位置

## 当前优先级
1. Localization 实机验收
2. Command / Request / Event / State 完整验收
3. Two-way binding
4. 正式 OnShutdown
5. API / Example 文档完善

## 重要
用户使用 BUTR Team Bannerlord Module 模板。
工程结构是：
`Modules/<ModName>/<ModName>.slnx`
和
`Modules/<ModName>/<ModName>/<ModName>.csproj`

当前用户主要使用 net472 游戏运行环境。
测试页面默认中文。
