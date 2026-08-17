# BannerlordHtmlUI Phase 2：原生 Sprite Resource Bridge

## 目标

Phase 1 已经可以从 Bannerlord `Brush` 读取颜色、字体、Layer 和 Sprite 元数据。

Phase 2 继续向前一步：

```text
Bannerlord Brush
    ↓
Sprite / SpritePart
    ↓
Engine Texture
    ↓
Framework 缓存 PNG
    ↓
Framework ContentRoot
    ↓
WebView2
    ↓
HTML / CSS
```

Consumer Mod 不需要引用 WebView2、`EngineTexture` 或 Gauntlet Renderer。

---

## JS 侧调用

仍然使用 Framework Request：

```javascript
const result = await window.game.request("framework.brush.get", {
    name: "ArmyManagement.Sort.ArrowBrush"
});
```

返回的 `brush.sprite` 在可缓存时会包含：

```json
{
  "name": "StdAssets\\expanded",
  "width": 40,
  "height": 40,
  "resourceUrl": "https://bannerlord-htmlui-framework-brush-cache.local/sprite-....png",
  "sheetX": 120,
  "sheetY": 240,
  "sheetWidth": 4096,
  "sheetHeight": 4096
}
```

这些坐标来自 Bannerlord 的 `SpritePart`，因为一个 Sprite 往往只是大图集中的一个区域。

---

## HTML 如何显示

推荐使用 CSS background，而不是直接把图集当成 `<img>`：

```css
width: 40px;
height: 40px;
background-image: url("...");
background-size: 4096px 4096px;
background-position: -120px -240px;
background-repeat: no-repeat;
```

这样 HTML 最终看到的是 Sprite 的实际区域，而不是整张图集。

---

## 缓存机制

Framework 在首次读取 Sprite 时，将 Bannerlord Engine Texture 保存到：

```text
%TEMP%\BannerlordHtmlUI\BrushCache\
```

并通过 Framework 自己的 ContentRoot 映射为：

```text
https://bannerlord-htmlui-framework-brush-cache.local/
```

同一 Texture 在当前缓存身份下只保存一次。

---

## 为什么不是直接暴露 Engine Texture

不要在 Consumer 中做：

```csharp
TaleWorlds.Engine.Texture
TaleWorlds.Engine.GauntletUI.EngineTexture
CoreWebView2
```

这些全部属于 Framework 内部实现。

Consumer 应该只关心：

```javascript
resourceUrl
sheetX
sheetY
sheetWidth
sheetHeight
```

这样以后 Framework 可以把底层实现从 PNG 缓存换成真正的资源协议，而不需要修改 Consumer UI。

---

## 当前限制

Phase 2 当前实现的是：

```text
Sprite 图像资源访问
+
SpritePart 裁剪坐标
```

尚未实现：

```text
NinePatch 自动布局
Brush Layer 原生合成
Native Brush Renderer
```

这些属于后续工作。

---

## 当前验收

使用 Framework 自带：

```text
F9 → Bannerlord Brush Browser
```

选中 Brush 后应该看到：

```text
Brush Snapshot
Sprite Resource = 已缓存并可由 WebView2 读取
```

并且预览区域应该直接显示 Bannerlord 原来的 Sprite。
