using System;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Compatibility facade retained for older consumers.
    ///
    /// Game-side suppression now exists: <see cref="HtmlUiInputBlocker"/> hides owned input from
    /// Bannerlord's polling API. The input controller is the only caller that turns it on, and it
    /// is always released for Passive and Hidden.
    /// </summary>
    public static class HtmlUiMouseCapture
    {
        internal static void Install()
        {
            try { HtmlUiInputBlocker.Install(); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to install the Bannerlord input blocker.", ex); }
        }

        internal static void Uninstall()
        {
            try { HtmlUiInputBlocker.Uninstall(); }
            catch (Exception ex) { HtmlUiLogger.Debug("Input blocker uninstall failed: " + ex.GetBaseException().Message); }
        }

        public static void Capture()
        {
            HtmlUiService.SetInputMode(HtmlUiInputMode.MouseCaptured);
        }
    }
}