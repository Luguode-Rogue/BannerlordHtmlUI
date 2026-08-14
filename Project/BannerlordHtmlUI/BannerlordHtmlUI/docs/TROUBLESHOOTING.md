# BannerlordHtmlUI 故障排查

## WebView2 初始化失败

检查：

1. Windows 是否安装 WebView2 Runtime。
2. 当前用户是否有权限写入 WebView2 User Data Folder。
3. Mod 是否正在正确的 Bannerlord 进程中加载。
4. `webRoot` 是否存在。
5. 查看 Framework 日志中的初始化异常。

默认 User Data Folder：

```text
%TEMP%\BannerlordHtmlUI\WebView2
```

## 页面白屏

先打开 DevTools：

```csharp
HtmlUiService.OpenDevTools();
```

检查：

- URL 是否正确。
- HTML 是否存在。
- JS 是否有语法异常。
- CSS/JS 相对路径是否正确。
- 页面是否依赖了不允许使用的外部资源。

本框架默认使用：

```text
https://bannerlord-htmlui.local/
```

映射到本地 `webRoot`。

## `game.request()` 超时

通常表示：

- C# Request 没有注册。
- Bannerlord 游戏线程没有正常 Tick。
- C# Handler 长时间阻塞。
- Handler 发生异常。

检查 Request 名称是否完全一致。

不要在 Request Handler 中执行长时间同步 IO。

## JS 能显示，但游戏逻辑没有响应

不要直接在 WebView2 的回调线程操作 Bannerlord API。

使用 Framework 的注册接口：

```csharp
HtmlUiService.RegisterCommand(...);
HtmlUiService.RegisterRequest(...);
```

Framework 会把操作排到 Bannerlord Tick 中。

## HTML 修改后没有刷新

确认：

```csharp
HtmlUiService.Host.HotReloadEnabled = true;
```

以及页面：

```csharp
new HtmlUiPage(...)
{
    HotReload = true
};
```

生产环境建议关闭热重载。

## UI 和游戏输入冲突

使用：

```csharp
HtmlUiService.CaptureInput();
HtmlUiService.ReleaseInput();
```

当前版本的输入隔离属于基础实现，实机环境下仍需要根据具体窗口模式验证。
