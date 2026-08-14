using System;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiStateBootstrapPatch
    {
        private const string Marker = "__bannerlordHtmlUiStateBootstrapInstalled";

        private const string Script = @"
(() => {
  const install = () => {
    if (!window.game || window.game[""__bannerlordHtmlUiStateBootstrapInstalled""]) return;

    const hydrate = async () => {
      try {
        if (!window.game || !window.game.state || typeof window.game.request !== 'function') return;
        const snapshot = await window.game.request('framework.stateSnapshot', {});
        if (!snapshot || typeof snapshot !== 'object') return;
        for (const [key, value] of Object.entries(snapshot)) {
          window.game.__receive({
            type: 'event',
            name: `state:${key}`,
            payload: value,
            version: 1
          });
        }
      } catch (error) {
        try { console.error('BannerlordHtmlUI state bootstrap failed:', error); } catch (_) {}
      }
    };

    window.game[""__bannerlordHtmlUiStateBootstrapInstalled""] = true;
    queueMicrotask(() => { hydrate(); });
  };

  if (window.game) install();
  else queueMicrotask(install);
})();";

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;
            try
            {
                var field = typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
                var web = field?.GetValue(host) as WebView2;
                var core = web?.CoreWebView2;
                if (core == null) return;
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(Script);
                HtmlUiLogger.Info("state bootstrap patch installed.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install state bootstrap patch.", ex);
            }
        }
    }
}
