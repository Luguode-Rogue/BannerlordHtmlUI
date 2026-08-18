using System;
using System.Drawing;
using System.Reflection;
using HarmonyLib;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Framework-level transparent WebView/overlay setup.
    /// WebView2 defaults to a white background; transparent consumers need
    /// transparent page pixels to reveal the Bannerlord frame underneath.
    /// </summary>
    internal static class HtmlUiTransparencyPatch
    {
        private static readonly FieldInfo WebField =
            typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FormField =
            typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            var harmony = new Harmony("BannerlordHtmlUI.Transparency");
            var target = AccessTools.Method(typeof(HtmlUiHost), "ConfigureAfterWebViewReady");
            var postfix = AccessTools.Method(typeof(HtmlUiTransparencyPatch), nameof(AfterWebViewReady));
            if (target == null || postfix == null)
                throw new MissingMethodException("BannerlordHtmlUI transparency target method was not found.");

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            _installed = true;
            HtmlUiLogger.Info("Transparent WebView/overlay patch installed.");
        }

        private static void AfterWebViewReady(HtmlUiHost __instance)
        {
            try
            {
                var web = WebField?.GetValue(__instance) as WebView2;
                if (web == null)
                    throw new InvalidOperationException("HtmlUiHost WebView2 instance is unavailable.");

                // WebView2 officially supports only alpha 0 or 255. Transparent is 0.
                web.DefaultBackgroundColor = Color.Transparent;

                var form = FormField?.GetValue(__instance) as System.Windows.Forms.Form;
                if (form != null && !form.IsDisposed)
                {
                    var key = Color.Magenta;
                    form.BackColor = key;
                    form.TransparencyKey = key;
                    form.Opacity = 1.0;
                }

                HtmlUiLogger.Info("Transparent WebView/overlay configured successfully.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Transparent WebView/overlay configuration failed.", ex);
            }
        }
    }
}
