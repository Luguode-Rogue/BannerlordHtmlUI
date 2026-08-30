# v0.44 发布与收口指南

## 发布前必须完成

- Public API / Protocol 语义冻结
- ESC UI-thread close 真实验收
- MainWindowHandle=0 不再误隐藏
- WebView2 ProcessFailed recovery / safe shutdown 明确并验证
- StressLab 基础与长时间压力测试
- Binding / i18n 生命周期专项验收
- Page / Reload / Navigation race 回归
- Owner Request cancellation 回归
- Overlay/WebView2 正常基线回归
- 发布版日志保持低噪声
- Framework / Consumer 文档同步

## 发布构建原则

- Framework 运行目标保持 `net472`。
- 不为了消除编译错误提高 LangVersion。
- Consumer 的测试 target 如无实际价值，在发布前评估是否删除。
- Debug/Test surface 与正常发布入口分离。

## 发布目录

必须以最终加载 DLL 的实际 `Assembly.Location` 验证 HTML 资源部署，不以源码目录猜路径。

## 发布后第一轮检查

```text
启动
→ Framework Ready
→ Consumer Register
→ F11
→ 完整显示
→ ESC
→ 再次 F11
→ F8 StressLab
```

## 版本文档

发布说明与历史版本变化统一进入 `CHANGELOG.md`，原始 `CHANGELOG_v*.md` 继续保留。
