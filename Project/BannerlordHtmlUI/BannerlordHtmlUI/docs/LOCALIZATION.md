# BannerlordHtmlUI 本地化设计与使用

BannerlordHtmlUI 不维护自己的翻译数据库。网页 UI 的本地化以 Bannerlord 原生 Localization 为权威来源，Framework 负责把它安全地暴露给 HTML。

## 标准目录

Consumer Mod 的语言资源必须遵循 Bannerlord 的 `LanguageData` 结构。不要只把 `strings_zh.xml` / `strings_en.xml` 平铺在 `ModuleData/Languages/` 下作为唯一语言定义。

```text
YourMod/
├── ModuleData/
│   └── Languages/
│       ├── EN/
│       │   ├── language_data.xml
│       │   └── strings.xml
│       └── CNs/
│           ├── language_data.xml
│           └── strings.xml
└── UI/
    ├── index.html
    ├── style.css
    └── app.js
```

Bannerlord 使用 `language_data.xml` 注册语言，并通过 `LanguageFile xml_path="..."` 指向对应字符串文件。`CNs` 是简体中文目录代码，语言 ID/名称应使用游戏实际的原生名称 `简体中文`。这种结构与 BUTR 项目及 Bannerlord 的 `ModuleLanguageData` / `ModuleLanguage` schema 一致。citeturn724174search3turn724174search4turn724174search12

### `language_data.xml` 示例

English：

```xml
<LanguageData id="English"
              name="English"
              subtitle_extension="en-GB"
              supported_iso="en-GB,en-US,en,eng,en-us,en-gb"
              under_development="false">
  <LanguageFile xml_path="EN/strings.xml" />
</LanguageData>
```

简体中文：

```xml
<LanguageData id="简体中文"
              name="简体中文"
              subtitle_extension="zh-CN"
              supported_iso="zh-CN,zh-Hans,zh-cn,zh-hans"
              under_development="false">
  <LanguageFile xml_path="CNs/strings.xml" />
</LanguageData>
```

### 字符串 XML

字符串文件保持 Bannerlord 原生 `<base> / <tags> / <strings>` 结构：

```xml
<base type="string">
  <tags>
    <tag language="简体中文" />
  </tags>
  <strings>
    <string id="MyMod_Title" text="我的界面" />
  </strings>
</base>
```

`id` 必须稳定且唯一；翻译只修改 `text`。变量如 `{COUNT}` 必须保留。citeturn761401search0turn724174search11

## JavaScript

```javascript
const app = game.app;

const title = await app.i18n.t('MyMod_Title');
const text = await app.i18n.t('MyMod_Count', { COUNT: 10 });

const languages = await app.i18n.getLanguages();
const locale = await app.i18n.getLocale();

app.i18n.onLocaleChanged(language => {
    // Bannerlord 的活动文本语言发生变化后触发
});
```

## 声明式 HTML

```html
<h1 data-bhui-i18n="MyMod_Title"></h1>
<button data-bhui-i18n="MyMod_Equip"></button>
<input data-bhui-i18n-placeholder="MyMod_Search">
```

初始化后执行：

```javascript
await app.i18n.bind();
```

语言变化时可以再次调用 `bind()` 刷新文本。

## Fallback

解析顺序为：当前 Bannerlord 语言 → 指定 `fallbackLanguage` → English → Key 本身。缺失 Key 会写入 Framework 日志 WARN，但不会导致 UI 崩溃。

因此，测试页面如果直接显示 `HtmlUiConsumer_Title` 之类的 Key，优先检查 `ModuleData/Languages/<CODE>/language_data.xml` 是否存在、`LanguageFile` 路径是否正确，以及整个 `Languages` 目录是否真的部署到了游戏 Mod 根目录。

## Framework 部分

Framework 的 `HtmlUiLocalization` 直接使用 Bannerlord 的 `LocalizedTextManager` 查询字符串，并把当前 `MBTextManager.ActiveTextLanguage` 从显示名称映射到 Bannerlord 的语言 ID。网页端不应自己维护第二套翻译数据库。citeturn724174search12

## 设计原则

不要在网页中自己维护 `zh.json` / `en.json` 作为默认方案；不要根据 `navigator.language` 决定游戏 UI 语言；不要在 CSS `content:` 中放用户可见文字；不要把 `ModuleData/Languages` 当成普通构建输出目录。
