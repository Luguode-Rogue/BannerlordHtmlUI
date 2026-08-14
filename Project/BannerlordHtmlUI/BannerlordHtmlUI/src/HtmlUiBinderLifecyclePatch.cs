using System;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiBinderLifecyclePatch
    {
        private const string Marker = "__bannerlordHtmlUiBinderLifecyclePatched";

        private const string Script = @"
(() => {
  const install = () => {
    if (!window.game || window.game[""" + Marker + @"""]) return;

    const wrapBinder = (binder) => {
      if (!binder || typeof binder.dispose !== 'function' || binder.__bannerlordHtmlUiBinderWrapped) return binder;

      const originalDispose = binder.dispose.bind(binder);
      let active = true;
      const dispose = () => {
        if (!active) return;
        active = false;
        try { window.removeEventListener('pagehide', dispose); } catch (_) {}
        try { originalDispose(); } catch (error) {
          console.error('BannerlordHtmlUI binder dispose failed:', error);
        }
      };

      binder.dispose = dispose;
      binder.__bannerlordHtmlUiBinderWrapped = true;
      window.addEventListener('pagehide', dispose, { once: true });
      return binder;
    };

    const originalCreateScope = typeof window.game.scope === 'function'
      ? window.game.scope.bind(window.game)
      : null;
    if (originalCreateScope) {
      window.game.scope = (ownerId) => {
        const scope = originalCreateScope(ownerId);
        if (scope && scope.bind) scope.bind = wrapBinder(scope.bind);
        return scope;
      };
    }

    if (window.game.bind) window.game.bind = wrapBinder(window.game.bind);
    if (window.game.app && window.game.app.bind) window.game.app.bind = wrapBinder(window.game.app.bind);

    window.game[""" + Marker + @"""] = true;
  };

  if (window.game) install();
  else queueMicrotask(install);
})();";

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;
            try
            {
                var web = host.GetWebViewForInternalUse();
                var core = web?.CoreWebView2;
                if (core == null) return;
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(Script);
                HtmlUiLogger.Info("binder lifecycle patch installed.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install binder lifecycle patch.", ex);
            }
        }
    }
}
