using System;
using System.IO;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// A parallel overlay UI that can be shown alongside other surfaces inside the same
    /// WebView2 host. Surfaces never own input or window state; they only declare a demand.
    /// </summary>
    public sealed class HtmlUiSurface
    {
        public string Id { get; }
        public string RelativePath { get; }
        public string ContentRootId { get; set; } = "ui";
        public string OwnerId { get; internal set; } = "framework";

        /// <summary>Display order. Also used as the tie-breaker for input arbitration.</summary>
        public int ZIndex { get; set; } = 100;

        /// <summary>
        /// The input mode this surface needs. This is a demand, not a result.
        /// The framework aggregates all visible surfaces into a single effective mode.
        /// </summary>
        public HtmlUiInputMode InputDemand { get; set; } = HtmlUiInputMode.Passive;

        /// <summary>When false the surface is mounted but excluded from rendering and input aggregation.</summary>
        public bool Enabled { get; set; } = true;

        public Action Opened { get; set; }
        public Action Closed { get; set; }

        public HtmlUiSurface(string id, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Surface id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("Relative path is required.", nameof(relativePath));

            var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
            if (Path.IsPathRooted(relativePath) || normalizedPath.StartsWith("/", StringComparison.Ordinal) || IsAbsoluteUri(relativePath))
                throw new ArgumentException("RelativePath must be a relative resource path.", nameof(relativePath));

            if (normalizedPath == ".." || normalizedPath.StartsWith("../", StringComparison.Ordinal) || normalizedPath.IndexOf("/../", StringComparison.Ordinal) >= 0)
                throw new ArgumentException("RelativePath must stay inside its content root.", nameof(relativePath));

            // '#' would swallow the surface identity query string into a URL fragment, and '?'
            // would split the path. Neither can appear in a surface entry path.
            if (normalizedPath.IndexOfAny(new[] { '#', '?' }) >= 0)
                throw new ArgumentException("RelativePath must not contain '#' or '?'.", nameof(relativePath));

            Id = id;
            RelativePath = normalizedPath;
        }

        private static bool IsAbsoluteUri(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out _);
        }
    }
}
