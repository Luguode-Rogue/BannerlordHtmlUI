using System;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiStateBootstrapPatch
    {
        private const string Script = @"
(() => {
  const tryInstall = () => {
    const game = window.game;
    if (!game || game['__bannerlordHtmlUiStateBootstrapInstalled'] || typeof game.request !== 'function') return false;

    const hydrate = async () => {
      try {
        const snapshot = await game.request('framework.getStateSnapshot', {});
        if (!snapshot || typeof snapshot !== 'object' || typeof game.__receive !== 'function') return;
        for (const [key, value] of Object.entries(snapshot)) {
          game.__receive({
            version: 1,
            type: 'event',
            name: `state:${key}`,
            payload: value
          });
        }
      } catch (error) {
        try { console.error('BannerlordHtmlUI state bootstrap failed:', error); } catch (_) {}
      }
    };

    game['__bannerlordHtmlUiStateBootstrapInstalled'] = true;
    queueMicrotask(() => { hydrate(); });
    return true;
  };

  if (tryInstall()) return;

  // The framework runtime may be injected after document-created scripts run.
  // Poll briefly rather than relying on a single microtask, otherwise the first
  // navigation can permanently miss State hydration.
  let attempts = 0;
  const retry = () => {
    if (tryInstall()) return;
    if (++attempts >= 100) return;
    setTimeout(retry, 25);
  };
  setTimeout(retry, 0);
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
