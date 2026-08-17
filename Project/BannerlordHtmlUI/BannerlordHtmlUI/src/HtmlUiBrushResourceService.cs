namespace BannerlordHtmlUI
{
    internal static class HtmlUiBrushResourceService
    {
        public static void Initialize(HtmlUiHost host)
        {
            HtmlUiBrushResourceServiceCore.Initialize(host);
        }

        public static void Dispose()
        {
            HtmlUiBrushResourceServiceCore.Dispose();
        }

        public static object CreateSpriteSnapshot(object sprite, bool includeResource)
        {
            return HtmlUiBrushResourceServiceCore.CreateSpriteSnapshot(sprite, includeResource);
        }
    }
}
