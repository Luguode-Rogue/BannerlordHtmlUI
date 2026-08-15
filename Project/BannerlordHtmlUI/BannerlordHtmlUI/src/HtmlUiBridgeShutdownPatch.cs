using System;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiBridgeShutdownPatch
    {
        public static void CancelAll(HtmlUiBridge bridge)
        {
            try
            {
                bridge?.CancelAllRequests();
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Bridge shutdown cancellation cleanup failed: " + ex.GetBaseException().Message);
            }
        }
    }
}
