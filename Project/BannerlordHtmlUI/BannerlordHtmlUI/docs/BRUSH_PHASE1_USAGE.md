# Bannerlord Native Brush Bridge — Phase 1

## 目标

Phase 1 不尝试让 HTML 直接渲染 Bannerlord 的原生 `Brush`。

第一步先把游戏当前 `Gauntlet UIContext` 中的 Brush 读取成结构化快照，让 HTML 可以：

- 查询当前 UIContext
- 搜索 Brush 名称
- 查询单个 Brush
- 获取字体、颜色、Alpha、对齐、Sprite 元数据、Brush Layer 元数据

Bannerlord 的 `UIContext` 提供 `GetBrush()` 和 `Brushes`，`Brush` 本身提供字体、颜色、Sprite、Layers 等属性。参考 Bannerlord API：

- `TaleWorlds.GauntletUI.UIContext.GetBrush()` / `Brushes`
- `TaleWorlds.GauntletUI.Brush`
- `TaleWorlds.GauntletUI.BrushLayer`

## API

### `framework.brush.context`

```javascript
const result = await app.request("framework.brush.context");
```

返回类似：

```json
{
  "available": true,
  "contextName": "...",
  "brushCount": 1234
}
```

如果当前最顶层 Screen 没有可用 Gauntlet UIContext：

```json
{
  "available": false,
  "reason": "No active Gauntlet UIContext was found."
}
```

### `framework.brush.list`

```javascript
const result = await app.request("framework.brush.list", {
  filter: "Button",
  limit: 100
});
```

返回 Brush 列表以及每个 Brush 的第一阶段快照。

### `framework.brush.get`

```javascript
const result = await app.request("framework.brush.get", {
  name: "Basic.Button"
});
```

这是实际制作 HTML UI 时更重要的接口：先查原生 Brush，再决定如何把它映射到 HTML/CSS。

## 当前快照内容

当前第一阶段至少包含：

```text
Brush
├─ Name
├─ FontSize
├─ FontStyle
├─ TextHorizontalAlignment
├─ TextVerticalAlignment
├─ TransitionDuration
├─ Color / ColorFactor / AlphaFactor
├─ Hue / Saturation / Value
├─ FontColor / TextColorFactor / TextAlphaFactor
├─ Sprite
│  ├─ Name
│  ├─ Width
│  └─ Height
└─ Layers[]
   ├─ Name
   ├─ Hidden
   ├─ Color / ColorFactor / AlphaFactor
   ├─ Hue / Saturation / Value
   └─ Sprite
```

Sprite 目前只返回元数据，**没有把原始游戏纹理直接暴露给 WebView2**。

## Framework 自带验收页

Framework 自带：

```text
brush-browser
```

在游戏内按：

```text
F9
```

打开 Brush Browser。

Browser 可以：

1. 查询当前 UIContext。
2. 搜索 Brush。
3. 查看 Brush 快照。
4. 用 Brush 的颜色/字体信息做一个 HTML 近似预览。

这不是最终的“原生 Brush HTML 渲染器”，而是 Phase 1 的数据桥验收工具。

## 为什么先做 Snapshot

直接把 Gauntlet Brush Renderer 嵌进 WebView2 会同时引入：

- WebView2 合成
- TaleWorlds TwoDimension Renderer
- SpriteData / Texture 资源定位
- UI Context 生命周期
- DPI / Scale
- 状态动画
- 输入层

第一阶段先把“游戏 Brush 数据能否稳定进入 HtmlUI”单独验证，可以避免把资源渲染、生命周期和协议一次性绑死。

## 下一阶段

### Phase 2 — Sprite / Resource Bridge

目标：

```text
Brush
 ↓
Sprite
 ↓
Framework Resource URL
 ↓
HTML / CSS background-image
```

届时重点解决：

- Sprite 的实际纹理资源定位
- NinePatch / NineRegion
- WebView2 ResourceRequested
- Cache
- 生命周期

### Phase 3 — Native Brush Renderer

最终可以考虑：

```html
<bannerlord-brush brush="Basic.Button" state="Pressed"></bannerlord-brush>
```

由 Framework 负责调用原生 Brush/TwoDimension 渲染链。

Phase 3 不应在 Phase 1 尚未稳定前提前实现。
