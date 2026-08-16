# M3/M6 剩余专项验收测试

日期：2026-08-16
分支：dev

## 目的

StressLab 已扩展为剩余核心框架验收入口，覆盖 List、Template、Two-way、动态 DOM 的 declarative apply、i18n.bind、Locale 监视、Pagehide/Reload，以及已有压力测试。

## 测试原则

- `pass=true` 才算专项测试通过。
- Binder Dispose 不删除 Consumer 自己创建的 DOM；它应解除绑定并执行 child disposer。
- List/Template 测试使用 Framework Protocol 格式的 `state:` Event 注入来隔离并验收 JS Binding 语义；Bridge State/Event 链已经由高频 State/Event 250/250 实机验证。
- Dynamic DOM 测试明确验证“运行时创建 DOM + declarative apply()”，不宣称 MutationObserver 自动绑定。
- i18n.bind 自动验证当前语言；实际切换游戏语言仍需人工进行。

## 测试按钮

- List Binding：创建 A/B/C，更新为 A/C/D，再重排为 D/C/A；验证 key 复用、update、dispose。
- Template Binding：同上，但走 Template API。
- Two-way Binding：C# Name A -> DOM；DOM 输入 B -> C#；C# Name C -> DOM。
- Dynamic DOM / declarative apply：运行时创建 100 个带 `data-bhui-text` 的节点，调用 `bind.apply()`，验证全部同步。
- i18n.bind：验证 text/placeholder/title/alt 四种属性在当前语言下均正确解析。
- Locale change monitor：监听 10 秒 `localeChanged`，用于人工切换语言时验收事件；不会伪造语言切换。
- Pagehide / Reload binding：自动进行 3 次 reload，记录 pagehide、Ready、binding 和 diagnostics 基线。

## 通过标准

### List
`pass=true`，初始 3 child；第二阶段 3 child、B dispose、A/C update；第三阶段 key 顺序更新成功；Dispose 后调用者 DOM 保留。

### Template
同 List，但通过 template/update/dispose 路径。

### Two-way
A 初始化一致；DOM 输入 B 后 State=B；C# 再设 C 后 DOM=C。

### Dynamic DOM
100/100 节点通过 declarative `apply()` 同步；测试必须报告 `bindingMode=declarative apply()`。

### i18n
当前 locale 非空；text、placeholder、title、alt 全部等于对应 `i18n.t()` 结果。

### Reload
3 个 cycle 全部 Ready、bindingOk、snapshotOk；pagehideObserved=true。该测试作为页面销毁/重建基线，不代替完整宿主 F6 Lifecycle Stress。
