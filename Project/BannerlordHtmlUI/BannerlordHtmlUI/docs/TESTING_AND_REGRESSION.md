# 测试与回归总入口

## 1. 测试层级

### Smoke

验证 Framework 能启动、WebView2 Ready、Consumer Register、Page Open/Close。

### Lifecycle

验证：

```text
Open → Close → Open
Open → Reload
Open → ESC
Pagehide → Reopen
Framework Shutdown
```

### API

覆盖：

- Command
- Request / Response
- Cancellable Request
- Event
- State set/remove
- Binding
- Component
- i18n

### Stress

使用 Consumer TestMod 的 `StressLab`：

- DOM node stress
- Component
- Request
- Cancellable Request
- Diagnostics baseline
- 多轮运行

### Release

验证 v0.44 Public API、日志、Overlay、线程边界、资源部署和 Consumer 文档。

## 2. 当前快捷键

- `F11`：打开普通 Test 页面
- `F8`：打开 StressLab
- `F7`：关闭当前页面
- `ESC`：主要关闭路径
- F12：不作为可靠测试条件

## 3. 关键回归

### Page lifecycle

```text
F11
等待页面完整显示
ESC
确认 currentPage=<null>
确认 inputMode=Hidden
确认 hostVisible=False
F11 再次打开
```

### Navigation race

快速：

```text
Open
Reload
Close
Open
Reload
```

要求旧 Navigation 结果不能覆盖最新页面状态。

### Request cancellation

测试：

```text
正常完成
取消前
执行中 Abort
Timeout
pagehide
runtime shutdown
```

最终 `ActiveRequestCount` 应回到基线。

### Owner Dispose

ConsumerScope Dispose 后：

- owned Page 不再可用
- owned Command/Request 不再接收请求
- owned State 正确清理
- active Request 最终结束
- 旧异步结果不能写回新 Owner

### Overlay

只在修改 Overlay/WebView2/窗口样式时复测：

- HTML 可见
- 游戏画面不异常
- Captured 不闪烁
- 点击区域与视觉一致
- `debug/test-root-transparent` 基线不回归

### ESC

必须看到：

```text
ESC filter installed
Escape detected
CloseCurrent
currentPage=<null>
inputMode=Hidden
hostVisible=False
```

## 4. Diagnostics 重点

检查：

- PageCount
- StateCount
- ContentRootCount
- BridgeCommandCount
- BridgeRequestCount
- ActiveRequestCount
- NavigationInProgress
- CurrentPageOwner
- CurrentPagePath

不要用逐帧日志代替这些诊断数据。

## 5. 长时间压力

建议至少覆盖：

```text
StressLab Run 10
StressLab Run 50
多次 Open/Close
多次 Reload
Binding + Component 长时间运行
Cancellation 高频运行
```

比较运行前后的：

```text
PageCount
StateCount
Bridge registrations
ActiveRequestCount
DOM child count
```

## 6. 完成条件

一个修改只有在与修改范围相关的回归通过后才认为完成。

不要求每次改动都运行整套压力矩阵；但涉及生命周期、Overlay、Input、Bridge、Binding、i18n 的结构性修改必须运行对应专项测试。
