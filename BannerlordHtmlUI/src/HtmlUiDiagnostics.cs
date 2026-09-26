using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

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
        public int RuntimeDocumentCount { get; set; }
        public string RuntimeDocumentSummary { get; set; }
        public long StateDispatchFlushCount { get; set; }
        public long StateUpdateCount { get; set; }
        public long StateCoalescedCount { get; set; }
        public int PendingStateCount { get; set; }
        public int LiveFrameCount { get; set; }
    }

    public static class HtmlUiDiagnostics
    {
        public const string FrameworkVersion = "0.45.0";
        public const int ProtocolVersion = 1;

        private static readonly object Sync = new object();
        private static string _lastBrowserError;
        private static readonly Dictionary<string, RuntimeDocumentRecord> RuntimeDocuments =
            new Dictionary<string, RuntimeDocumentRecord>(StringComparer.OrdinalIgnoreCase);
        private const int MaxRuntimeDocumentRecords = 128;

        private sealed class RuntimeDocumentRecord
        {
            public string DocumentId;
            public string OwnerId;
            public string PageId;
            public string SurfaceId;
            public string Url;
            public string RuntimeVersion;
            public DateTime RuntimeReadyUtc;
            public DateTime? BusinessReadyUtc;
            public string Component;
            public string Validation;
        }

        internal static void RecordBrowserError(string message)
        {
            lock (Sync) _lastBrowserError = message;
        }

        internal static void RecordDocumentHello(JToken payload, HtmlUiHost host)
        {
            if (payload == null) return;
            var documentId = payload["documentId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(documentId)) return;
            var ownerId = payload["ownerId"]?.Value<string>();
            var surfaceId = payload["surfaceId"]?.Value<string>();
            var validation = ValidateSurfaceIdentity(host, surfaceId, ownerId);
            lock (Sync)
            {
                TrimRuntimeDocumentsIfNeeded(documentId);
                RuntimeDocuments[documentId] = new RuntimeDocumentRecord
                {
                    DocumentId = documentId,
                    OwnerId = ownerId,
                    PageId = payload["pageId"]?.Value<string>(),
                    SurfaceId = surfaceId,
                    Url = payload["url"]?.Value<string>(),
                    RuntimeVersion = payload["runtimeVersion"]?.Value<string>(),
                    RuntimeReadyUtc = DateTime.UtcNow,
                    Validation = validation
                };
            }
            if (!string.Equals(validation, "ok", StringComparison.OrdinalIgnoreCase))
                HtmlUiLogger.Warn("Runtime document identity mismatch: " + validation + " document=" + documentId);
        }

        internal static void RecordSurfaceReady(JToken payload, HtmlUiHost host)
        {
            if (payload == null) return;
            var documentId = payload["documentId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(documentId)) return;
            var ownerId = payload["ownerId"]?.Value<string>();
            var surfaceId = payload["surfaceId"]?.Value<string>();
            var validation = ValidateSurfaceIdentity(host, surfaceId, ownerId);
            lock (Sync)
            {
                if (!RuntimeDocuments.TryGetValue(documentId, out var record))
                {
                    TrimRuntimeDocumentsIfNeeded(documentId);
                    record = new RuntimeDocumentRecord
                    {
                        DocumentId = documentId,
                        OwnerId = ownerId,
                        SurfaceId = surfaceId,
                        RuntimeReadyUtc = DateTime.UtcNow,
                        Validation = validation
                    };
                    RuntimeDocuments[documentId] = record;
                }
                record.BusinessReadyUtc = DateTime.UtcNow;
                record.Component = payload["component"]?.Value<string>();
            }
        }

        private static void TrimRuntimeDocumentsIfNeeded(string incomingDocumentId)
        {
            if (RuntimeDocuments.ContainsKey(incomingDocumentId) || RuntimeDocuments.Count < MaxRuntimeDocumentRecords) return;
            string oldestId = null;
            var oldestUtc = DateTime.MaxValue;
            foreach (var pair in RuntimeDocuments)
            {
                if (pair.Value.RuntimeReadyUtc >= oldestUtc) continue;
                oldestUtc = pair.Value.RuntimeReadyUtc;
                oldestId = pair.Key;
            }
            if (oldestId != null) RuntimeDocuments.Remove(oldestId);
        }

        private static string ValidateSurfaceIdentity(HtmlUiHost host, string surfaceId, string ownerId)
        {
            if (string.IsNullOrWhiteSpace(surfaceId)) return "ok";
            var surfaces = host?.Surfaces;
            if (surfaces == null) return "surface-manager-unavailable";
            foreach (var surface in surfaces.All)
            {
                if (!string.Equals(surface.Id, surfaceId, StringComparison.OrdinalIgnoreCase)) continue;
                return string.Equals(surface.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase)
                    ? "ok"
                    : "owner expected=" + surface.OwnerId + " reported=" + (ownerId ?? "<null>");
            }
            return "surface-not-registered id=" + surfaceId;
        }

        public static HtmlUiDiagnosticsSnapshot Snapshot()
        {
            var host = HtmlUiService.IsInitialized ? HtmlUiService.Host : null;
            var window = host?.GetWindowState() ?? default(HtmlUiWindowState);
            var page = host?.Pages.Current;
            var bridge = HtmlUiBridge.Current;
            string lastError;
            string runtimeDocumentSummary;
            int runtimeDocumentCount;
            lock (Sync)
            {
                lastError = _lastBrowserError;
                runtimeDocumentCount = RuntimeDocuments.Count;
                var runtimeLines = new System.Text.StringBuilder();
                foreach (var record in RuntimeDocuments.Values)
                {
                    if (runtimeLines.Length > 0) runtimeLines.Append('\n');
                    runtimeLines.Append(record.DocumentId)
                        .Append(" | owner=").Append(record.OwnerId ?? "<none>")
                        .Append(" | page=").Append(record.PageId ?? "<none>")
                        .Append(" | surface=").Append(record.SurfaceId ?? "<none>")
                        .Append(" | runtimeReady=").Append(record.RuntimeReadyUtc.ToString("o"))
                        .Append(" | businessReady=").Append(record.BusinessReadyUtc?.ToString("o") ?? "<pending>")
                        .Append(" | validation=").Append(record.Validation ?? "unknown");
                }
                runtimeDocumentSummary = runtimeLines.ToString();
            }

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
                SurfaceSummary = surfaceSummary,
                RuntimeDocumentCount = runtimeDocumentCount,
                RuntimeDocumentSummary = runtimeDocumentSummary,
                StateDispatchFlushCount = host?.StateFlushCount ?? 0,
                StateUpdateCount = host?.StateUpdateCount ?? 0,
                StateCoalescedCount = host?.StateCoalescedCount ?? 0,
                PendingStateCount = host?.PendingStateCount ?? 0,
                LiveFrameCount = host?.LiveFrameCount ?? 0
            };
        }
    }
}
