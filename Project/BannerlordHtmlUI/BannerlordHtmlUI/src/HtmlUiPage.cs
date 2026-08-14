using System;
using System.IO;

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

        public HtmlUiPage(string id, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Page id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("Relative path is required.", nameof(relativePath));

            var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
            if (Path.IsPathRooted(relativePath) || normalizedPath.StartsWith("/", StringComparison.Ordinal) || IsAbsoluteUri(relativePath))
                throw new ArgumentException("RelativePath must be a relative resource path.", nameof(relativePath));

            if (normalizedPath == ".." || normalizedPath.StartsWith("../", StringComparison.Ordinal) || normalizedPath.IndexOf("/../", StringComparison.Ordinal) >= 0)
                throw new ArgumentException("RelativePath must stay inside its content root.", nameof(relativePath));

            RelativePath = normalizedPath;
        }

        private static bool IsAbsoluteUri(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out _);
        }
    }
}
