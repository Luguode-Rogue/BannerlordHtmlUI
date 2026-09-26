using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// v2 Coexist half of the Page/Surface coexistence strategy.
    ///
    /// While a Page owns the host, the WebView2 document is the page itself and the shell
    /// (which normally mounts surfaces) is not loaded. This document-created script mounts
    /// surfaces that opted into CoexistWithPage directly into the page document as transparent,
    /// non-interactive iframes. In the shell document it is a no-op (shell.js owns mounting).
    /// </summary>
    internal static class HtmlUiCoexistHost
    {
        private const string Script = @"
(() => {
  // Document-created scripts also run in every child frame. Only the top-level
  // page may host surfaces; otherwise each HUD can mount the other HUDs again.
  if (window !== window.top) return;

  const MARK = '__bannerlordHtmlUiCoexistHost';
  if (window[MARK]) return;

  const mounted = new Map();
  let container = null;
  let probeScheduled = false;

  const ensureContainer = () => {
    if (container && container.isConnected) return container;
    if (!document.body) return null;
    container = document.createElement('div');
    container.id = 'bh-coexist-host';
    // Fullscreen overlay host. pointer-events:none keeps page input untouched; each
    // coexist surface is Passive by contract, so it never needs mouse events here.
    container.style.cssText = 'position:fixed;left:0;top:0;right:0;bottom:0;pointer-events:none;z-index:2147483000;overflow:hidden;';
    document.body.appendChild(container);
    return container;
  };

  const pickUri = s => (s.coexistUris && s.coexistUris[location.host]) || s.uri || '';

  const apply = (list) => {
    const want = new Map();
    for (const s of (Array.isArray(list) ? list : [])) {
      if (!s || !s.visible || s.suppressed || !s.enabled) continue;
      if (!s.coexistWithPage || !(pickUri(s))) continue;
      want.set(String(s.id).toLowerCase(), s);
    }

    for (const [key, rec] of Array.from(mounted)) {
      if (!want.has(key)) {
        try { rec.iframe.remove(); } catch (_) {}
        mounted.delete(key);
      }
    }

    for (const [key, s] of want) {
      const uri = pickUri(s);
      let rec = mounted.get(key);
      if (!rec) {
        const host = ensureContainer();
        if (!host) return;
        const iframe = document.createElement('iframe');
        iframe.setAttribute('data-bh-coexist', s.id);
        iframe.style.cssText = 'position:absolute;left:0;top:0;width:100%;height:100%;border:0;background:transparent;pointer-events:none;';
        iframe.src = uri;
        host.appendChild(iframe);
        rec = { iframe, uri, surface: s };
        mounted.set(key, rec);
      }
      rec.surface = s;
      if (rec.uri !== uri) { rec.uri = uri; rec.iframe.src = uri; }
      rec.iframe.style.zIndex = String(Number(s.zIndex) || 100);
    }

    if (mounted.size > 0 && !probeScheduled) {
      probeScheduled = true;
      setTimeout(() => {
        probeScheduled = false;
        for (const [key, rec] of mounted) {
          let info;
          try {
            const doc = rec.iframe.contentDocument;
            const frameWindow = doc && doc.defaultView;
            const frameGame = frameWindow && frameWindow.game;
            info = 'src=' + String(rec.uri).split('?')[0] +
              ' sameOrigin=' + (doc ? 'yes' : 'no') +
              ' readyState=' + (doc ? doc.readyState : 'n/a') +
              ' hasGame=' + (!!frameGame) +
              ' expectedOwner=' + (rec.surface.ownerId || '<none>') +
              ' reportedOwner=' + (frameGame ? (frameGame.ownerId || '<none>') : '<none>') +
              ' expectedSurface=' + (rec.surface.id || '<none>') +
              ' reportedSurface=' + (frameGame && frameGame.surface ? (frameGame.surface.id || '<none>') : '<none>') +
              ' bg=' + (doc ? getComputedStyle(doc.body).backgroundColor : 'n/a');
          } catch (e) { info = 'probe failed: ' + e; }
          try {
            window.game.call('runtime.error', { kind: 'coexist-probe', message: key + ' | ' + info });
          } catch (_) {}
        }
      }, 3000);
    }

    if (mounted.size === 0 && container && container.isConnected) {
      try { container.remove(); } catch (_) {}
      container = null;
    }
  };

  const isShellDocument = () => !!document.getElementById('ui-root');

  const start = () => {
    const game = window.game;
    if (!game || typeof game.on !== 'function' || typeof game.state !== 'object') return false;
    if (isShellDocument()) return true; // shell.js owns mounting in the shell document

    try {
      if (typeof game.state.watch === 'function') game.state.watch('framework.surfaces', apply);
      else {
        game.on('state:framework.surfaces', payload => apply(payload));
        const hydrate = () => { try { apply(game.state.get('framework.surfaces')); } catch (_) {} };
        if (typeof game.ready === 'function') game.ready().then(hydrate).catch(hydrate);
        else hydrate();
      }
    } catch (_) {}

    window[MARK] = true;
    return true;
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => { if (!window[MARK]) start(); }, { once: true });
  } else {
    start();
  }
})();";

        public static async Task InstallAsync(HtmlUiHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var field = typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
            var web = field?.GetValue(host) as WebView2;
            var core = web?.CoreWebView2 ?? throw new InvalidOperationException("CoreWebView2 is not ready for coexist host installation.");
            await core.AddScriptToExecuteOnDocumentCreatedAsync(Script);
            HtmlUiLogger.Info("coexist surface host installed.");
        }
    }
}
