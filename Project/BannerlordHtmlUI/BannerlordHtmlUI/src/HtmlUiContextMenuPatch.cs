using System;
using System.Reflection;
using HarmonyLib;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiContextMenuPatch
    {
        private const string HarmonyId = "BannerlordHtmlUI.ContextMenu";
        private static readonly object Sync = new object();
        private static Harmony _harmony;
        private static bool _installed;
        private static FieldInfo _webField;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;

            lock (Sync)
            {
                if (_installed) return;

                // Framework default: WebView2 DevTools and browser context menus are disabled.
                // An embedding consumer may explicitly opt into DevTools after Framework startup if needed.
                host.DevToolsEnabled = false;

                var method = AccessTools.Method(typeof(HtmlUiHost), "ConfigureAfterWebViewReady");
                if (method == null)
                    throw new MissingMethodException("HtmlUiHost.ConfigureAfterWebViewReady was not found.");

                _webField = typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_webField == null)
                    throw new MissingFieldException("HtmlUiHost._web was not found.");

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(
                    method,
                    postfix: new HarmonyMethod(
                        typeof(HtmlUiContextMenuPatch),
                        nameof(AfterConfigureWebView)));

                _installed = true;
                // Apply immediately to the current WebView2 instance because this patch is installed
                // after the initial ConfigureAfterWebViewReady call.
                ApplySettings(host);
                HtmlUiLogger.Info("WebView2 native context menu and DevTools suppression patch installed. Browser UI disabled by default.");
            }
        }

        public static void Uninstall()
        {
            lock (Sync)
            {
                if (!_installed) return;

                try { _harmony?.UnpatchAll(HarmonyId); }
                finally
                {
                    _harmony = null;
                    _webField = null;
                    _installed = false;
                }
            }
        }

        private static void AfterConfigureWebView(HtmlUiHost __instance)
        {
            ApplySettings(__instance);
        }

        private static void ApplySettings(HtmlUiHost host)
        {
            try
            {
                var web = _webField?.GetValue(host) as Microsoft.Web.WebView2.WinForms.WebView2;
                var core = web?.CoreWebView2;
                if (core == null) return;

                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = host.DevToolsEnabled;
                core.Settings.IsStatusBarEnabled = false;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Failed to apply WebView2 browser UI policy: " + ex.GetBaseException().Message);
            }
        }
    }
}
