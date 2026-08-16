using System;
using System.Drawing;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Enables a transparent WinForms/WebView2 overlay so transparent HTML pixels reveal the host game.
    /// Intended for HUD-style consumer pages; normal pages remain unchanged unless explicitly enabled.
    /// </summary>
    public static class HtmlUiOverlayTransparency
    {
        private static readonly FieldInfo HostFormField =
            typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo HostWebField =
            typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo RunOnUiThreadSyncMethod =
            typeof(HtmlUiHost).GetMethod("RunOnUiThreadSync", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Color KeyColor = Color.Magenta;

        public static void Enable(HtmlUiHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (HostFormField == null || HostWebField == null || RunOnUiThreadSyncMethod == null)
                throw new MissingMemberException(typeof(HtmlUiHost).FullName, "overlay transparency members");

            RunOnUiThreadSyncMethod.Invoke(host, new object[] { (Action)(() => Apply(host)) });
        }

        private static void Apply(HtmlUiHost host)
        {
            var form = HostFormField.GetValue(host) as System.Windows.Forms.Form;
            var web = HostWebField.GetValue(host) as WebView2;
            if (form == null || web == null || web.CoreWebView2 == null || web.CoreWebView2Controller == null)
                throw new InvalidOperationException("WebView2 host is not ready for transparent overlay mode.");

            form.BackColor = KeyColor;
            form.TransparencyKey = KeyColor;
            form.Opacity = 1.0;

            // DefaultBackgroundColor belongs to the WebView2 controller, not CoreWebView2.
            // Full transparency (alpha = 0) lets transparent HTML pixels reveal the host game.
            web.CoreWebView2Controller.DefaultBackgroundColor = Color.Transparent;
        }
    }
}
