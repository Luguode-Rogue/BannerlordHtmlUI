# ROADMAP

## M0 工程基线
基本完成：BUTR layout、Debug config、两个独立 Mod。

## M1 Host / Lifecycle
已基本完成并实机验证：WebView2、Overlay、Open/Close/Reopen、Input、ConsumerScope。

## M2 Bridge
下一阶段：
- Command 完整验收
- Request/Response
- Event
- State
- Two-way binding
- 错误传播

## M3 Localization
当前阶段：
1. 验证 Bannerlord 原生字符串加载
2. 验证 `game.app.i18n.t()`
3. 验证 `data-bhui-i18n`
4. Fallback
5. 中文
6. 英文
7. 运行时语言变化

## M4 Developer Experience
- Golden Consumer Example
- API 文档
- Localization 文档
- Debugging
- 典型 UI 示例
- 更完整验收页

## M5 稳定性
- 正式 Framework OnShutdown
- 多 Page
- 多 Consumer
- ProcessFailed
- Reload
- ContentRoot 隔离
