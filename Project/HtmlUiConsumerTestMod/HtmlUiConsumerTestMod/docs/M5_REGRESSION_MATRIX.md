# M5 Consumer Regression Matrix

## 目的

把 HtmlUiConsumerTestMod 作为 BannerlordHtmlUI 的稳定回归入口。默认不要求每次开发都进行全量人工测试；代码侧先保证每项都有可重复触发入口。

## 当前入口

| 项目 | TestMod 入口 | 预期 |
|---|---|---|
| Page Open / Close | F11 / F12 | Open/Close 事件各一次，无重复回调 |
| State | Increment / Name / Enabled | State 更新立即到达页面 |
| Event | Event | C# → JS 单次事件到达 |
| Request | 普通异步 Request | 正常返回，不残留 pending |
| Cancellation | 可取消 Request | Abort 后 `REQUEST_ABORTED`，C# handler 收到取消，不出现晚到成功结果 |
| Command Error | Command 异常 | 获得稳定错误对象 |
| Request Error | Request 异常 | 获得稳定错误对象 |
| Diagnostics | Diagnostics Snapshot | 返回当前运行态快照 |
| Navigation Race | Rapid Open/Reload Race | 旧导航不得覆盖当前导航状态 |
| Binding | Name / Enabled | State → DOM / DOM → Command 正常 |
| i18n | 页面初始 bind / pagehide | 页面切换后无残留 binding |
| Pressure | F8 → StressLab，Run 1/10/50 | 结束后无未处理异常；注册数与 ActiveRequestCount 回到基线，Component host 全部清除 |

### StressLab 压力模型

每轮压力测试包含：

- 100 个独立 DOM host。
- 50 个独立 `binder.component()` host；每个组件单独挂载，避免多个组件共享一个 DOM root 造成测试假阳性。
- 20 个普通 Request。
- 20 个 `requestCancellable()`，在短延迟后批量 Abort。
- 开始/结束各读取一次 Diagnostics。
- 单次 StressLab 运行具有互斥锁，重复点击不会并发启动第二个压力循环。
- `Stop` / `pagehide` 会停止下一轮循环；当前轮已经创建的 Promise 仍由自己的 finally/cancellation 路径负责收尾。
- 每轮结束会清空 StressLab 的 benchmark DOM。

## Diagnostics 指标语义

`BridgeCommandCount` / `BridgeRequestCount` 表示当前 **注册项数量**，不是“正在执行或等待中的 Request 数量”。

`ActiveRequestCount` 表示当前仍持有 `CancellationTokenSource` 的 Request 数量。

因此：

- 注册数稳定只能证明 Command / Request 注册没有持续增长。
- `ActiveRequestCount` 回到基线，才可以证明这批 Request 已经退出 Bridge 的活动集合。
- Runtime shutdown 触发取消后，最终 `ActiveRequestCount` 必须回到 `0`；不能把“调用 Cancel”本身误判为“已经退出执行”。

## 必须保持的自动清理

- Page close / pagehide 必须结束页面自己的 Request、Event、State、Binding 生命周期。
- `HtmlUiConsumerScope.Dispose()` 必须按 Owner 清理 Page、Command、Request、State、ContentRoot。
- Request cancellation 必须支持执行前、执行中和 runtime shutdown。
- Navigation Guard 不得强引用旧 Host。

## 实机测试触发条件

以下情况才要求人工进入游戏：

1. WebView2 / Overlay 可见性或输入行为发生改动。
2. Navigation Guard、Cancellation 或 Binding patch 的运行时行为发生改动。
3. Bannerlord 版本或 WebView2 Runtime 发生变化。
4. 压力测试需要验证长时间运行后的实际资源回落。

## 通过标准

单项测试不得出现：

- 未捕获 JS Error。
- C# Request 永久 pending。
- 页面关闭后旧 Event / State / Binding 继续更新新页面。
- 重复 Open/Close 产生重复生命周期回调。
- Rapid Navigation 的旧完成事件覆盖当前页面状态。
- Cancellation 后出现成功结果或未处理的 late response。
- 压力测试结束后注册项持续增长。
- 压力测试结束后 `ActiveRequestCount` 高于开始基线。
- StressLab 结束后 benchmark DOM 没有回到预期数量。

## 日志原则

Consumer TestMod 允许记录操作级日志；BannerlordHtmlUI Framework 默认保持低噪声。禁止恢复逐帧 Window Tracking、Render Geometry 等高频日志，除非正在针对具体问题临时开启。
