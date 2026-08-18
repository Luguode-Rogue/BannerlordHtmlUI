# BUTR 工程资源放置规则

本文件是 BannerlordHtmlUI 及其 Consumer 工程创建、迁移和修改时的**默认资源放置规则**。

## 1. 默认假设

除非项目明确采用其他构建体系，否则使用 BUTR Bannerlord 模板的工程应假定：

```text
工程项目目录/_Module/
        ↓
最终 Bannerlord Mod 根目录
```

也就是说，`_Module` 是最终 `Modules/<ModId>/` 的工程内镜像。

## 2. 新增文件时必须先判断最终位置

创建任何随 Mod 部署的文件前，先问：

> 这个文件最终应该出现在 `Modules/<ModId>/` 的什么位置？

然后在工程 `_Module/` 下使用完全相同的相对路径。

例如最终需要：

```text
Modules/MyMod/ModuleData/Languages/zh-CN.xml
```

工程中直接创建：

```text
MyMod/_Module/ModuleData/Languages/zh-CN.xml
```

不要先创建到其他目录，再要求用户手工复制。

## 3. Framework / Consumer 的程序集相对资源是例外

不是所有资源都属于 Mod 根。

例如 BannerlordHtmlUI Framework 当前使用：

```text
Modules/BannerlordHtmlUI/bin/<GameBinariesFolder>/web/
```

Consumer TestMod 当前使用：

```text
Modules/HtmlUiConsumerTestMod/bin/<GameBinariesFolder>/UI/
```

这些资源必须根据代码中的 `Assembly.Location` 和项目部署目标决定最终位置。

因此创建资源时要区分：

```text
Mod 根资源
→ _Module/<最终相对路径>

程序集旁资源
→ 按对应项目的 Assembly.Location / Deployment Target 规则
```

## 4. 给其他工程增加 HtmlUI 页面时的默认做法

新建 Consumer 工程时：

1. 先检查该工程是否采用 BUTR 模板。
2. 如果采用，确认 `_Module/` 是否存在。
3. 所有需要进入 Mod 根的 HTML/UI 外部资源，按最终运行路径决定是否进入 `_Module/`。
4. 对于使用 HtmlUI Framework 的程序集旁 UI，必须先检查该 Consumer 的 `SubModule.cs` 和 `.csproj`，以实际 `Assembly.Location` 注册路径为准。
5. `.csproj` 的 Build/Deploy Target 应负责自动复制到游戏目录，用户不应被要求手工复制。

## 5. AI / 开发助手默认规则

以后在这个仓库中创建新的 Mod 文件、UI 资源、语言文件、XML、Prefab、图片、前端静态资源或其他部署文件时，**默认先计算最终 Bannerlord 部署路径，再决定工程路径**。

不得因为“文件目前只是测试资源”而随意放在一个临时目录后要求用户手工移动。

如果最终位置属于 Mod 根，直接创建在 `_Module/` 的对应位置。

如果最终位置属于 DLL 所在目录或 DLL 的子目录，则按照对应项目的 `Assembly.Location` / `.csproj` 部署规则放置，并同步更新部署规则，而不是要求用户手工复制。

## 6. 验证标准

完成资源新增后，至少核对：

```text
工程源路径
    ↓
构建输出
    ↓
Modules/<ModId>/最终路径
    ↓
代码实际读取路径
```

四者必须一致。

路径问题出现时，优先检查这四个位置，不要直接修改运行时路径猜测。
