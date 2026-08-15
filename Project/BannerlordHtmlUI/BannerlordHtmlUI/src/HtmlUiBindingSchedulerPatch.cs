using System;
using System.Reflection;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiBindingSchedulerPatch
    {
        private const string Marker = "__bannerlordHtmlUiBindingSchedulerPatched";

        private const string Script = @"
(() => {
  const install = () => {
    const game = window.game;
    if (!game || game[\"" + Marker + @"\"] || !game.bind) return false;

    const schedulerFactory = () => {
      const debounceTimers = new WeakMap();
      const throttleState = new WeakMap();

      const clearFor = element => {
        if (!element) return;
        const debounce = debounceTimers.get(element);
        if (debounce) clearTimeout(debounce);
        debounceTimers.delete(element);

        const throttle = throttleState.get(element);
        if (throttle?.timer) clearTimeout(throttle.timer);
        throttleState.delete(element);
      };

      const schedule = (writer, value, event, element, options) => {
        if (typeof writer !== 'function') return;
        const debounceMs = Math.max(0, Number(options?.debounce || 0));
        const throttleMs = Math.max(0, Number(options?.throttle || 0));
        if (debounceMs <= 0 && throttleMs <= 0) {
          writer(value, event, element);
          return;
        }

        if (debounceMs > 0) {
          const prior = debounceTimers.get(element);
          if (prior) clearTimeout(prior);
          const timer = setTimeout(() => {
            debounceTimers.delete(element);
            try { writer(value, event, element); } catch (e) { console.error(e); }
          }, debounceMs);
          debounceTimers.set(element, timer);
          return;
        }

        let state = throttleState.get(element);
        const now = Date.now();
        if (!state) {
          state = { last: 0, timer: null, queued: null };
          throttleState.set(element, state);
        }
        const run = (v, ev) => {
          state.last = Date.now();
          state.queued = null;
          try { writer(v, ev, element); } catch (e) { console.error(e); }
        };
        if (now - state.last >= throttleMs) {
          if (state.timer) { clearTimeout(state.timer); state.timer = null; }
          run(value, event);
          return;
        }
        state.queued = { value, event };
        if (!state.timer) {
          state.timer = setTimeout(() => {
            state.timer = null;
            if (state.queued) run(state.queued.value, state.queued.event);
          }, throttleMs - (now - state.last));
        }
      };

      return {
        wrapWriter(writer, options) {
          return (value, event, element) => schedule(writer, value, event, element, options || {});
        },
        clearFor
      };
    };

    const patchBinder = binder => {
      if (!binder || binder.__bannerlordHtmlUiBindingSchedulerPatched) return binder;
      if (typeof binder.twoWayValue !== 'function' && typeof binder.twoWayChecked !== 'function') return binder;

      const scheduler = schedulerFactory();
      const originalValue = binder.twoWayValue;
      const originalChecked = binder.twoWayChecked;

      if (typeof originalValue === 'function') {
        binder.twoWayValue = (element, key, writer, options = {}) => {
          let target = element;
          if (typeof target === 'string') target = document.querySelector(target);
          const wrapped = scheduler.wrapWriter(writer, options);
          const safeOptions = { ...options, debounce: 0, throttle: 0 };
          const dispose = originalValue.call(binder, element, key, wrapped, safeOptions);
          return () => {
            scheduler.clearFor(target);
            try { dispose?.(); } catch (_) {}
          };
        };
      }

      if (typeof originalChecked === 'function') {
        binder.twoWayChecked = (element, key, writer, options = {}) => {
          let target = element;
          if (typeof target === 'string') target = document.querySelector(target);
          const wrapped = scheduler.wrapWriter(writer, options);
          const safeOptions = { ...options, debounce: 0, throttle: 0 };
          const dispose = originalChecked.call(binder, element, key, wrapped, safeOptions);
          return () => {
            scheduler.clearFor(target);
            try { dispose?.(); } catch (_) {}
          };
        };
      }

      binder.__bannerlordHtmlUiBindingSchedulerPatched = true;
      return binder;
    };

    patchBinder(game.bind);
    patchBinder(game.app && game.app.bind);

    if (typeof game.scope === 'function') {
      const originalScope = game.scope;
      game.scope = (...args) => patchBinderOnScope(originalScope(...args));
      function patchBinderOnScope(scope) {
        if (scope && scope.bind) patchBinder(scope.bind);
        return scope;
      }
    }

    game[\"" + Marker + @"\"] = true;
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
                var core = web?.CoreWebView2;
                if (core == null) return;
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(Script);
                HtmlUiLogger.Info("binding scheduler patch installed.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install binding scheduler patch.", ex);
            }
        }
    }
}
