# KNOWN ISSUES

## Localization
尚未完成 Bannerlord 实机验收：
- 本体字符串是否真正加载
- `game.app.i18n.t()` 是否返回正确文本
- 中文/英文
- Language Switch
- Missing Key fallback
- 本地化失败不得导致标题/按钮空白

## UI 输出路径
历史上 Consumer 实际运行 UI 位于：
`Modules\HtmlUiConsumerTestMod\bin\Win64_Shipping_Client\UI`
这是当前 BUTR BuildResources 发布结果，不要仅根据源码位置判断游戏加载路径。

## Shutdown
当前有防御式 ConsumerScope Dispose。未来可以增加正式 Framework OnShutdown，让 Consumer 在 Framework 关闭前主动清理。

## Bridge
Command / Request / Event / State / Two-way binding 尚未完成完整绿灯验收。
