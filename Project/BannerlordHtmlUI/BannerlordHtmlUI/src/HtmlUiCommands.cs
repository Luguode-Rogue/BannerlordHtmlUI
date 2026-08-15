using System;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Convenience surface for framework consumers. Keeps consumers away from HtmlUiHost internals.
    /// Prefer HtmlUiConsumerScope for owner-scoped registrations.
    /// </summary>
    public static class HtmlUiCommands
    {
        public static void RegisterCommand(string name, Action<JToken> handler) => HtmlUiService.RegisterCommand(name, handler);
        public static void RegisterRequest(string name, Func<JToken, Task<object>> handler) => HtmlUiService.RegisterRequest(name, handler);
        public static void RegisterRequest(string name, Func<JToken, CancellationToken, Task<object>> handler) => HtmlUiService.RegisterRequest(name, handler);
        public static void Publish(string name, object payload) => HtmlUiService.SendEvent(name, payload);
        public static bool CommandExists(string name) => HtmlUiService.Host.CommandExists(name);
    }
}
