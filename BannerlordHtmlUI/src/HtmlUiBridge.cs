using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace BannerlordHtmlUI
{
    internal sealed class HtmlUiBridge
    {
        private const int ProtocolVersion = 1;
        private const long PreCanceledRequestTtlMs = 30_000L;
        private const int MaxPreCanceledRequests = 2048;
        private static readonly object CurrentSync = new object();
        private static WeakReference<HtmlUiBridge> _current;
        private readonly HtmlUiHost _host;
        private readonly ConcurrentDictionary<string, RequestEntry> _requests = new ConcurrentDictionary<string, RequestEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CommandEntry> _commands = new ConcurrentDictionary<string, CommandEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _requestCancellation = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, long> _preCanceledRequests = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _activeRequestOwners = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _activeRequestNames = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private CoreWebView2 _web;
        private int _attached;
        private int _disposed;

        private sealed class RequestEntry
        {
            public string OwnerId;
            public Func<JToken, Task<object>> Handler;
            public Func<JToken, CancellationToken, Task<object>> CancellableHandler;
        }

        private sealed class CommandEntry
        {
            public string OwnerId;
            public Action<JToken> Handler;
        }

        public HtmlUiBridge(HtmlUiHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            lock (CurrentSync)
                _current = new WeakReference<HtmlUiBridge>(this);
        }

        internal static HtmlUiBridge Current
        {
            get
            {
                lock (CurrentSync)
                {
                    var weak = _current;
                    if (weak == null) return null;
                    return weak.TryGetTarget(out var bridge) && !bridge.IsDisposed ? bridge : null;
                }
            }
        }

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        public int CommandCount => _commands.Count;
        public int RequestCount => _requests.Count;
        public int ActiveRequestCount => _requestCancellation.Count;

        public void RegisterRequest(string name, Func<JToken, Task<object>> handler) => RegisterRequestCore(name, handler, null, "framework");
        public void RegisterRequest(string name, Func<JToken, CancellationToken, Task<object>> handler) => RegisterRequestCore(name, null, handler, "framework");
        public bool CommandExists(string name) => !IsDisposed && _commands.ContainsKey(name);

        public bool UnregisterCommand(string name) => UnregisterCommand(name, "framework");

        public bool UnregisterCommand(string name, string ownerId)
        {
            if (IsDisposed || string.IsNullOrWhiteSpace(name)) return false;
            if (!_commands.TryGetValue(name, out var existing)) return false;
            if (!string.Equals(existing.OwnerId, NormalizeOwner(ownerId), StringComparison.OrdinalIgnoreCase)) return false;
            return ((ICollection<KeyValuePair<string, CommandEntry>>)_commands).Remove(new KeyValuePair<string, CommandEntry>(name, existing));
        }

        public bool UnregisterRequest(string name) => UnregisterRequest(name, "framework");

        public bool UnregisterRequest(string name, string ownerId)
        {
            if (IsDisposed || string.IsNullOrWhiteSpace(name)) return false;
            if (!_requests.TryGetValue(name, out var existing)) return false;
            if (!string.Equals(existing.OwnerId, NormalizeOwner(ownerId), StringComparison.OrdinalIgnoreCase)) return false;

            CancelRequests(name, existing.OwnerId);
            var removed = ((ICollection<KeyValuePair<string, RequestEntry>>)_requests).Remove(new KeyValuePair<string, RequestEntry>(name, existing));
            CancelRequests(name, existing.OwnerId);
            return removed;
        }

        public bool CancelRequest(string id)
        {
            if (IsDisposed || string.IsNullOrWhiteSpace(id)) return false;
            CleanupPreCanceledRequests();
            if (_requestCancellation.TryGetValue(id, out var cancellation))
            {
                try { cancellation.Cancel(); return true; }
                catch (ObjectDisposedException) { return false; }
            }

            if (_preCanceledRequests.Count >= MaxPreCanceledRequests)
                CleanupPreCanceledRequests(forceTrim: true);
            _preCanceledRequests[id] = GetMonotonicMilliseconds();
            return true;
        }

        public void CancelRequestsByOwner(string ownerId) => CancelRequests(null, ownerId);

        private void CancelRequests(string requestName, string ownerId)
        {
            var normalizedOwner = NormalizeOwner(ownerId);
            foreach (var pair in _activeRequestOwners)
            {
                if (!string.Equals(pair.Value, normalizedOwner, StringComparison.OrdinalIgnoreCase)) continue;
                if (requestName != null && (!_activeRequestNames.TryGetValue(pair.Key, out var activeName) || !string.Equals(activeName, requestName, StringComparison.OrdinalIgnoreCase))) continue;
                if (_requestCancellation.TryGetValue(pair.Key, out var cancellation))
                {
                    try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
                }
            }
        }

        public void CancelAllRequests()
        {
            foreach (var pair in _requestCancellation)
            {
                try { pair.Value.Cancel(); } catch (ObjectDisposedException) { }
            }
            _activeRequestOwners.Clear();
            _activeRequestNames.Clear();
            _preCanceledRequests.Clear();
        }

        public void RegisterCommand(string name, Action<JToken> handler, string ownerId) => RegisterCommandCore(name, handler, ownerId);
        public void RegisterRequest(string name, Func<JToken, Task<object>> handler, string ownerId) => RegisterRequestCore(name, handler, null, ownerId);
        public void RegisterRequest(string name, Func<JToken, CancellationToken, Task<object>> handler, string ownerId) => RegisterRequestCore(name, null, handler, ownerId);

        public int UnregisterByOwner(string ownerId)
        {
            if (IsDisposed || string.IsNullOrWhiteSpace(ownerId)) return 0;
            var normalizedOwner = NormalizeOwner(ownerId);
            var count = 0;
            CancelRequestsByOwner(normalizedOwner);
            foreach (var pair in _commands)
                if (string.Equals(pair.Value.OwnerId, normalizedOwner, StringComparison.OrdinalIgnoreCase) && ((ICollection<KeyValuePair<string, CommandEntry>>)_commands).Remove(pair)) count++;
            foreach (var pair in _requests)
                if (string.Equals(pair.Value.OwnerId, normalizedOwner, StringComparison.OrdinalIgnoreCase) && ((ICollection<KeyValuePair<string, RequestEntry>>)_requests).Remove(pair)) count++;
            CancelRequestsByOwner(normalizedOwner);
            return count;
        }

        public void RegisterCommand(string name, Action<JToken> handler) => RegisterCommandCore(name, handler, "framework");

        private static string NormalizeOwner(string ownerId) => string.IsNullOrWhiteSpace(ownerId) ? "framework" : ownerId;
        private static long GetMonotonicMilliseconds() => Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;

        private void RegisterCommandCore(string name, Action<JToken> handler, string ownerId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Command name is required.", nameof(name));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ownerId = NormalizeOwner(ownerId);
            var entry = new CommandEntry { OwnerId = ownerId, Handler = handler };
            if (_commands.TryAdd(name, entry)) return;
            if (_commands.TryGetValue(name, out var existing)) throw new InvalidOperationException("Command already registered: " + name + " (owner=" + existing.OwnerId + ")");
            throw new InvalidOperationException("Command registration race: " + name);
        }

        private void RegisterRequestCore(string name, Func<JToken, Task<object>> handler, Func<JToken, CancellationToken, Task<object>> cancellableHandler, string ownerId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Request name is required.", nameof(name));
            if (handler == null && cancellableHandler == null) throw new ArgumentNullException(nameof(handler));
            ownerId = NormalizeOwner(ownerId);
            var entry = new RequestEntry { OwnerId = ownerId, Handler = handler, CancellableHandler = cancellableHandler };
            if (_requests.TryAdd(name, entry)) return;
            if (_requests.TryGetValue(name, out var existing)) throw new InvalidOperationException("Request already registered: " + name + " (owner=" + existing.OwnerId + ")");
            throw new InvalidOperationException("Request registration race: " + name);
        }

        private bool IsCurrentCommand(string name, CommandEntry expected) => !IsDisposed && _commands.TryGetValue(name, out var current) && ReferenceEquals(current, expected);
        private bool IsCurrentRequest(string name, RequestEntry expected) => !IsDisposed && _requests.TryGetValue(name, out var current) && ReferenceEquals(current, expected);

        private async Task SendResponseSafelyAsync(string id, object result, string error, string context)
        {
            if (string.IsNullOrWhiteSpace(id) || IsDisposed) return;
            try { await _host.SendResponseAsync(id, result, error).ConfigureAwait(false); }
            catch (Exception ex) { HtmlUiLogger.Debug("Bridge response send failed: " + context + " | " + ex.GetBaseException().Message); }
        }

        private void CleanupPreCanceledRequests(bool forceTrim = false)
        {
            var now = GetMonotonicMilliseconds();
            foreach (var pair in _preCanceledRequests)
                if (forceTrim || now - pair.Value >= PreCanceledRequestTtlMs) _preCanceledRequests.TryRemove(pair.Key, out _);
        }

        public void Attach(CoreWebView2 web)
        {
            ThrowIfDisposed();
            if (web == null) throw new ArgumentNullException(nameof(web));
            if (ReferenceEquals(_web, web) && Volatile.Read(ref _attached) != 0) return;
            Detach();
            _web = web;
            _web.WebMessageReceived += OnWebMessageReceived;
            Volatile.Write(ref _attached, 1);
        }

        public void Detach()
        {
            var web = _web;
            _web = null;
            if (web != null)
            {
                try { web.WebMessageReceived -= OnWebMessageReceived; } catch { }
            }
            Volatile.Write(ref _attached, 0);
            CancelAllRequests();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Detach();
            _commands.Clear();
            _requests.Clear();
            _requestCancellation.Clear();
            _activeRequestOwners.Clear();
            _activeRequestNames.Clear();
            _preCanceledRequests.Clear();
            lock (CurrentSync)
            {
                if (_current != null && _current.TryGetTarget(out var current) && ReferenceEquals(current, this))
                    _current = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(HtmlUiBridge));
        }

        private async void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (IsDisposed || Volatile.Read(ref _attached) == 0) return;
            string id = null;
            try
            {
                var root = JObject.Parse(e.WebMessageAsJson);
                var versionToken = root["version"];
                var version = versionToken?.Type == JTokenType.Integer ? versionToken.Value<int>() : 0;
                id = root["id"]?.Value<string>();
                if (version != ProtocolVersion)
                {
                    HtmlUiLogger.Warn("Bridge protocol mismatch: received=" + version + ", expected=" + ProtocolVersion + ", id=" + (id ?? "<null>"));
                    await SendResponseSafelyAsync(id, null, "Unsupported protocol version: " + version, "protocol mismatch").ConfigureAwait(false);
                    return;
                }

                var type = root["type"]?.Value<string>() ?? string.Empty;
                var name = root["name"]?.Value<string>() ?? string.Empty;
                var payload = root["payload"] ?? JValue.CreateNull();
                if (type == "cancel")
                {
                    if (string.IsNullOrWhiteSpace(id)) return;
                    CancelRequest(id);
                    return;
                }

                if (type == "command")
                {
                    if (string.IsNullOrWhiteSpace(id) && !string.Equals(name, "runtime.error", StringComparison.OrdinalIgnoreCase)) return;
                    if (!_commands.TryGetValue(name, out var commandEntry))
                    {
                        if (!string.IsNullOrWhiteSpace(id)) await SendResponseSafelyAsync(id, null, "Unknown command: " + name, "unknown command").ConfigureAwait(false);
                        return;
                    }

                    _host.DispatchToGameThread(() =>
                    {
                        if (!IsCurrentCommand(name, commandEntry)) { if (!string.IsNullOrWhiteSpace(id)) _ = SendResponseSafelyAsync(id, null, "Command was unregistered before execution: " + name, "stale command"); return; }
                        try
                        {
                            commandEntry.Handler(payload);
                            if (!string.IsNullOrWhiteSpace(id) && IsCurrentCommand(name, commandEntry)) _ = SendResponseSafelyAsync(id, true, null, "command success: " + name);
                        }
                        catch (Exception ex)
                        {
                            HtmlUiLogger.Error("Command failed: " + name, ex);
                            if (!string.IsNullOrWhiteSpace(id)) _ = SendResponseSafelyAsync(id, null, ex.GetBaseException().Message, "command failure: " + name);
                        }
                    });
                    return;
                }

                if (type == "request")
                {
                    if (string.IsNullOrWhiteSpace(id)) return;
                    if (!_requests.TryGetValue(name, out var requestEntry))
                    {
                        await SendResponseSafelyAsync(id, null, "Unknown request: " + name, "unknown request").ConfigureAwait(false);
                        return;
                    }

                    _host.DispatchToGameThread(async () =>
                    {
                        if (!IsCurrentRequest(name, requestEntry)) { await SendResponseSafelyAsync(id, null, "Request was unregistered before execution: " + name, "stale request").ConfigureAwait(false); return; }
                        using (var cancellation = new CancellationTokenSource())
                        {
                            _requestCancellation[id] = cancellation;
                            _activeRequestOwners[id] = requestEntry.OwnerId;
                            _activeRequestNames[id] = name;
                            if (_preCanceledRequests.TryRemove(id, out _)) cancellation.Cancel();
                            try
                            {
                                if (cancellation.IsCancellationRequested) return;
                                object result = requestEntry.CancellableHandler != null
                                    ? await requestEntry.CancellableHandler(payload, cancellation.Token).ConfigureAwait(false)
                                    : await requestEntry.Handler(payload).ConfigureAwait(false);
                                if (cancellation.IsCancellationRequested || IsDisposed) return;
                                if (!IsCurrentRequest(name, requestEntry)) { await SendResponseSafelyAsync(id, null, "Request was unregistered while executing: " + name, "request unregistered").ConfigureAwait(false); return; }
                                await SendResponseSafelyAsync(id, result, null, "request success: " + name).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) { }
                            catch (Exception ex)
                            {
                                HtmlUiLogger.Error("Request failed: " + name, ex);
                                if (!cancellation.IsCancellationRequested && !IsDisposed) await SendResponseSafelyAsync(id, null, ex.GetBaseException().Message, "request failure: " + name).ConfigureAwait(false);
                            }
                            finally
                            {
                                _requestCancellation.TryRemove(id, out _);
                                _activeRequestOwners.TryRemove(id, out _);
                                _activeRequestNames.TryRemove(id, out _);
                                _preCanceledRequests.TryRemove(id, out _);
                            }
                        }
                    });
                    return;
                }

                await SendResponseSafelyAsync(id, null, "Unknown message type: " + type, "unknown type").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Bridge message failed.", ex);
                if (!IsDisposed) await SendResponseSafelyAsync(id, null, ex.GetBaseException().Message, "message handler failure").ConfigureAwait(false);
            }
        }
    }
}
