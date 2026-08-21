# Architecture

本文件现在只作为旧入口兼容页，不再维护第二套架构描述。

当前真实架构规范：

- [`ARCHITECTURE_MASTER.md`](ARCHITECTURE_MASTER.md)：线程、生命周期、状态 owner、输入、Window、Runtime 总架构。
- [`FRAMEWORK_MODULE_MAP.md`](FRAMEWORK_MODULE_MAP.md)：每个 C# 模块的职责与禁止事项。
- [`CODE_PLACEMENT_RULES.md`](CODE_PLACEMENT_RULES.md)：修改代码时必须遵守的归属规则。
- [`API.md`](API.md)：Consumer 可使用的公共 API。
- [`BUG_KNOWLEDGE_BASE.md`](BUG_KNOWLEDGE_BASE.md)：历史 Bug、失败方案和定位入口。

## 最重要的三个边界

```text
WindowTracker
    = 窗口事实 / HWND / Bounds / Minimize / Foreground

InputController
    = Hidden / Passive / Captured / MouseCaptured

PageManager
    = Register / Open / Close / Reload / Navigation lifecycle
```

它们不得互相复制职责。

## 线程边界

```text
Bannerlord Game Thread
        ↓
Framework C#
        ↓ marshal
WebView2 UI Thread
        ↓
Chromium / JavaScript Runtime
```

`CoreWebView2` 只在 WebView2 UI thread 使用；Bannerlord Game API 只在 Game Thread 使用。

## Consumer 边界

Consumer 不创建 WebView2，不修改 Framework 私有窗口状态，不添加 Framework Harmony input/window workaround。

Consumer-specific 行为必须留在 Consumer。

## 历史复盘

旧 `Handoff/` 和历史 Changelog 继续保存完整排错过程；不要把历史文件重新当作当前架构规范。
