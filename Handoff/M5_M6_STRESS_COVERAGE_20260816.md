# M5 / M6 Stress Coverage

日期：2026-08-16
分支：`feature/framework-finalization-m3-m4`

## 本轮目标

继续把 Framework 从“功能可用”推进到“可长期运行、可作为真实 Consumer UI 基础设施”的状态。

## 已新增的 StressLab 覆盖

### 1. 高频 State / Event

StressLab 新增：`高频 State/Event`

测试路径：

```text
StressLab
  ↓
250 次 game.call('increment')
  ↓
Consumer State: counter 高频更新
  ↓
Consumer Event: counterChanged 高频广播
  ↓
检查事件计数
  ↓
检查 ActiveRequestCount 基线
```

这个测试复用 Consumer 已存在的 `increment` Command，因此同时覆盖 GameThread dispatcher、StateStore 变更通知和 C# → JS Event。

### 2. 大量 DOM State Binding

StressLab 新增：`大量 DOM Binding`

默认创建 500 个 `game.app.bind.text()` binding，并执行 25 次 State 更新，随后：

- 检查 500 个节点是否全部更新到最新 State。
- 调用 Binder Dispose。
- 清理测试 DOM。

此测试用于发现：

- State listener 累积
- Binding dispose 不完整
- 高频 state → DOM 写回异常
- Binder 在大量节点下的更新成本

### 3. Binder 生命周期

StressLab 已有独立：`Binder 生命周期`

覆盖：

- `component()` disposer
- `list()` disposer
- `template()` disposer
- Binder Dispose 后 child DOM / disposer 是否释放

## 当前仍需宿主侧压力测试

### Rapid Page / Reload / Close

不在当前 StressLab 页面内部实现自动 Reload 循环。

原因：页面 Reload 会主动销毁当前 JS Runtime，页面自身无法可靠地在一次测试中继续驱动 Reload 后半程。因此该场景应由 C# / Framework Host 层驱动。

目标矩阵：

```text
Consumer Test
    ↕
Consumer StressLab
    ↕
Open / CloseCurrent
    ↕
Reload
    ↕
Rapid Open
    ↕
Rapid Reload
    ↕
多 Page 交替
```

下一阶段将增加独立 Host-side Stress Controller，不把生命周期状态机塞进 Consumer UI。

## 当前验收原则

测试工具存在 ≠ 测试通过。

本文件新增的高频 State/Event、500 Binding 和 Binder 生命周期目前均标记为“有自动化入口，待 Bannerlord 实机验证”。

只有实机运行结果记录后，M5/M6 才能将对应项目从 `[ ]` 改为 `[x]`。
