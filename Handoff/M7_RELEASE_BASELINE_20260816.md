# M7 / v0.44 Release Baseline

日期：2026-08-16
主开发分支：`dev`
当前目标：把 BannerlordHtmlUI 从“功能完整”收口为“可稳定作为 Consumer Mod UI 框架使用”的 v0.44 基线。

## 1. Release Gate

### A. Framework Core
- [x] WebView2 初始化 / UI-thread boundary
- [x] Page / ContentRoot 安全边界
- [x] Request / Response / Event / State 基础协议
- [x] Request cancellation / AbortSignal
- [x] Runtime shutdown cancellation
- [x] ProcessFailed recovery 代码框架
- [x] HWND=0 tracking guard
- [x] Page Reload / HotReload transition lock

### B. Runtime / i18n
- [x] Runtime 单脚本 DocumentCreated 注入
- [x] Runtime Core / i18n 模块源码拆分
- [x] `game.app.i18n` 与 `game.i18n` 实例一致
- [x] Bannerlord Localization -> HTML 实机验证
- [x] ESC 宿主层输入路径

### C. Binding
- [x] State / text / value / checked / disabled / hidden / visible
- [x] two-way binding
- [x] debounce / throttle
- [x] keyed list / template / component 基础能力
- [x] dynamic DOM / MutationObserver lifecycle
- [x] Binder disposer
- [x] component/list/template child disposer ownership
- [ ] Language Switch 实机压力验证
- [ ] 大量 DOM Binding 实机压力验证
- [ ] Template/List 长时间运行验证

### D. Public API / Protocol
- [x] Protocol v1
- [x] Public API inventory
- [x] Stable Error Model
- [x] Ownership / disposer semantics documented
- [x] Request timeout / cancellation semantics documented
- [ ] Final v0.44 contract sign-off

### E. Consumer / StressLab
- [x] Consumer TestMod 基础 smoke
- [x] Diagnostics snapshot
- [x] StressLab Request / Cancellation / Component / DOM tests
- [x] StressLab 高频 State/Event 入口
- [x] StressLab 大量 DOM Binding 入口
- [x] Binder lifecycle test
- [x] Host-side F6 lifecycle stress controller
- [ ] Bannerlord 实机 F6 20-round pass
- [ ] StressLab long-run pass
- [ ] Leakage baseline freeze

### F. Release Hygiene
- [x] Framework 主日志默认低噪声
- [x] Window Tracking 不逐帧记录
- [x] 已知 Overlay rendering regression 有独立 postmortem
- [ ] 删除临时 debug hotkeys / 实验开关，或明确标记为 diagnostics-only
- [ ] README / Consumer integration guide 完整
- [ ] `.d.ts` 与最终 Runtime API 再做一次 release audit
- [ ] Release checklist 全部通过

## 2. Test Evidence Policy

没有 Bannerlord 实机证据的项目，不得标记为“已验证”。代码存在、StressLab 按钮存在、静态审计通过，只能标记为“已实现/待实机”。

实机测试结果至少应记录：

- Framework version
- Consumer version
- PageCount / StateCount / ContentRootCount
- BridgeCommandCount / BridgeRequestCount
- ActiveRequestCount
- F6 lifecycle rounds / pass / fail
- StressLab run count
- 是否出现 JS console error / C# ERROR
- Reload / Close / Reopen 最终状态

## 3. Release Definition

v0.44 只有在以下条件同时满足后才视为 Release Baseline：

1. Public API / Protocol 契约冻结；
2. Localization、Binding、Cancellation、Page lifecycle 有实机回归证据；
3. StressLab 长测没有注册数、ActiveRequestCount、PageCount、StateCount 持续增长；
4. F6 Host lifecycle stress 达到目标轮数且无异常；
5. 无已知阻塞级错误；
6. Release 默认日志保持低噪声；
7. Consumer 接入文档足以让独立 Mod 直接创建第一个 UI。

## 4. 当前结论

当前项目处于：`Release Candidate preparation`。

不是“功能还没做完”，而是“剩余主要工作集中在实机验收、压力基线、契约冻结与发布卫生”。
