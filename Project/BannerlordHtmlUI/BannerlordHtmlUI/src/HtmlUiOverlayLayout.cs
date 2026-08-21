using System;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace BannerlordHtmlUI
{
    public enum HtmlUiOverlayLayoutMode
    {
        FullWindow,
        TopRight
    }

    /// <summary>
    /// Consumer-facing overlay placement API. Framework defaults to FullWindow.
    /// </summary>
    public static class HtmlUiOverlayLayout
    {
        public static void UseFullWindow()
        {
            if (!HtmlUiService.IsInitialized) return;
            HtmlUiOverlayLayoutRegistry.Set(HtmlUiService.Host, HtmlUiOverlayLayoutMode.FullWindow, 0, 0, 0);
            HtmlUiWindowTracker.RequestSync(HtmlUiService.Host);
        }

        public static void UseTopRight(int width, int height, int margin)
        {
            if (!HtmlUiService.IsInitialized) return;
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
            if (margin < 0) throw new ArgumentOutOfRangeException(nameof(margin));

            HtmlUiOverlayLayoutRegistry.Set(HtmlUiService.Host, HtmlUiOverlayLayoutMode.TopRight, width, height, margin);
            HtmlUiWindowTracker.RequestSync(HtmlUiService.Host);
        }
    }

    internal sealed class HtmlUiOverlayLayoutState
    {
        public HtmlUiOverlayLayoutMode Mode = HtmlUiOverlayLayoutMode.FullWindow;
        public int Width;
        public int Height;
        public int Margin;
    }

    internal static class HtmlUiOverlayLayoutRegistry
    {
        private static readonly ConditionalWeakTable<HtmlUiHost, HtmlUiOverlayLayoutState> States =
            new ConditionalWeakTable<HtmlUiHost, HtmlUiOverlayLayoutState>();

        public static void Set(HtmlUiHost host, HtmlUiOverlayLayoutMode mode, int width, int height, int margin)
        {
            if (host == null) return;
            var state = States.GetOrCreateValue(host);
            state.Mode = mode;
            state.Width = width;
            state.Height = height;
            state.Margin = margin;
        }

        public static Rectangle GetBounds(HtmlUiHost host, int left, int top, int width, int height)
        {
            var state = States.GetOrCreateValue(host);
            if (state.Mode != HtmlUiOverlayLayoutMode.TopRight)
                return new Rectangle(left, top, Math.Max(1, width), Math.Max(1, height));

            int regionWidth = Math.Min(Math.Max(1, state.Width), Math.Max(1, width));
            int regionHeight = Math.Min(Math.Max(1, state.Height), Math.Max(1, height));
            int margin = Math.Max(0, state.Margin);
            int regionLeft = left + Math.Max(0, width - margin - regionWidth);
            int regionTop = top + margin;
            return new Rectangle(regionLeft, regionTop, regionWidth, regionHeight);
        }
    }
}
