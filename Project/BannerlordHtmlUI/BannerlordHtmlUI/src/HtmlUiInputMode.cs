namespace BannerlordHtmlUI
{
    /// <summary>Controls how the HTML host participates in mouse/keyboard input.</summary>
    public enum HtmlUiInputMode
    {
        Hidden = 0,
        Passive = 1,
        Captured = 2,
        /// <summary>Captures mouse input without activating the WebView2 window, leaving keyboard input with the game.</summary>
        MouseCaptured = 3
    }
}
