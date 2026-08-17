using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TaleWorlds.MountAndBlade;
using TaleWorlds.InputSystem;

namespace BannerlordHtmlUI
{
    public sealed class SubModule : MBSubModuleBase
    {
        private Task _initTask;
        private string _moduleDirectory;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            _moduleDirectory = Path.GetDirectoryName(typeof(SubModule).Assembly.Location);
            var webRoot = Path.Combine(_moduleDirectory, "web");

            try { HtmlUiNativeRuntimeTexturePatch.Install(); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to install native runtime texture probe patch.", ex); }

            HtmlUiService.OnReady(RegisterFrameworkPages);
            _initTask = HtmlUiService.InitializeAsync(_moduleDirectory, webRoot);
            _initTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    HtmlUiLogger.Error("HtmlUiService initialization failed.", t.Exception?.GetBaseException());
            }, TaskScheduler.Default);
        }

        private static void RegisterFrameworkPages()
        {
            try
            {
                HtmlUiService.NotifyGameContext("application", true);
                HtmlUiHotReloadPatch.Install(HtmlUiService.Host);
                HtmlUiStateRemovalPatch.Install(HtmlUiService.Host);
                HtmlUiWindowTrackingPatch.Install(HtmlUiService.Host);
                HtmlUiProcessRecovery.Install(HtmlUiService.Host);
                HtmlUiContextMenuPatch.Install(HtmlUiService.Host);

                // WebView2-dependent Runtime patches are installed by HtmlUiHost.ConfigureAfterWebViewReady()
                // on the dedicated WebView2 UI thread. Do not reinstall them from Bannerlord's game thread.

                if (!HtmlUiCommands.CommandExists("runtime.error"))
                {
                    HtmlUiService.RegisterCommand("runtime.error", payload =>
                    {
                        try
                        {
                            HtmlUiLogger.Error("Browser runtime error: " + (payload == null ? "<null>" : payload.ToString(Newtonsoft.Json.Formatting.None)));
                        }
                        catch (Exception ex)
                        {
                            HtmlUiLogger.Error("Failed to log browser runtime error payload.", ex);
                        }
                    });
                }

                if (!HtmlUiService.Pages.Contains("framework"))
                    HtmlUiService.Pages.Register(new HtmlUiPage("framework", "index.html") { HotReload = true });
                if (!HtmlUiService.Pages.Contains("diagnostics"))
                    HtmlUiService.Pages.Register(new HtmlUiPage("diagnostics", "diagnostics.html") { HotReload = true });
                if (!HtmlUiService.Pages.Contains("brush-browser"))
                    HtmlUiService.Pages.Register(new HtmlUiPage("brush-browser", "brush-browser.html") { HotReload = true });
                if (!HtmlUiService.Pages.Contains("native-asset-diagnostics"))
                    HtmlUiService.Pages.Register(new HtmlUiPage("native-asset-diagnostics", "native-asset-diagnostics.html") { HotReload = true });
                if (!HtmlUiCommands.CommandExists("framework.openDiagnostics"))
                    HtmlUiService.RegisterCommand("framework.openDiagnostics", _ => HtmlUiService.Pages.Open("diagnostics"));
                if (!HtmlUiCommands.CommandExists("framework.openBrushBrowser"))
                    HtmlUiService.RegisterCommand("framework.openBrushBrowser", _ => HtmlUiService.Pages.Open("brush-browser"));
                if (!HtmlUiCommands.CommandExists("framework.openNativeAssetDiagnostics"))
                    HtmlUiService.RegisterCommand("framework.openNativeAssetDiagnostics", _ => HtmlUiService.Pages.Open("native-asset-diagnostics"));
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to register framework page.", ex);
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            try
            {
                HtmlUiService.Tick();

                // Temporary framework diagnostic hotkey. Remove or remap in production consumers.
                if (Input.IsKeyPressed(InputKey.F8))
                {
                    HtmlUiLogger.Warn("===== F8 NATIVE ASSET DIAGNOSTICS OPEN =====");
                    var opened = HtmlUiService.Pages.Open("native-asset-diagnostics");
                    HtmlUiLogger.Warn("F8 Native Asset Diagnostics result=" + opened
                        + ", currentPage=" + (HtmlUiService.Pages.CurrentId ?? "<null>")
                        + ", hostVisible=" + HtmlUiService.Host.IsVisible
                        + ", webViewReady=" + HtmlUiService.Host.IsWebViewReady
                        + ", inputMode=" + HtmlUiService.Host.InputMode);

                    HtmlUiService.State.Set("framework.nativeAssetDiagnostics", new
                    {
                        status = "loading",
                        startedUtc = DateTime.UtcNow
                    });

                    _ = RunNativeAssetDiagnosticsForF8Async();
                }

                if (Input.IsKeyPressed(InputKey.F10))
                {
                    HtmlUiLogger.Warn("===== F10 DIAGNOSTICS OPEN =====");
                    var opened = HtmlUiService.Pages.Open("diagnostics");
                    HtmlUiLogger.Warn("F10 Open result=" + opened
                        + ", initialized=" + HtmlUiService.IsInitialized
                        + ", ready=" + HtmlUiService.IsReady
                        + ", lifecycle=" + HtmlUiService.LifecycleState
                        + ", currentPage=" + (HtmlUiService.Pages.CurrentId ?? "<null>")
                        + ", hostVisible=" + HtmlUiService.Host.IsVisible
                        + ", webViewReady=" + HtmlUiService.Host.IsWebViewReady
                        + ", inputMode=" + HtmlUiService.Host.InputMode);
                }

                if (Input.IsKeyPressed(InputKey.F9))
                {
                    HtmlUiLogger.Warn("===== F9 BRUSH BROWSER OPEN =====");
                    var opened = HtmlUiService.Pages.Open("brush-browser");
                    HtmlUiLogger.Warn("F9 Brush Browser result=" + opened
                        + ", currentPage=" + (HtmlUiService.Pages.CurrentId ?? "<null>")
                        + ", hostVisible=" + HtmlUiService.Host.IsVisible
                        + ", webViewReady=" + HtmlUiService.Host.IsWebViewReady
                        + ", inputMode=" + HtmlUiService.Host.InputMode);
                }
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Application tick failed.", ex);
            }
        }

        private static async Task RunNativeAssetDiagnosticsForF8Async()
        {
            try
            {
                await Task.Delay(500).ConfigureAwait(false);
                var result = await HtmlUiNativeAssetDiagnosticsService.RunAsync(null, CancellationToken.None).ConfigureAwait(false);
                HtmlUiService.State.Set("framework.nativeAssetDiagnostics", result);
                HtmlUiLogger.Info("F8 Native Asset Diagnostics completed and state published.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("F8 Native Asset Diagnostics failed.", ex);
                HtmlUiService.State.Set("framework.nativeAssetDiagnostics", new
                {
                    status = "error",
                    error = ex.GetBaseException().ToString()
                });
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            try { HtmlUiNativeRuntimeTexturePatch.Uninstall(); } catch { }
            try { HtmlUiService.NotifyGameContext("application", false); } catch { }
            try { HtmlUiHotReloadPatch.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("HotReload patch uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiWindowTrackingPatch.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("Window tracking patch uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiProcessRecovery.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("WebView2 process recovery uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiContextMenuPatch.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("Context menu uninstall failed: " + ex.GetBaseException().Message); }
            HtmlUiService.Dispose();
            base.OnSubModuleUnloaded();
        }
    }
}