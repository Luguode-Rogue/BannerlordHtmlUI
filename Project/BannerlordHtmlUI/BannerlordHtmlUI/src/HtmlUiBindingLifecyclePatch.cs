using System;
using System.Reflection;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiBindingLifecyclePatch
    {
        private const string Script = @"
(() => {
  const BINDER_MARKER = '__bannerlordHtmlUiBindingLifecyclePatched';
  const SCOPE_MARKER = '__bannerlordHtmlUiBindingScopePatched';
  const registry = new Set();

  const safeDispose = disposer => {
    try { disposer?.(); } catch (error) { console.error('BannerlordHtmlUI binding disposer failed:', error); }
  };

  const trackDisposer = (binder, disposer) => {
    if (typeof disposer !== 'function') return disposer;
    let active = true;
    const tracked = () => {
      if (!active) return;
      active = false;
      binder.__bannerlordHtmlUiBindingManaged?.delete(tracked);
      safeDispose(disposer);
    };
    binder.__bannerlordHtmlUiBindingManaged.add(tracked);
    return tracked;
  };

  const trackComponent = (binder, handle) => {
    if (!handle || typeof handle.dispose !== 'function') return handle;
    const originalDispose = handle.dispose;
    let active = true;
    const trackedDispose = () => {
      if (!active) return;
      active = false;
      binder.__bannerlordHtmlUiBindingManaged?.delete(trackedDispose);
      safeDispose(() => originalDispose.call(handle));
    };
    handle.dispose = trackedDispose;
    binder.__bannerlordHtmlUiBindingManaged.add(trackedDispose);
    return handle;
  };

  const wrapBinder = binder => {
    if (!binder || binder[BINDER_MARKER]) return binder;

    const originalDispose = binder.dispose;
    const managed = new Set();
    Object.defineProperty(binder, '__bannerlordHtmlUiBindingManaged', {
      value: managed,
      configurable: true
    });
    registry.add(binder);

    for (const methodName of ['list', 'template']) {
      const original = binder[methodName];
      if (typeof original !== 'function') continue;
      binder[methodName] = function (...args) {
        return trackDisposer(binder, original.apply(this, args));
      };
    }

    const originalComponent = binder.component;
    if (typeof originalComponent === 'function') {
      binder.component = function (...args) {
        return trackComponent(binder, originalComponent.apply(this, args));
      };
    }

    binder.dispose = function () {
      const pending = [...managed];
      managed.clear();
      for (const disposer of pending) safeDispose(disposer);
      safeDispose(() => originalDispose?.call(binder));
      registry.delete(binder);
    };

    Object.defineProperty(binder, BINDER_MARKER, {
      value: true,
      configurable: false
    });
    return binder;
  };

  const install = () => {
    const game = window.game;
    if (!game) return;

    wrapBinder(game.bind);
    if (game.app) wrapBinder(game.app.bind);

    if (!game[SCOPE_MARKER] && typeof game.scope === 'function') {
      const originalScope = game.scope;
      game.scope = function (...args) {
        const scope = originalScope.apply(this, args);
        if (scope && scope.bind) wrapBinder(scope.bind);
        return scope;
      };
      Object.defineProperty(game, SCOPE_MARKER, {
        value: true,
        configurable: false
      });
    }

    if (!game.__bannerlordHtmlUiBindingPagehidePatched) {
      window.addEventListener('pagehide', () => {
        for (const binder of [...registry]) safeDispose(() => binder.dispose());
        registry.clear();
      }, { once: true });
      Object.defineProperty(game, '__bannerlordHtmlUiBindingPagehidePatched', {
        value: true,
        configurable: false
      });
    }
  };

  if (window.game && window.game.bind) install();
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
                HtmlUiLogger.Info("binding lifecycle patch installed.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install binding lifecycle patch.", ex);
            }
        }
    }
}
