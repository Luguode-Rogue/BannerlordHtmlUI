# BannerlordHtmlUI dev 状态冻结：Native Brush / Sprite 实验

> 冻结日期：2026-08-18
> 来源分支：`dev`
> 冻结提交：`142aedcd2c5476f27c4dc0f1cf0d501ca1b045c9`
> 保存分支：`archive/dev-native-brush-20260818`

## 当前总体状态

BannerlordHtmlUI 主体框架可继续使用，WebView2、页面管理、输入/生命周期、HTMLUI 消费者模式等主线能力保持可开发。

本文件保存的是 2026-08-18 时 `dev` 上的 Native Brush / Sprite 研究工作，不代表该实验功能可用于正式生产。

## 已完成

- 已建立 Brush Browser，可读取并展示大量原生 Brush 的结构信息。
- 已读取 Brush 的字体、颜色、状态、Sprite 名称、Sprite 尺寸、UV、Atlas 坐标等信息。
- 已实现 Legacy Sprite Resource 研究链路。
- 已尝试从运行时 `Texture` / `PlatformTexture` / `EngineTexture` 获取像素并生成 WebView2 可读取的 PNG。
- 已尝试多种像素通道、裁剪、纹理导出及运行时 Texture 路线。
- 已增加 Native Atlas Asset 实验服务与独立 Native Asset Diagnostics 页面。
- 已研究 Native `AssetPackages`、`EmAssetPackages`、`ui_group1_*` 等原生资源定位路线。
- 已尝试通过 TpacTool 作为外部原生 Atlas 解包路径。
- 已处理过 WebView2 Overlay 的 ALT+TAB / TopMost 问题：Overlay 不再使用全局 TopMost，而是绑定 Bannerlord 主窗口层级。

## 当前失败点

### 1. 原生 Sprite 图片无法可靠取得

当前从运行时 Texture 得到的图像在实机上与 Bannerlord 原生 UI 视觉明显不一致。

典型结果：

- Pixel diagnostics 显示异常低的颜色值。
- Atlas / Sprite crop 与预期按钮图像不一致。
- `Attribute.Close.Button` 无法得到肉眼可确认的原版关闭按钮。

### 2. Native Atlas 路线未完成

目前 Native Atlas Asset Probe 仍可能返回：

- `missing`
- `provider=none`
- TpacTool DLL 缺失

因此尚未成功完成：

`AssetPackages / TPAC -> 正确 Atlas -> SpriteData 精确裁剪 -> 正确 PNG`

### 3. Native Asset Diagnostics 仍属于实验工具

F8 诊断页曾出现页面可以导航但诊断请求链不稳定的问题，因此该诊断功能不应视为 Framework 核心能力。

## 重要结论：该功能难以实现

当前实验已经验证：

> "HTMLUI 直接取得 Bannerlord 运行时原生 Brush 的真实 Sprite 像素，并将其无损转换成 WebView2 可用图片"

不是一个适合继续在 Framework 主线上投入的普通功能。

原因主要在于 Bannerlord 的 TwoDimension / Gauntlet UI 资源属于游戏自己的 Atlas / TPAC / Runtime Texture 体系。虽然 Brush 元数据和 SpriteData 很容易读取，但从运行时 GPU/UI 资源反向得到可靠的 PNG 并不稳定。

已经尝试过的方向包括：

- `Texture.GetPixelData`
- RGBA / BGRA / ARGB 变体
- UV / Sheet 坐标裁剪
- `PlatformTexture`
- `EngineTexture`
- `PreloadTexture`
- `SaveToFile`
- Native Atlas 定位
- TPAC / TpacTool 路线
- AssetPackages / EmAssetPackages 搜索

截至冻结时仍未获得一张通过视觉验收的原生 Sprite。

## 建议的后续策略

### 当前不继续作为主线开发

不要继续围绕：

`GPU Texture -> PixelData -> PNG`

或

`Brush -> Runtime Texture -> WebView2`

进行无限试错。

### 可保留的研究方向

后续仅在出现可靠的以下条件之一时再恢复：

1. 找到稳定可调用的 Bannerlord 原生 Atlas/TPAC 解包能力。
2. 找到明确可用的原生 Sprite -> 文件/Bitmap API。
3. 能以成熟库直接解析当前 Bannerlord 版本的 Native AssetPackages。

### HTMLUI 的正式路线

正式开发应优先：

- HTML/CSS/JS 自己实现 UI。
- Brush 元数据仅用于风格参考。
- 必要时人工重建原版按钮/面板视觉。
- 真正需要 1:1 原生资源时，再考虑保留 Native Widget/Brush 混合方案。

## 分支用途

本分支是实验冻结档，不再作为普通开发分支使用。

主要用途：

- 保存当前 Native Brush 实验代码
- 保存失败路线和诊断工具
- 后续若重新研究 Native Atlas，可直接从这里恢复

## 与当前 `dev` 的关系

冻结完成后，原 `dev` 应恢复到 `main`，保持干净。

后续新的 HtmlUI Framework 开发应从干净的 `dev` 重新开始；如未来要继续 Native Brush 实验，则从本分支恢复。 
