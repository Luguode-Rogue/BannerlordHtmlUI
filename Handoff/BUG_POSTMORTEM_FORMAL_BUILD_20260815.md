# 正式环境编译故障复盘（2026-08-15）

## 现象

将当前开发版本放入正式环境编译/构建时，出现大量 C# 语法错误，错误码包括：

`CS1519, CS8803, CS0710, CS0650, CS1003, CS1525, CS0102, CS1011, CS0246, CS1660, CS1520, CS0103, CS1022, CS1031, CS1001, CS1513, CS1002, CS1026, CS1514, CS1055, CS0145, CS1010, CS1023, CS1012, CS8802, CS8124, CS0106, CS0270, CS0160, CS0501`

涉及文件：

- `HtmlUiI18nBindingPatch.cs`
- `HtmlUiBindingSchedulerPatch.cs`
- `HtmlUiErrorModelPatch.cs`
- `HtmlUiRequestCancellationPatch.cs`
- `HtmlUiHost.cs`

## 根因

框架项目的 `BannerlordHtmlUI.csproj` 使用：

```xml
<LangVersion>10.0</LangVersion>
```

多个 WebView2 JavaScript 注入 Patch 使用 C# 逐字字符串：

```csharp
private const string Script = @"...";
```

但生成的源码在逐字字符串内部错误地使用了 `\"` 试图转义双引号。

C# 逐字字符串中反斜杠没有转义双引号的作用；双引号必须写成 `""`。因此字符串在错误位置提前结束，后续 JavaScript 内容被 C# 编译器当作源码解析，产生大量互相级联的 `CS10xx/CS15xx` 语法错误。

## 修复

以下文件已重写为 C# 10 合法写法，JS 中用于 Patch Marker 的属性访问统一使用单引号，从源码层面避免在 C# 逐字字符串里出现错误的 `\"`：

- `HtmlUiI18nBindingPatch.cs`
- `HtmlUiBindingSchedulerPatch.cs`
- `HtmlUiErrorModelPatch.cs`
- `HtmlUiErrorModelPatch.cs`
- `HtmlUiRequestCancellationPatch.cs`

`HtmlUiHost.cs` 检查后未发现同类逐字字符串转义错误；其被错误列表列出属于前述文件解析失步导致的级联诊断，不是已确认的独立根因。

## 工程规则

后续新增 C# → JavaScript 注入脚本时：

1. C# `@"..."` 内禁止使用 `\"`。
2. 如果必须使用双引号，应写为 `""`。
3. 优先让注入 JavaScript 使用单引号属性/字符串，减少 C# 字符串层级转义。
4. 修改 Patch 注入文件后必须先完成 C# 语法级编译验证，再进入 Bannerlord 实机测试。
5. 不得通过提高 `LangVersion` 来规避本问题；项目目标语言版本继续保持 C# 10。

## 当前状态

该故障的源码根因已经修复，下一步应重新编译 Framework 与 Consumer TestMod，并确认上述错误码不再出现。