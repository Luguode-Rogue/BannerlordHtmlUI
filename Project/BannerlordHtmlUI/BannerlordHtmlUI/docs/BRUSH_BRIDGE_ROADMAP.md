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

### Phase 1：Brush Snapshot

先把原生 Brush 转换成稳定的 JSON 数据：

```json
{
  "name": "Basic.Button",
  "layers": [
    {
      "name": "Default",
      "color": "#ffffff",
      "colorFactor": 1,
      "alphaFactor": 1,
      "hueFactor": 0,
      "saturationFactor": 0,
      "valueFactor": 0,
      "sprite": "..."
    }
  ]
}
```

JS API 目标：

```javascript
const brush = await app.request("ui.getBrush", {
    name: "Basic.Button"
});
```

这一阶段只解决“读取和理解 Brush”，不直接在 WebView2 中执行 Bannerlord Renderer。

### Phase 2：Sprite / NinePatch Resource Bridge

把 Brush 中引用的 SpriteData 暴露成 HtmlUI 可以读取的资源：

```text
bannerlord://sprite/...
```

或由 Framework 提供等价的安全资源 URL。

目标：

```css
.native-button {
    background-image: url("bannerlord://sprite/...");
}
```

必要时同时支持 NinePatch 参数、Layer 顺序和颜色因子。

### Phase 3：Native Brush Renderer

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
1. 确认当前支持 Bannerlord 版本的 UIContext / BrushFactory / SpriteData 来源
2. 建立 BrushSnapshot 数据结构
3. 实现 ui.getBrush Request
4. Consumer TestMod 增加 Brush 浏览器
5. 实机验证 Basic.Button 等原版 Brush
6. 再做 Sprite Resource Bridge
7. 最后评估 Native Brush Renderer
```

## 设计约束

### 不让 Consumer 直接依赖 WebView2

Consumer 应该只看到：

```text
app.request("ui.getBrush", ...)
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

Bannerlord 不同版本的 Gauntlet API 可能存在差异，因此 Brush Bridge 的第一阶段应该集中在 Framework 内部做版本兼容，而不是把具体版本 API 暴露给 HTML。

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
