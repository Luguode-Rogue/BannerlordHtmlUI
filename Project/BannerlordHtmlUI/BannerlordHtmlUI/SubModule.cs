using System;
using System.IO;
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
            Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00000000");
            try { HtmlUiTransparencyPatch.Install(); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to install transparent overlay patch.", ex); }
            HtmlUiService.OnReady(RegisterFrameworkPages);
            _initTask = HtmlUiService.InitializeAsync(_moduleDirectory, webRoot);
            _initTask.ContinueWith(t => { if (t.IsFaulted) HtmlUiLogger.Error("HtmlUiService initialization failed.", t.Exception?.GetBaseException()); }, TaskScheduler.Default);
        }

        private static void RegisterFrameworkPages()
        {
            try
            {
                HtmlUiService.NotifyGameContext("application", true);
                HtmlUiMouseCapture.Install();
                HtmlUiHotReloadPatch.Install(HtmlUiService.Host);
                HtmlUiStateRemovalPatch.Install(HtmlUiService.Host);
                HtmlUiProcessRecovery.Install(HtmlUiService.Host);
                HtmlUiInputControllerPatch.Install(HtmlUiService.Host);
                HtmlUiContextMenuPatch.Install(HtmlUiService.Host);

                if (!HtmlUiCommands.CommandExists("runtime.error"))
                {
                    HtmlUiService.RegisterCommand("runtime.error", payload =>
                    {
                        try { HtmlUiLogger.Error("Browser runtime error: " + (payload == null ? "<null>" : payload.ToString(Newtonsoft.Json.Formatting.None))); }
                        catch (Exception ex) { HtmlUiLogger.Error("Failed to log browser runtime error payload.", ex); }
                    });
                }

                if (!HtmlUiService.Pages.Contains("framework"))
                    HtmlUiService.Pages.Register(new HtmlUiPage("framework", "index.html") { HotReload = true });
                if (!HtmlUiService.Pages.Contains("diagnostics"))
                    HtmlUiService.Pages.Register(new HtmlUiPage("diagnostics", "diagnostics.html") { HotReload = true });
                if (!HtmlUiCommands.CommandExists("framework.openDiagnostics"))
                    HtmlUiService.RegisterCommand("framework.openDiagnostics", _ => HtmlUiService.Pages.Open("diagnostics"));
            }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to register framework page.", ex); }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            try
            {
                HtmlUiService.Tick();
                if (Input.IsKeyPressed(InputKey.F10))
                {
                    HtmlUiLogger.Warn("===== F10 DIAGNOSTICS OPEN =====");
                    var opened = HtmlUiService.Pages.Open("diagnostics");
                    HtmlUiLogger.Warn("F10 Open result=" + opened + ", initialized=" + HtmlUiService.IsInitialized + ", ready=" + HtmlUiService.IsReady + ", lifecycle=" + HtmlUiService.LifecycleState + ", currentPage=" + (HtmlUiService.Pages.CurrentId ?? "<null>") + ", hostVisible=" + HtmlUiService.Host.IsVisible + ", webViewReady=" + HtmlUiService.Host.IsWebViewReady + ", inputMode=" + HtmlUiService.Host.InputMode);
                }
            }
            catch (Exception ex) { HtmlUiLogger.Error("Application tick failed.", ex); }
        }

        protected override void OnSubModuleUnloaded()
        {
            try { HtmlUiService.NotifyGameContext("application", false); } catch { }
            try { HtmlUiHotReloadPatch.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("HotReload patch uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiKeyboardAndDiagnosticsPatch.Uninstall(HtmlUiService.Host); } catch (Exception ex) { HtmlUiLogger.Debug("Keyboard diagnostics uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiInputControllerPatch.Uninstall(HtmlUiService.Host); } catch (Exception ex) { HtmlUiLogger.Debug("Input controller uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiNavigationRacePatch.Uninstall(HtmlUiService.Host); } catch (Exception ex) { HtmlUiLogger.Debug("Navigation race patch uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiMouseCapture.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("Mouse capture policy uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiProcessRecovery.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("WebView2 process recovery uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiContextMenuPatch.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("Context menu patch uninstall failed: " + ex.GetBaseException().Message); }
            HtmlUiService.Dispose();
            base.OnSubModuleUnloaded();
        }
    }
}
