# M6 Host-side Lifecycle Stress

日期：2026-08-16
分支：`dev`

## 目的

在网页自身会被 Reload / Close 销毁的前提下，单纯依赖页面 JavaScript 无法可靠覆盖完整的 Open / Reload / Page Switch / Close 生命周期。

因此增加 Consumer 宿主侧压力控制器，由 Bannerlord `OnApplicationTick()` 驱动 Framework Page API。

## 入口

`HtmlUiConsumerTestMod`：

- `F6`：启动 20 轮生命周期压力测试。
- 再按 `F6`：停止当前压力测试。
- 不改变既有：
  - `F11` 普通 Test Page
  - `F12` 关闭
  - `F8` StressLab
  - `F7` 关闭当前页面

## 单轮流程

```text
Open Test Page
    ↓
Reload 当前 Page
    ↓
切换 StressLab Page
    ↓
CloseCurrent
    ↓
重新 Open Test Page
    ↓
CloseCurrent
    ↓
记录 PASS / FAIL
```

默认 20 轮，每一步之间留出固定 Tick 延迟，避免把所有操作压成同一帧。

## 记录指标

压力结束时记录：

- 总轮数
- PASS / FAIL
- 当前 Page
- `State.Count`
- `Pages.Count`

日志写入 Consumer TestMod 日志，不恢复高频逐帧日志。

## 验收标准

### PASS

- 20/20 轮完成。
- 每轮 Open / Reload / Page Switch / Close 均没有异常。
- 最终 `CurrentPage` 为 `<null>`。
- `Pages.Count` 与测试开始前一致。
- `State.Count` 不持续增长。

### FAIL

出现以下任一情况即判失败：

- `Pages.Open` 返回 false。
- Reload 在有效页面状态下被拒绝。
- 生命周期过程抛异常。
- 20 轮结束后残留打开页面。
- State/Page 注册数相对基线持续增长。

## 当前状态

代码入口已完成并直接进入 `dev`。

**尚未实机跑 20 轮，因此当前只能标记为“测试工具已就绪”，不能标记 M6 生命周期验收通过。**
