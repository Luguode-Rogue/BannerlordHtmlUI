using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiHostCancellableExtensions
    {
        public static void RegisterRequest(
            this HtmlUiHost host,
            string name,
            Func<JToken, CancellationToken, Task<object>> handler)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var bridge = HtmlUiBridge.Current;
            if (bridge == null) throw new InvalidOperationException("HTML UI bridge is not ready.");
            bridge.RegisterRequest(name, handler);
        }

        public static void RegisterRequest(
            this HtmlUiHost host,
            string name,
            Func<JToken, CancellationToken, Task<object>> handler,
            string ownerId)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var bridge = HtmlUiBridge.Current;
            if (bridge == null) throw new InvalidOperationException("HTML UI bridge is not ready.");
            bridge.RegisterRequest(name, handler, ownerId);
        }
    }
}
