# BannerlordHtmlUI 本地化设计与使用

BannerlordHtmlUI 不维护自己的翻译数据库。网页 UI 的本地化以 Bannerlord 原生 Localization 为权威来源，Framework 负责把它安全地暴露给 HTML。

## 推荐目录

消费者 Mod 仍然使用 Bannerlord 自己的语言 XML：

```text
YourMod/
├── ModuleData/
│   └── Languages/
│       ├── strings_en.xml
│       └── strings_zh.xml
└── UI/
    ├── index.html
    ├── style.css
    └── app.js
```

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

解析顺序为：当前 Bannerlord 语言 → 指定 fallbackLanguage → English → Key 本身。缺失 Key 会写入 Framework 日志 WARN，但不会导致 UI 崩溃。

## 变量

Framework 当前提供 `{NAME}` 形式的变量替换，用于网页端显示本地化文本；游戏语言、语言列表、日期和时间格式仍由 Bannerlord 原生 Localization API 提供。Bannerlord 1.3.4 的 `LocalizedTextManager` 提供翻译查询、语言列表、语言标题及按语言格式化日期/时间等能力，`MBTextManager` 提供当前活动文本语言。 citeturn394715search0turn394715search2

## 设计原则

不要在网页中自己维护 `zh.json` / `en.json` 作为默认方案；不要根据 `navigator.language` 决定游戏 UI 语言；不要在 CSS `content:` 中放用户可见文字。
