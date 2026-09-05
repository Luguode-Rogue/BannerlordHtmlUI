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
            HtmlUiInputTraceLogger.Initialize(_moduleDirectory);
            HtmlUiInputTraceLogger.Event("SUBMODULE_LOAD");
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
                HtmlUiInputTraceLogger.Event("FRAMEWORK_READY_REGISTER");
                HtmlUiService.NotifyGameContext("application", true);
                // Each component installs independently: one failure must never leave the
                // remaining owners (especially the input controller and blocker) uninstalled.
                TryInstall("WindowTracker", () => HtmlUiWindowTracker.Install(HtmlUiService.Host));
                TryInstall("InputBlocker", () => HtmlUiMouseCapture.Install());
                TryInstall("HotReloadPatch", () => HtmlUiHotReloadPatch.Install(HtmlUiService.Host));
                TryInstall("StateRemovalPatch", () => HtmlUiStateRemovalPatch.Install(HtmlUiService.Host));
                TryInstall("ProcessRecovery", () => HtmlUiProcessRecovery.Install(HtmlUiService.Host));
                TryInstall("InputControllerPatch", () => HtmlUiInputControllerPatch.Install(HtmlUiService.Host));
                TryInstall("ContextMenuPatch", () => HtmlUiContextMenuPatch.Install(HtmlUiService.Host));

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
                    HtmlUiService.Pages.Register(new HtmlUiPage("diagnostics", "diagnostics.html")
                    {
                        HotReload = true,
                        // Without Captured the page opens pass-through: no clicks, no keys, no way
                        // to close it. Any interactive framework page must own its input.
                        DefaultInputMode = HtmlUiInputMode.Captured
                    });
                if (!HtmlUiCommands.CommandExists("framework.openDiagnostics"))
                    HtmlUiService.RegisterCommand("framework.openDiagnostics", _ => HtmlUiService.Pages.Open("diagnostics"));
            }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to register framework page.", ex); HtmlUiInputTraceLogger.Event("FRAMEWORK_READY_REGISTER_FAILED " + ex.GetBaseException().Message); }
        }

        private static void TryInstall(string name, Action install)
        {
            try { install(); }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Framework component installation failed: " + name, ex);
                HtmlUiInputTraceLogger.Event("COMPONENT_INSTALL_FAILED " + name);
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            HtmlUiInputTraceLogger.TickStart(dt);
            try
            {
                base.OnApplicationTick(dt);
                HtmlUiInputTraceLogger.TickAfterBase();

                HtmlUiInputTraceLogger.BannerlordInput(HtmlUiService.IsInitialized ? HtmlUiService.Host : null);
                HtmlUiInputTraceLogger.TickAfterInput();

                HtmlUiService.Tick();
                HtmlUiInputTraceLogger.TickAfterService();

                if (Input.IsKeyPressed(InputKey.F10))
                {
                    HtmlUiInputTraceLogger.Event("F10_DIAGNOSTICS_HOTKEY");
                    HtmlUiLogger.Warn("===== F10 DIAGNOSTICS OPEN =====");
                    var opened = HtmlUiService.Pages.Open("diagnostics");
                    HtmlUiLogger.Warn("F10 Open result=" + opened + ", initialized=" + HtmlUiService.IsInitialized + ", ready=" + HtmlUiService.IsReady + ", lifecycle=" + HtmlUiService.LifecycleState + ", currentPage=" + (HtmlUiService.Pages.CurrentId ?? "<null>") + ", hostVisible=" + HtmlUiService.Host.IsVisible + ", webViewReady=" + HtmlUiService.Host.IsWebViewReady + ", inputMode=" + HtmlUiService.Host.InputMode);
                    HtmlUiInputTraceLogger.Event("F10_RESULT opened=" + opened + " current=" + (HtmlUiService.Pages.CurrentId ?? "<null>"));
                }
                HtmlUiInputTraceLogger.TickAfterF10();
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Application tick failed.", ex);
                HtmlUiInputTraceLogger.Event("APPLICATION_TICK_ERROR " + ex.GetBaseException().Message);
            }
            finally
            {
                HtmlUiInputTraceLogger.TickCompleted();
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            HtmlUiInputTraceLogger.Event("SUBMODULE_UNLOAD_BEGIN");
            try { HtmlUiService.NotifyGameContext("application", false); } catch { }
            try { HtmlUiHotReloadPatch.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("HotReload patch uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiKeyboardAndDiagnosticsPatch.Uninstall(HtmlUiService.Host); } catch (Exception ex) { HtmlUiLogger.Debug("Keyboard diagnostics uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiInputControllerPatch.Uninstall(HtmlUiService.Host); } catch (Exception ex) { HtmlUiLogger.Debug("Input controller uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiWindowTracker.Uninstall(HtmlUiService.Host); } catch (Exception ex) { HtmlUiLogger.Debug("Window tracker uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiNavigationRacePatch.Uninstall(HtmlUiService.Host); } catch (Exception ex) { HtmlUiLogger.Debug("Navigation race patch uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiMouseCapture.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("Mouse capture policy uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiProcessRecovery.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("WebView2 process recovery uninstall failed: " + ex.GetBaseException().Message); }
            try { HtmlUiContextMenuPatch.Uninstall(); } catch (Exception ex) { HtmlUiLogger.Debug("Context menu patch uninstall failed: " + ex.GetBaseException().Message); }
            HtmlUiService.Dispose();
            HtmlUiInputTraceLogger.Event("SUBMODULE_UNLOAD_END");
            HtmlUiInputTraceLogger.Shutdown();
            base.OnSubModuleUnloaded();
        }
    }
}
