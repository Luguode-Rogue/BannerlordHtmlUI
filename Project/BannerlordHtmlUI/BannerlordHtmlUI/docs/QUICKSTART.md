# BannerlordHtmlUI 5 分钟上手

## 目标

做一个 Bannerlord UI：

```text
网页按钮
    ↓
JavaScript
    ↓
C#
    ↓
Bannerlord 游戏逻辑
```

## 第一步：准备页面

```text
UI/Hello/index.html
```

```html
<!doctype html>
<html>
<body>
    <h1>My Bannerlord UI</h1>
    <button id="button">测试</button>

    <script>
        document.querySelector('#button').onclick = () => {
            game.call('hello');
        };

        game.on('hello.result', data => {
            alert(data.text);
        });
    </script>
</body>
</html>
```

## 第二步：注册页面

```csharp
HtmlUiService.Pages.Register(
    new HtmlUiPage("hello", "Hello/index.html"));
```

## 第三步：注册 Command

```csharp
HtmlUiService.RegisterCommand("hello", payload =>
{
    // Bannerlord 游戏逻辑。

    HtmlUiService.SendEvent("hello.result", new
    {
        text = "Hello from C#!"
    });
});
```

## 第四步：打开页面

```csharp
HtmlUiService.Pages.Open("hello");
```

## 第五步：需要交互时接管输入

```csharp
HtmlUiService.CaptureInput();
```

关闭时：

```csharp
HtmlUiService.Pages.Close("hello");
HtmlUiService.ReleaseInput();
```

完整 API 见 `docs/USAGE.md` 和 `docs/API.md`。
