using System;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiBridgeShutdownPatch
    {
        public static void CancelAll()
        {
            try
            {
                var bridge = HtmlUiBridge.Current;
                if (bridge == null) return;
                bridge.CancelAllRequests();
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Bridge shutdown cancellation cleanup failed: " + ex.GetBaseException().Message);
            }
        }
    }
}
