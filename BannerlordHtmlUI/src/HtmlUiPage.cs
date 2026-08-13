using System;

namespace BannerlordHtmlUI
{
    public sealed class HtmlUiPage
    {
        public string Id { get; }
        public string RelativePath { get; }
        public string ContentRootId { get; set; } = "framework";
        public string OwnerId { get; internal set; } = "framework";
        public bool HotReload { get; set; }
        public HtmlUiInputMode DefaultInputMode { get; set; } = HtmlUiInputMode.Passive;
        public Action Opened { get; set; }
        public Action Closed { get; set; }

        /// <summary>
        /// When set, the overlay window is sized to these dimensions (in screen pixels)
        /// instead of following the full game window. Null keeps full-screen behavior.
        /// </summary>
        public int? OverlayWidth { get; set; }
        public int? OverlayHeight { get; set; }

        /// <summary>Whether the WebView2 background should be fully transparent (alpha=0).</summary>
        public bool Transparent { get; set; }

        /// <summary>True when this page is configured as a non-fullscreen overlay.</summary>
        public bool IsOverlay => OverlayWidth.HasValue && OverlayHeight.HasValue && OverlayWidth.Value > 0 && OverlayHeight.Value > 0;

        public HtmlUiPage(string id, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Page id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("Relative path is required.", nameof(relativePath));
            Id = id;
            RelativePath = relativePath.Replace('\\', '/').TrimStart('/');
            if (RelativePath == ".." || RelativePath.StartsWith("../", StringComparison.Ordinal) || RelativePath.IndexOf("/../", StringComparison.Ordinal) >= 0)
                throw new ArgumentException("RelativePath must stay inside its content root.", nameof(relativePath));
        }
    }
}
