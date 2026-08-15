using System;
using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Completes the JS-facing semantics of HtmlUiStateStore.Remove().
    ///
    /// The framework protocol already emits state-remove:&lt;key&gt;. Older runtime
    /// versions do not consume that event, so this compatibility patch normalizes
    /// the public game.state API without changing the validated runtime core.
    /// </summary>
    internal static class HtmlUiStateRemovalPatch
    {
        private const string Marker = "__bannerlordHtmlUiStateRemovalPatched";
        private const string Script = @"
(() => {
  const install = () => {
    const game = window.game;
    if (!game || game['" + Marker + @"'] || typeof game.__receive !== 'function' || !game.state) return false;

    const originalReceive = game.__receive;
    const originalGet = typeof game.state.get === 'function' ? game.state.get.bind(game.state) : null;
    const originalHas = typeof game.state.has === 'function' ? game.state.has.bind(game.state) : null;
    const originalSnapshot = typeof game.state.snapshot === 'function' ? game.state.snapshot.bind(game.state) : null;
    const removed = new Set();

    game.__receive = function(messageJson) {
      let message = messageJson;
      try { if (typeof messageJson === 'string') message = JSON.parse(messageJson); } catch (_) {}

      if (message && message.type === 'event' && typeof message.name === 'string' && message.name.startsWith('state-remove:')) {
        const key = message.name.substring('state-remove:'.length);
        if (key) {
          removed.add(key);
          // Preserve existing listener semantics: listeners still receive a null
          // update, while the public state API below exposes the key as absent.
          try {
            originalReceive.call(this, {
              version: message.version || 1,
              type: 'event',
              name: 'state:' + key,
              payload: null
            });
          } catch (_) {}
        }
        return;
      }

      if (message && message.type === 'event' && typeof message.name === 'string' && message.name.startsWith('state:')) {
        removed.delete(message.name.substring('state:'.length));
      }

      return originalReceive.call(this, messageJson);
    };

    game.state.get = key => removed.has(String(key)) ? undefined : (originalGet ? originalGet(key) : undefined);
    game.state.has = key => removed.has(String(key)) ? false : (originalHas ? originalHas(key) : false);
    game.state.snapshot = () => {
      const snapshot = originalSnapshot ? originalSnapshot() : {};
      for (const key of removed) delete snapshot[key];
      return snapshot;
    };

    game['" + Marker + @"'] = true;
    return true;
  };

  if (install()) return;
  let attempts = 0;
  const retry = () => {
    if (install()) return;
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
                if (web == null || web.IsDisposed) return;

                void InstallOnUiThread()
                {
                    try
                    {
                        var core = web.CoreWebView2;
                        if (core == null) return;
                        _ = core.AddScriptToExecuteOnDocumentCreatedAsync(Script);
                        _ = core.ExecuteScriptAsync(Script);
                        HtmlUiLogger.Info("state removal compatibility patch installed.");
                    }
                    catch (Exception ex)
                    {
                        HtmlUiLogger.Error("Failed to install state removal compatibility patch on UI thread.", ex);
                    }
                }

                if (web.InvokeRequired)
                    web.BeginInvoke((Action)InstallOnUiThread);
                else
                    InstallOnUiThread();
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install state removal compatibility patch.", ex);
            }
        }
    }
}
