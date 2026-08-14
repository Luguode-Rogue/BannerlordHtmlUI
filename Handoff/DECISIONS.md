# DECISIONS

1. Framework 与 Consumer TestMod 必须是两个独立 Mod。
2. BannerlordHtmlUI 只负责基础设施，不负责业务 UI。
3. ConsumerTestMod 是官方示例 + 回归验收工程。
4. 第三方 Mod 使用 HTML/CSS/JS 制作 UI。
5. C# 负责 Bannerlord 游戏逻辑；HTML/JS 负责 UI 与交互。
6. 本地化优先复用 Bannerlord 本体 Localization。
7. 测试页面默认使用中文。
8. Framework 正常运行禁止逐帧 Window Tracking 日志；需要诊断时再临时开启。
9. bin/obj 是构建产物，不作为源码交接内容。
10. 工程结构严格遵循用户实际提供的 BUTR 模板。
11. 解决方案路径：`Modules/<ModName>/<ModName>.slnx`。
12. 项目路径：`Modules/<ModName>/<ModName>/<ModName>.csproj`。
13. 不要擅自改成解决方案与项目同目录。
14. 不要擅自删除 net472/net6 模板目标。
15. Captured 模式下 Overlay 自己成为 foreground 不应触发隐藏。
16. Page Close 最终必须恢复 Hidden input。
17. HTML i18n 是覆盖默认文本，不是清空后等待翻译。
