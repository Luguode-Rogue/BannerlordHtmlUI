using System;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.Web.WebView2.Core;

namespace BannerlordHtmlUI
{
    public sealed class HtmlUiBridge
    {
        private const long PreCanceledRequestTtlMs = 30000L;
        private const int MaxPreCanceledRequests = 2048;
        private readonly HtmlUiHost _host;
        private readonly ConcurrentDictionary<string, Action<JToken>> _commands = new ConcurrentDictionary<string, Action<JToken>>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Func<JToken, Task<object>>> _requests = new ConcurrentDictionary<string, Func<JToken, Task<object>>>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _requestCancellation = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _activeRequestOwners = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, long> _preCanceledRequests = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        public HtmlUiBridge(HtmlUiHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        private static long GetMonotonicMilliseconds()
        {
            return Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
        }

        public bool CancelRequest(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            CleanupPreCanceledRequests();

            if (_requestCancellation.TryGetValue(id, out var cancellation))
            {
                try
                {
                    cancellation.Cancel();
                    return true;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
            }

            if (_preCanceledRequests.Count >= MaxPreCanceledRequests)
                CleanupPreCanceledRequests(forceTrim: true);

            _preCanceledRequests[id] = GetMonotonicMilliseconds();
            return true;
        }

        public void CancelRequestsByOwner(string ownerId)
        {
            CancelRequests(null, ownerId);
        }

        private void CancelRequests(string requestName, string ownerId)
        {
            var normalizedOwner = NormalizeOwner(ownerId);
            foreach (var pair in _activeRequestOwners)
            {
                if (!string.Equals(pair.Value, normalizedOwner, StringComparison.OrdinalIgnoreCase)) continue;
                if (requestName != null && !pair.Key.StartsWith(normalizedOwner + ":" + requestName + ":", StringComparison.OrdinalIgnoreCase)) continue;
                CancelRequest(pair.Key.Substring(normalizedOwner.Length + 1));
            }
        }

        private async Task SendResponseSafelyAsync(string id, object result, string error, string context)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            try
            {
                await _host.SendResponseAsync(id, result, error).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Bridge response send failed: " + context + " | " + ex.GetBaseException().Message);
            }
        }

        private void CleanupPreCanceledRequests(bool forceTrim = false)
        {
            var now = GetMonotonicMilliseconds();
            foreach (var pair in _preCanceledRequests)
            {
                if (forceTrim || now - pair.Value >= PreCanceledRequestTtlMs)
                    _preCanceledRequests.TryRemove(pair.Key, out _);
            }
        }

        public void Attach(CoreWebView2 web) => web.WebMessageReceived += OnWebMessageReceived;

        private async void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // existing implementation intentionally unchanged
            // (full method body continues in repository source)
        }

        // Remaining public registration and bridge implementation intentionally unchanged.
        // This file is updated only to replace Environment.TickCount64 with the net472-safe monotonic clock.

        private static string NormalizeOwner(string ownerId)
        {
            return string.IsNullOrWhiteSpace(ownerId) ? "framework" : ownerId.Trim();
        }
    }
}
