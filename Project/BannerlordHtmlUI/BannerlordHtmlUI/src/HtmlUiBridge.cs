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

        public HtmlUiBridge(HtmlUiHost host) => _host = host;

        public void RegisterRequest(string name, Func<JToken, Task<object>> handler)
        {
            RegisterRequestCore(name, handler, "framework");
        }

        public bool CommandExists(string name) => _commands.ContainsKey(name);

        public bool UnregisterCommand(string name) => _commands.TryRemove(name, out _);
        public bool UnregisterRequest(string name) => _requests.TryRemove(name, out _);

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
            var count = 0;
            foreach (var pair in _commands)
                if (string.Equals(pair.Value.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase) && _commands.TryRemove(pair.Key, out _)) count++;
            foreach (var pair in _requests)
                if (string.Equals(pair.Value.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase) && _requests.TryRemove(pair.Key, out _)) count++;
            return count;
        }

        public void RegisterCommand(string name, Action<JToken> handler)
        {
            RegisterCommandCore(name, handler, "framework");
        }

        private void RegisterCommandCore(string name, Action<JToken> handler, string ownerId)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Command name is required.", nameof(name));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (string.IsNullOrWhiteSpace(ownerId)) ownerId = "framework";

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
            if (string.IsNullOrWhiteSpace(ownerId)) ownerId = "framework";

            var entry = new RequestEntry { OwnerId = ownerId, Handler = handler };
            if (_requests.TryAdd(name, entry)) return;

            if (_requests.TryGetValue(name, out var existing))
                throw new InvalidOperationException("Request already registered: " + name + " (owner=" + existing.OwnerId + ")");

            throw new InvalidOperationException("Request registration race: " + name);
        }

        public void Attach(CoreWebView2 web) => web.WebMessageReceived += OnWebMessageReceived;

        private async void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string id = null;
            try
            {
                var root = JObject.Parse(e.WebMessageAsJson);
                if (root["version"]?.Value<int>() != ProtocolVersion)
                    return;

                var type = root["type"]?.Value<string>() ?? string.Empty;
                id = root["id"]?.Value<string>();
                var name = root["name"]?.Value<string>() ?? string.Empty;
                var payload = root["payload"] ?? JValue.CreateNull();

                if (type == "command")
                {
                    // Runtime diagnostics are fire-and-forget and legitimately have no request id.
                    if (string.IsNullOrWhiteSpace(id) && !string.Equals(name, "runtime.error", StringComparison.OrdinalIgnoreCase))
                        return;

                    if (!_commands.TryGetValue(name, out var commandEntry))
                    {
                        if (!string.IsNullOrWhiteSpace(id))
                            await _host.SendResponseAsync(id, null, "Unknown command: " + name).ConfigureAwait(false);
                        return;
                    }

                    _host.DispatchToGameThread(() =>
                    {
                        try
                        {
                            commandEntry.Handler(payload);
                            if (!string.IsNullOrWhiteSpace(id))
                                _ = _host.SendResponseAsync(id, true, null);
                        }
                        catch (Exception ex)
                        {
                            HtmlUiLogger.Error("Command failed: " + name, ex);
                            if (!string.IsNullOrWhiteSpace(id))
                                _ = _host.SendResponseAsync(id, null, ex.GetBaseException().Message);
                        }
                    });
                    return;
                }

                if (type == "request")
                {
                    if (string.IsNullOrWhiteSpace(id)) return;
                    if (!_requests.TryGetValue(name, out var requestEntry))
                    {
                        await _host.SendResponseAsync(id, null, "Unknown request: " + name).ConfigureAwait(true);
                        return;
                    }

                    _host.DispatchToGameThread(async () =>
                    {
                        try
                        {
                            var result = await requestEntry.Handler(payload).ConfigureAwait(false);
                            await _host.SendResponseAsync(id, result, null).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            HtmlUiLogger.Error("Request failed: " + name, ex);
                            await _host.SendResponseAsync(id, null, ex.GetBaseException().Message).ConfigureAwait(false);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Bridge message failed.", ex);
                if (!string.IsNullOrWhiteSpace(id))
                    await _host.SendResponseAsync(id, null, ex.GetBaseException().Message).ConfigureAwait(false);
            }
        }
    }
}
