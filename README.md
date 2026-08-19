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
2. [`Project/BUTR_PROJECT_LAYOUT_RULES.md`](Project/BUTR_PROJECT_LAYOUT_RULES.md) — **工程资源放置、Mod-root 与程序集旁运行时资源的统一规则**
3. `docs/PROJECT_STATUS.md` — 当前真实状态与待验收项
4. `docs/ARCHITECTURE_MASTER.md` — 当前架构与线程边界
5. `docs/API.md` / `docs/DEVELOPMENT_GUIDE.md` — Framework 接入与公共 API
6. `docs/BUG_KNOWLEDGE_BASE.md` — 已解决问题、失败方案与快速排错
7. `docs/TESTING_AND_REGRESSION.md` — 实机回归与 StressLab

## 工程结构

```text
Project/
├─ BannerlordHtmlUI/          # Framework
│  └─ BannerlordHtmlUI/
│     └─ _Module/             # 最终 Framework Mod 根目录的工程镜像
└─ HtmlUiConsumerTestMod/     # Consumer / 实机验收 Mod
   └─ HtmlUiConsumerTestMod/
      └─ _Module/             # 最终 Consumer Mod 根目录的工程镜像
```

## 资源放置规则

工程资源的最终 Bannerlord 部署位置、`_Module` 规则、程序集旁 `bin/<GameBinariesFolder>/` 资源、Consumer UI、Framework `web/` 以及构建/部署映射，统一以：

[`Project/BUTR_PROJECT_LAYOUT_RULES.md`](Project/BUTR_PROJECT_LAYOUT_RULES.md)

为准。

其他文档不得另行维护一套相互独立的“资源应该放哪里”规则；发现路径问题时，应回到该文档，再结合具体 Consumer 的 `.csproj` 与 `Assembly.Location` 验证实际部署路径。

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