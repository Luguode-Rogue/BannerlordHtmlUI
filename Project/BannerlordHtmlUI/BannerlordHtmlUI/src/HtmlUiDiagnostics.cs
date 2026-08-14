using System;
using System.Collections.Generic;

namespace BannerlordHtmlUI
{
    public sealed class HtmlUiDiagnosticsSnapshot
    {
        public string SnapshotUtc { get; set; }
        public string FrameworkVersion { get; set; }
        public int ProtocolVersion { get; set; }
        public string Lifecycle { get; set; }
        public string InputMode { get; set; }
        public bool HostInitialized { get; set; }
        public bool WebViewReady { get; set; }
        public bool PageOpen { get; set; }
        public string CurrentPage { get; set; }
        public string CurrentPageOwner { get; set; }
        public string CurrentPagePath { get; set; }
        public bool HotReloadEnabled { get; set; }
        public bool DevToolsEnabled { get; set; }
        public bool WindowVisible { get; set; }
        public bool WindowForeground { get; set; }
        public bool WindowMinimized { get; set; }
        public string LastBrowserError { get; set; }
        public int ContentRootCount { get; set; }
        public int PageCount { get; set; }
        public int StateCount { get; set; }
        public bool NavigationInProgress { get; set; }
    }

    public static class HtmlUiDiagnostics
    {
        public const string FrameworkVersion = "0.44.0";
        public const int ProtocolVersion = 1;

        private static readonly object Sync = new object();
        private static string _lastBrowserError;

        internal static void RecordBrowserError(string message)
        {
            lock (Sync) _lastBrowserError = message;
        }

        public static HtmlUiDiagnosticsSnapshot Snapshot()
        {
            var host = HtmlUiService.IsInitialized ? HtmlUiService.Host : null;
            var window = host?.GetWindowState() ?? default(HtmlUiWindowState);
            var page = host?.Pages.Current;
            string lastError;
            lock (Sync) lastError = _lastBrowserError;

            return new HtmlUiDiagnosticsSnapshot
            {
                SnapshotUtc = DateTime.UtcNow.ToString("o"),
                FrameworkVersion = FrameworkVersion,
                ProtocolVersion = ProtocolVersion,
                Lifecycle = HtmlUiService.LifecycleState.ToString(),
                InputMode = host?.InputMode.ToString() ?? HtmlUiInputMode.Hidden.ToString(),
                HostInitialized = host != null,
                WebViewReady = host?.IsWebViewReady ?? false,
                PageOpen = !string.IsNullOrEmpty(host?.Pages.CurrentId),
                CurrentPage = page?.Id,
                CurrentPageOwner = page?.OwnerId,
                CurrentPagePath = page?.RelativePath,
                HotReloadEnabled = host?.HotReloadEnabled ?? false,
                DevToolsEnabled = host?.DevToolsEnabled ?? false,
                WindowVisible = window.IsVisible,
                WindowForeground = window.IsForeground,
                WindowMinimized = window.IsMinimized,
                LastBrowserError = lastError,
                ContentRootCount = host?.ContentRootCount ?? 0,
                PageCount = host?.Pages.Count ?? 0,
                StateCount = host?.State.Count ?? 0,
                NavigationInProgress = host?.NavigationInProgress ?? false
            };
        }
    }
}
