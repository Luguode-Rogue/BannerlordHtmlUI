# Bannerlord 原生 Brush / Sprite → HtmlUI

## 目标

让 HtmlUI 尽量复用 Bannerlord 原有 Gauntlet UI 的视觉资源，而不是所有 HTML UI 都重新制作一套完全不同的皮肤。

Bannerlord 的 `UIContext` 提供 `GetBrush()`、`Brushes`、`BrushFactory`、`SpriteData`、`FontFactory` 等资源入口；`BrushLayer` 也提供 Sprite、Color、AlphaFactor、HueFactor、SaturationFactor、ValueFactor 等数据。具体 API 以目标 Bannerlord 版本为准。

当前原则：

```text
游戏 Brush / Sprite / Font
        ↓
BannerlordHtmlUI Bridge
        ↓
HTML / CSS / JS
```

而不是让 Consumer Mod 自己直接引用 WebView2 和 Gauntlet Renderer。

---

## 三阶段实现

### Phase 1：Brush Snapshot ✅

第一阶段已经完成并经过实机验证。

Framework 现在提供：

```javascript
app.request("framework.brush.context")
app.request("framework.brush.list", { filter, limit })
app.request("framework.brush.get", { name })
```

数据包含：

```text
Brush
├─ 字体信息
├─ 文字对齐
├─ Color / Alpha / HSV 因子
├─ FontColor / TextColor 因子
├─ Sprite 元数据
└─ Layers[] 元数据
```

内置验收页：

```text
F9
↓
Framework Brush Browser
```

已验证可以读取真实 Bannerlord Gauntlet `UIContext` 中的 Brush，例如 `ArmyManagement.Sort.ArrowBrush`，并得到真实 Layer / Sprite 元数据。

### Phase 2：Sprite Resource Bridge 🚧

核心桥接已经实现，等待下一轮实机验证。

实现方式：

```text
Brush.Sprite / SpritePart
        ↓
PlatformTexture
        ↓
EngineTexture.SaveToFile()
        ↓
%TEMP%/BannerlordHtmlUI/BrushCache
        ↓
Framework ContentRoot
        ↓
https://bannerlord-htmlui-framework-brush-cache.local/...
        ↓
HTML CSS background-image
```

`framework.brush.get` 在读取具体 Brush 时会附带：

```text
resourceUrl
sheetX / sheetY
sheetWidth / sheetHeight
width / height
UV
```

因此 HTML 可以从完整图集中裁剪出原版 Sprite。

内置 Brush Browser 已升级为直接显示原生 Sprite，并显示 Resource 状态。

详细用法见：

`docs/BRUSH_PHASE2_USAGE.md`

当前仍需要实机确认：

```text
EngineTexture.SaveToFile 是否在当前运行环境正常
生成 PNG 是否正确
WebView2 是否能从 Framework Brush ContentRoot 读取
Sprite 裁剪位置是否与原 Gauntlet 显示一致
```

### Phase 3：Native Brush Renderer ⏳

长期目标是允许 HTML 元素直接声明使用游戏 Brush：

```html
<bannerlord-brush
    brush="Basic.Button"
    state="Pressed">
</bannerlord-brush>
```

这要求 Framework 处理：

```text
HTML 元素
  ↕
Brush 状态
  ↕
Native Brush Renderer
```

这一阶段复杂度明显高于前两个阶段，因此不会提前耦合到 Framework 核心生命周期。

---

## 当前开发顺序

```text
1. UIContext / BrushFactory / SpriteData 来源确认          ✅
2. Brush Snapshot 数据结构                               ✅
3. framework.brush.get / list / context                  ✅
4. Framework Brush Browser                               ✅
5. 实机验证 Brush Snapshot                               ✅
6. Sprite Resource Bridge                                🚧
7. 实机验证原生 Sprite                                  ⏳
8. NinePatch / Layer 精确复现                            ⏳
9. Native Brush Renderer                                 ⏳
```

## 设计约束

### 不让 Consumer 直接依赖 WebView2

Consumer 应该只看到：

```text
app.request("framework.brush.get", ...)
```

而不是：

```text
CoreWebView2
CoreWebView2Controller
WebView2 WinForms
```

### 不复制游戏 UI 规则

Brush Bridge 只负责视觉资源。

```text
技能规则 / 数据 / Controller
        ↓
原 Mod

Brush / Sprite / Font
        ↓
Framework

HTML / CSS
        ↓
Consumer UI
```

### 版本兼容

Bannerlord 不同版本的 Gauntlet API 可能存在差异，因此 Brush Bridge 的版本适配集中在 Framework 内部，而不是把具体版本 API 暴露给 HTML。

---

## 现实预期

Brush Bridge 不意味着“任何 Gauntlet Widget 都会自动变成 HTML Widget”。

它解决的是：

```text
视觉资源复用
```

而不是：

```text
Widget / Layout / Binding / Input 自动转换
```

真正的 HtmlUI 界面仍然应该由 HTML / CSS / JS 设计；原生 Brush 作为可复用的 Bannerlord 视觉资产。

## 第一阶段使用文档

`docs/BRUSH_PHASE1_USAGE.md`

## 第二阶段使用文档

`docs/BRUSH_PHASE2_USAGE.md`
