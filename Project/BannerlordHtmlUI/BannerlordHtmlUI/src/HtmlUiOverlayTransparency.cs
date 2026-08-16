using System;
using BannerlordHtmlUI;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Compatibility facade for HUD-style overlays.
    /// Transparency is configured when the WebView2 controller is initialized.
    /// </summary>
    public static class HtmlUiOverlayTransparency
    {
        public static void Enable(HtmlUiHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            HtmlUiLogger.Info("Transparent overlay is configured at WebView2 controller initialization.");
        }
    }
}
