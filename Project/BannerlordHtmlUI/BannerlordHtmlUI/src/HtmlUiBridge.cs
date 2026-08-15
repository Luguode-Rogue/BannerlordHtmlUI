using System;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace BannerlordHtmlUI
{
    internal sealed class HtmlUiBridge
    {
        private const int ProtocolVersion = 1;
        private static WeakReference<HtmlUiBridge> _current;
        private readonly HtmlUiHost _host;
        private readonly ConcurrentDictionary<string, RequestEntry> _requests =
            new ConcurrentDictionary<string, RequestEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CommandEntry> _commands =
            new ConcurrentDictionary<string, CommandEntry>(StringComparer.OrdinalIgnoreCase);

        private sealed class RequestEntry
        {
            public string OwnerId;
            public Func<JToken, Task<object>> Handler;
        }

        private sealed class CommandEntry
        {
            public string OwnerId;
            public Action<JToken> Handler;
        }

        public HtmlUiBridge(HtmlUiHost host)
        {
            _host = host;
            _current = new WeakReference<HtmlUiBridge>(this);
        }

        internal static HtmlUiBridge Current
        {
            get
            {
                var weak = _current;
                if (weak == null) return null;
                return weak.TryGetTarget(out var bridge) ? bridge : null;
            }
        }

        public int CommandCount => _commands.Count;
        public int RequestCount => _requests.Count;

        public void RegisterRequest(string name, Func<JToken, Task<object>> handler)
        {
            RegisterRequestCore(name, handler, "framework");
        }

        public bool CommandExists(string name) => _commands.ContainsKey(name);

        public bool UnregisterCommand(string name)
        {
            return UnregisterCommand(name, "framework");
        }

        public bool UnregisterCommand(string name, string ownerId)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (!_commands.TryGetValue(name, out var existing)) return false;
            if (!string.Equals(existing.OwnerId, NormalizeOwner(ownerId), StringComparison.OrdinalIgnoreCase)) return false;
            return ((ICollection<KeyValuePair<string, CommandEntry>>)_commands).Remove(
                new KeyValuePair<string, CommandEntry>(name, existing));
        }

        public bool UnregisterRequest(string name)
        {
            return UnregisterRequest(name, "framework");
        }

        public bool UnregisterRequest(string name, string ownerId)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (!_requests.TryGetValue(name, out var existing)) return false;
            if (!string.Equals(existing.OwnerId, NormalizeOwner(ownerId), StringComparison.OrdinalIgnoreCase)) return false;
            return ((ICollection<KeyValuePair<string, RequestEntry>>)_requests).Remove(
                new KeyValuePair<string, RequestEntry>(name, existing));
        }

        public void RegisterCommand(string name, Action<JToken> handler, string ownerId)
        {
            RegisterCommandCore(name, handler, ownerId);
        }

        public void RegisterRequest(string name, Func<JToken, Task<object>> handler, string ownerId)
        {
            RegisterRequestCore(name, handler, ownerId);
        }

        public int UnregisterByOwner(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) return 0;
            var normalizedOwner = NormalizeOwner(ownerId);
            var count = 0;

            foreach (var pair in _commands)
            {
                if (!string.Equals(pair.Value.OwnerId, normalizedOwner, StringComparison.OrdinalIgnoreCase)) continue;
                if (((ICollection<KeyValuePair<string, CommandEntry>>)_commands).Remove(pair)) count++;
            }

            foreach (var pair in _requests)
            {
                if (!string.Equals(pair.Value.OwnerId, normalizedOwner, StringComparison.OrdinalIgnoreCase)) continue;
                if (((ICollection<KeyValuePair<string, RequestEntry>>)_requests).Remove(pair)) count++;
            }

            return count;
        }

        public void RegisterCommand(string name, Action<JToken> handler)
        {
            RegisterCommandCore(name, handler, "framework");
        }

        private static string NormalizeOwner(string ownerId)
        {
            return string.IsNullOrWhiteSpace(ownerId) ? "framework" : ownerId;
        }

        private void RegisterCommandCore(string name, Action<JToken> handler, string ownerId)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Command name is required.", nameof(name));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ownerId = NormalizeOwner(ownerId);

            var entry = new CommandEntry { OwnerId = ownerId, Handler = handler };
            if (_commands.TryAdd(name, entry)) return;

            if (_commands.TryGetValue(name, out var existing))
                throw new InvalidOperationException("Command already registered: " + name + " (owner=" + existing.OwnerId + ")");

            throw new InvalidOperationException("Command registration race: " + name);
        }

        private void RegisterRequestCore(string name, Func<JToken, Task<object>> handler, string ownerId)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Request name is required.", nameof(name));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ownerId = NormalizeOwner(ownerId);

            var entry = new RequestEntry { OwnerId = ownerId, Handler = handler };
            if (_requests.TryAdd(name, entry)) return;

            if (_requests.TryGetValue(name, out var existing))
                throw new InvalidOperationException("Request already registered: " + name + " (owner=" + existing.OwnerId + ")");

            throw new InvalidOperationException("Request registration race: " + name);
        }

        private bool IsCurrentCommand(string name, CommandEntry expected)
        {
            return _commands.TryGetValue(name, out var current) && ReferenceEquals(current, expected);
        }

        private bool IsCurrentRequest(string name, RequestEntry expected)
        {
            return _requests.TryGetValue(name, out var current) && ReferenceEquals(current, expected);
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

        public void Attach(CoreWebView2 web) => web.WebMessageReceived += OnWebMessageReceived;

        private async void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
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

                if (type == "command")
                {
                    if (string.IsNullOrWhiteSpace(id) && !string.Equals(name, "runtime.error", StringComparison.OrdinalIgnoreCase))
                    {
                        HtmlUiLogger.Warn("Bridge command missing id: " + name);
                        return;
                    }

                    if (!_commands.TryGetValue(name, out var commandEntry))
                    {
                        if (!string.IsNullOrWhiteSpace(id))
                            await SendResponseSafelyAsync(id, null, "Unknown command: " + name, "unknown command").ConfigureAwait(false);
                        else
                            HtmlUiLogger.Warn("Unknown fire-and-forget command: " + name);
                        return;
                    }

                    _host.DispatchToGameThread(() =>
                    {
                        if (!IsCurrentCommand(name, commandEntry))
                        {
                            HtmlUiLogger.Debug("Skipped stale command callback: " + name);
                            if (!string.IsNullOrWhiteSpace(id))
                                _ = SendResponseSafelyAsync(id, null, "Command was unregistered before execution: " + name, "stale command");
                            return;
                        }

                        try
                        {
                            commandEntry.Handler(payload);
                            if (!string.IsNullOrWhiteSpace(id) && IsCurrentCommand(name, commandEntry))
                                _ = SendResponseSafelyAsync(id, true, null, "command success: " + name);
                            else if (!string.IsNullOrWhiteSpace(id))
                                _ = SendResponseSafelyAsync(id, null, "Command was unregistered while executing: " + name, "command unregistered");
                        }
                        catch (Exception ex)
                        {
                            HtmlUiLogger.Error("Command failed: " + name, ex);
                            if (!string.IsNullOrWhiteSpace(id))
                                _ = SendResponseSafelyAsync(id, null, ex.GetBaseException().Message, IsCurrentCommand(name, commandEntry) ? "command failure: " + name : "stale command failure: " + name);
                        }
                    });
                    return;
                }

                if (type == "request")
                {
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        HtmlUiLogger.Warn("Bridge request missing id: " + name);
                        return;
                    }

                    if (!_requests.TryGetValue(name, out var requestEntry))
                    {
                        await SendResponseSafelyAsync(id, null, "Unknown request: " + name, "unknown request").ConfigureAwait(false);
                        return;
                    }

                    _host.DispatchToGameThread(async () =>
                    {
                        if (!IsCurrentRequest(name, requestEntry))
                        {
                            HtmlUiLogger.Debug("Skipped stale request callback: " + name);
                            await SendResponseSafelyAsync(id, null, "Request was unregistered before execution: " + name, "stale request").ConfigureAwait(false);
                            return;
                        }

                        try
                        {
                            var result = await requestEntry.Handler(payload).ConfigureAwait(false);
                            if (!IsCurrentRequest(name, requestEntry))
                            {
                                HtmlUiLogger.Debug("Dropped response from unregistered request: " + name);
                                await SendResponseSafelyAsync(id, null, "Request was unregistered while executing: " + name, "request unregistered").ConfigureAwait(false);
                                return;
                            }
                            await SendResponseSafelyAsync(id, result, null, "request success: " + name).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            HtmlUiLogger.Error("Request failed: " + name, ex);
                            if (IsCurrentRequest(name, requestEntry))
                                await SendResponseSafelyAsync(id, null, ex.GetBaseException().Message, "request failure: " + name).ConfigureAwait(false);
                            else
                                await SendResponseSafelyAsync(id, null, ex.GetBaseException().Message, "stale request failure: " + name).ConfigureAwait(false);
                        }
                    });
                    return;
                }

                HtmlUiLogger.Warn("Bridge message has unknown type: " + type + ", name=" + name + ", id=" + (id ?? "<null>"));
                await SendResponseSafelyAsync(id, null, "Unknown message type: " + type, "unknown type").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Bridge message failed.", ex);
                await SendResponseSafelyAsync(id, null, ex.GetBaseException().Message, "message handler failure").ConfigureAwait(false);
            }
        }
    }
}
