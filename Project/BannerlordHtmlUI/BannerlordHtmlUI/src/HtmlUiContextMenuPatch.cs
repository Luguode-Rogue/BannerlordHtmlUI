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
                HtmlUiLogger.Info("WebView2 native context menu suppression patch installed.");
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
            try
            {
                var web = _webField?.GetValue(__instance) as Microsoft.Web.WebView2.WinForms.WebView2;
                var core = web?.CoreWebView2;
                if (core == null) return;

                core.Settings.AreDefaultContextMenusEnabled = false;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Failed to suppress WebView2 native context menu: " + ex.GetBaseException().Message);
            }
        }
    }
}
