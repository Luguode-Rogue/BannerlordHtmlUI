using System;
using System.Reflection;
using Microsoft.Web.WebView2.Core;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiBridgeDetachExtensions
    {
        private static readonly FieldInfo HostField = typeof(HtmlUiBridge).GetField(
            "_host",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo MessageHandler = typeof(HtmlUiBridge).GetMethod(
            "OnWebMessageReceived",
            BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Detach(this HtmlUiBridge bridge)
        {
            if (bridge == null) return;

            try
            {
                var host = HostField?.GetValue(bridge) as HtmlUiHost;
                if (host != null)
                {
                    var webField = typeof(HtmlUiHost).GetField(
                        "_web",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var web = webField?.GetValue(host) as Microsoft.Web.WebView2.WinForms.WebView2;
                    var core = web?.CoreWebView2;

                    if (core != null && MessageHandler != null)
                        core.WebMessageReceived -= (EventHandler<CoreWebView2WebMessageReceivedEventArgs>)
                            Delegate.CreateDelegate(
                                typeof(EventHandler<CoreWebView2WebMessageReceivedEventArgs>),
                                bridge,
                                MessageHandler,
                                throwOnBindFailure: false);
                }
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Bridge detach event cleanup failed: " + ex.GetBaseException().Message);
            }
            finally
            {
                // HtmlUiBridge.Current is weak, so there is no strong retention once the
                // Host releases the bridge. The extension deliberately avoids depending on
                // private static state mutation here.
            }
        }
    }
}
