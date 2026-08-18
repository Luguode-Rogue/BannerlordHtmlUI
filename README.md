# BannerlordHtmlUI

BannerlordHtmlUI 是面向 **Mount & Blade II: Bannerlord Mod** 的 WebView2 HTML UI Framework。

它提供 WebView2 Host、Overlay、Page 生命周期、Input、State、Command、Request、Event、Binding、i18n、Diagnostics 等基础设施；Consumer Mod 保留自己的游戏业务逻辑与 HTML/CSS/JS。

## 当前开发入口

当前开发线：`dev`  
发布基线：`main`  
Framework 当前工程版本：`0.44.0`  
运行目标：`net472` / C# 10

### 推荐阅读顺序

1. [`Project/BannerlordHtmlUI/BannerlordHtmlUI/docs/README.md`](Project/BannerlordHtmlUI/BannerlordHtmlUI/docs/README.md) — 文档总入口
2. `docs/PROJECT_STATUS.md` — 当前真实状态与待验收项
3. `docs/ARCHITECTURE_MASTER.md` — 当前架构与线程边界
4. `docs/API.md` / `docs/DEVELOPMENT_GUIDE.md` — Framework 接入与公共 API
5. `docs/BUG_KNOWLEDGE_BASE.md` — 已解决问题、失败方案与快速排错
6. `docs/TESTING_AND_REGRESSION.md` — 实机回归与 StressLab

## 工程结构

```text
Project/
├─ BannerlordHtmlUI/          # Framework
└─ HtmlUiConsumerTestMod/     # Consumer / 实机验收 Mod
```

## 当前原则

- Consumer 不直接创建或管理 WebView2。
- `CoreWebView2` 访问必须遵守 WebView2 UI thread 边界。
- Bannerlord GameThread、WebView2 UI thread、Runtime JS 三个执行域必须明确区分。
- `HWND = 0` 不能直接等价为游戏关闭。
- F12 不作为可靠关闭协议；ESC 与页面 Close 是主要验收路径。
- 正常日志保持低噪声，不恢复逐帧 Window Tracking。
- Overlay / WebView2 窗口样式实验必须通过对应回归矩阵。
- 历史 Bug/Postmortem/Changelog 不因文档整理删除；统一入口只负责索引，原始排错过程继续保留。

项目完整文档、API 契约、测试方法和历史经验均以 `Project/BannerlordHtmlUI/BannerlordHtmlUI/docs/` 为当前知识入口。