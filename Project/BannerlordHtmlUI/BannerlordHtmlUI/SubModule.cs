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
                HtmlUiI18nBindingPatch.Install(HtmlUiService.Host);
                HtmlUiService.NotifyGameContext("application", true);
                if (!HtmlUiService.Pages.Contains("framework"))
                    HtmlUiService.Pages.Register(new HtmlUiPage("framework", "index.html") { HotReload = true });
                if (!HtmlUiService.Pages.Contains("diagnostics"))
                    HtmlUiService.Pages.Register(new HtmlUiPage("diagnostics", "diagnostics.html") { HotReload = true });
                if (!HtmlUiCommands.CommandExists("framework.openDiagnostics"))
                    HtmlUiService.RegisterCommand("framework.openDiagnostics", _ => HtmlUiService.Pages.Open("diagnostics"));
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
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Application tick failed.", ex);
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            try { HtmlUiService.NotifyGameContext("application", false); } catch { }
            HtmlUiService.Dispose();
            base.OnSubModuleUnloaded();
        }
    }
}
