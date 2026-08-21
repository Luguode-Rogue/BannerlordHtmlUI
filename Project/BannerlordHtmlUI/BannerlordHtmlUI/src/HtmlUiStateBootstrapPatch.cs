using System;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiStateBootstrapPatch
    {
        // runtime-core.js now owns the initial state snapshot through game.ready().
        // Keeping a second document-created bootstrap causes duplicate state events
        // on every navigation and makes Consumer initialization order-dependent.
        public static void Install(HtmlUiHost host)
        {
            HtmlUiLogger.Debug("State bootstrap compatibility patch skipped; runtime core owns state hydration.");
        }
    }
}
