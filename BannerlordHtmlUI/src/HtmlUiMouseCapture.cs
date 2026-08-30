namespace BannerlordHtmlUI
{
    /// <summary>
    /// Compatibility facade retained for older consumers.
    /// Native overlay mouse interception is intentionally disabled until a correct
    /// Bannerlord-side input routing implementation replaces it.
    /// </summary>
    public static class HtmlUiMouseCapture
    {
        internal static void Install()
        {
            // Intentionally no-op. Do not install a Harmony patch over Host input state.
            HtmlUiLogger.Info("HtmlUiMouseCapture compatibility facade loaded; native mouse interception disabled.");
        }

        internal static void Uninstall()
        {
            // Intentionally no-op.
        }

        public static void Capture()
        {
            HtmlUiService.SetInputMode(HtmlUiInputMode.MouseCaptured);
        }
    }
}