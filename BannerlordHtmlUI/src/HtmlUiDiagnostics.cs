using System;

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
        public int BridgeCommandCount { get; set; }
        public int BridgeRequestCount { get; set; }
        public int ActiveRequestCount { get; set; }
        public bool NavigationInProgress { get; set; }

        // Surfaces
        public int SurfaceCount { get; set; }
        public int VisibleSurfaceCount { get; set; }
        public bool ShellActive { get; set; }
        public bool SurfacesSuppressedByPage { get; set; }
        public string SurfaceEffectiveInputMode { get; set; }
        public string SurfaceInputOwner { get; set; }
        public bool BlockingGameMouse { get; set; }
        public bool BlockingGameKeyboard { get; set; }
        public string SurfaceSummary { get; set; }
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
            var bridge = HtmlUiBridge.Current;
            string lastError;
            lock (Sync) lastError = _lastBrowserError;

            var surfaces = host?.Surfaces;
            var aggregate = surfaces?.Aggregate;
            var surfaceSummary = string.Empty;
            if (surfaces != null)
            {
                var lines = new System.Text.StringBuilder();
                foreach (var view in surfaces.All)
                {
                    if (lines.Length > 0) lines.Append('\n');
                    lines.Append(view.Id)
                         .Append(" | owner=").Append(view.OwnerId)
                         .Append(" | z=").Append(view.ZIndex)
                         .Append(" | visible=").Append(view.Visible)
                         .Append(" | demand=").Append(view.InputDemand);
                    if (view.Enabled == false) lines.Append(" | disabled");
                    if (view.Suppressed) lines.Append(" | suppressed-by-page");
                }
                surfaceSummary = lines.ToString();
            }

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
                BridgeCommandCount = bridge?.CommandCount ?? 0,
                BridgeRequestCount = bridge?.RequestCount ?? 0,
                ActiveRequestCount = bridge?.ActiveRequestCount ?? 0,
                NavigationInProgress = host?.NavigationInProgress ?? false,

                SurfaceCount = surfaces?.Count ?? 0,
                VisibleSurfaceCount = surfaces?.VisibleCount ?? 0,
                ShellActive = host?.IsShellActive ?? false,
                SurfacesSuppressedByPage = surfaces?.IsSuppressedByPage ?? false,
                SurfaceEffectiveInputMode = aggregate?.EffectiveInputMode.ToString() ?? HtmlUiInputMode.Hidden.ToString(),
                SurfaceInputOwner = aggregate?.InputOwnerId ?? string.Empty,
                BlockingGameMouse = HtmlUiInputBlocker.IsBlockingMouse,
                BlockingGameKeyboard = HtmlUiInputBlocker.IsBlockingKeyboard,
                SurfaceSummary = surfaceSummary
            };
        }
    }
}
