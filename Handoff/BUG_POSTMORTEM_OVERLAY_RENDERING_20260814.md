# Overlay/WebView2 渲染不可见 Bug 复盘（2026-08-14）

## 现象

WebView2 页面能够：
- 成功注册
- 成功导航
- OverlayForm 实际创建并铺满 1707×960
- WebView2 子窗口存在且尺寸正确
- Captured 输入仍然生效
- 页面按钮对应位置可以点击

但用户看不到 HTML 页面内容。该现象在多个实验版本中重复出现，因此不能简单归因于页面地址、ContentRoot 或 HTML 导航失败。

典型日志同时出现：

```text
Navigation completed: success=True
formVisible=True
requestedVisible=True
inputMode=Captured
```

并且窗口树可以看到：

```text
Chrome_WidgetWin_0
Chrome_WidgetWin_1
Chrome_RenderWidgetHostHWND
Intermediate D3D Window
```

其中相关子窗口通常都是完整尺寸。

## 最重要的实机事实

### 1. "看不见，但能点" 是核心症状

页面内容不可见并不等于 WebView2 没有工作。用户可以点击应该存在按钮的位置，说明：

- 页面导航成功
- Chromium DOM/输入链路至少部分工作
- Overlay 命中区域存在
- 问题主要位于渲染/合成/子窗口显示链，而不是页面注册本身

### 2. 已验证的正常版本

以下历史/二分版本曾实机正常显示：

- `BannerlordHtmlUI-debug-bisect-1e5ff1c`
- `5775369`
- `debug/bisect-5155-no-child-style`
- 后续基于该路线的版本
- 当前用于继续开发的正常基线：`debug/test-root-transparent`

因此后续排查必须优先以这些已验证版本为基线，不应重新引入已经证明会破坏显示的子窗口样式修改。

## 已验证的错误方向

### A. 对 `Chrome_RenderWidgetHostHWND` 设置 `WS_EX_TRANSPARENT`

实验提交：

- `3f836d4db2d659a17a7fae6b67549a145dcc57dc`
- `c5d161d44d4e9f8f6b3caa47dd743b6595237194`

实验内容：将 `Chrome_RenderWidgetHostHWND` 的扩展窗口样式加入/移除 `WS_EX_TRANSPARENT`，尝试让渲染宿主窗口鼠标穿透。

结果：重新出现与历史问题相同的“页面不可见”现象。该方向视为错误。

**结论：禁止默认修改 `Chrome_RenderWidgetHostHWND` 的扩展窗口样式。**

### B. 不要把“输入穿透”和“渲染穿透”混为一谈

`WS_EX_TRANSPARENT` 是 Win32 窗口样式，不是 WebView2 的正常输入路由 API。它改变的是窗口系统层面的绘制/命中行为；对于 Chromium/WebView2 的子窗口层，贸然修改可能影响最终合成结果。

当前项目已经证明：

- Overlay 本身可以通过 `WS_EX_NOACTIVATE`、`WM_NCHITTEST/HTTRANSPARENT` 等方式处理输入模式。
- 不能因此推导出“WebView2 内部任意子窗口都可以加 `WS_EX_TRANSPARENT`”。

## D3D 子窗口实验

`Intermediate D3D Window` 曾被用于 A/B 测试。相关实验主要用于确认 WebView2 GPU/D3D 合成链是否与输入穿透有关。

重要的是：该实验应当视为**诊断手段**，不是正式架构方案。

后续修改 Overlay 渲染路径前，必须：

1. 记录原始扩展样式。
2. 单变量 A/B。
3. 每次只修改一个明确窗口节点。
4. 测试完成后恢复到已验证正常基线。
5. 不保留无必要的子窗口样式修改。

## 日志要求

正式版本不要恢复高频逐帧：

```text
Window tracking: ...
```

这类日志会快速膨胀并妨碍问题分析。

保留：
- Framework 初始化/失败
- Page Open / Close / Navigate
- 输入模式切换的关键状态
- Navigation completed
- 真正发生的 A/B 实验结果
- JS runtime error
- Shutdown / ProcessFailed 等异常生命周期事件

A/B 日志应该只在状态实际变化时输出，不应每帧输出。

## 当前基线与规则

### 正常基线

`debug/test-root-transparent`

用户已实机确认该路线正常显示。

### 后续开发规则

- Overlay / Input 生命周期不要重做。
- 不再默认修改 Chromium 子窗口扩展样式。
- 不再根据“地址不对”解释已经出现的可点击但不可见现象；只有出现明确导航/资源错误时才检查地址。
- 所有新的渲染实验必须从已验证正常基线派生，并保证可以单变量回退。

## 根因状态

**目前不是“WebView2 地址错误”或“页面没加载”的证据链。**

目前最可靠的结论是：

> 问题发生在 Overlay + WebView2/Chromium 子窗口的渲染/合成层；对 Chromium 子窗口设置不合适的 Win32 extended style 可以直接复现不可见问题。

尚未把最终底层 Windows/Chromium 合成根因精确到单个内部机制，因此不得在代码注释或文档中声称已经找到了唯一最终根因。
