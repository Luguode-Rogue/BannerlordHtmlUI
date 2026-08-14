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
  const install = () => {
    if (!window.game || window.game[""__bannerlordHtmlUiStateBootstrapInstalled""] || typeof window.game.request !== 'function') return;

    const hydrate = async () => {
      try {
        const snapshot = await window.game.request('framework.getStateSnapshot', {});
        if (!snapshot || typeof snapshot !== 'object' || typeof window.game.__receive !== 'function') return;
        for (const [key, value] of Object.entries(snapshot)) {
          window.game.__receive({
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
