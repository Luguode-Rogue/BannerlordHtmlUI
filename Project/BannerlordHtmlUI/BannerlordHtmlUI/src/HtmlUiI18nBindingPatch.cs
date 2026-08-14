using System;
using System.Reflection;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiI18nBindingPatch
    {
        private const string Marker = "__bannerlordHtmlUiI18nBindLifecyclePatched";

        private const string Script = @"
(() => {
  const install = () => {
    const i18n = window.game && window.game.i18n;
    if (!i18n || typeof i18n.bind !== 'function' || i18n.__bannerlordHtmlUiI18nBindLifecyclePatched) return;

    const selector = '[data-bhui-i18n],[data-bhui-i18n-placeholder],[data-bhui-i18n-title],[data-bhui-i18n-alt]';
    const mappings = [
      ['data-bhui-i18n', 'textContent'],
      ['data-bhui-i18n-placeholder', 'placeholder'],
      ['data-bhui-i18n-title', 'title'],
      ['data-bhui-i18n-alt', 'alt']
    ];

    i18n.bind = async (root = document) => {
      const target = root || document;
      const elements = [];
      if (target && typeof target.matches === 'function' && target.matches(selector)) elements.push(target);
      if (target && typeof target.querySelectorAll === 'function') {
        for (const element of target.querySelectorAll(selector)) elements.push(element);
      }

      const bindings = [];
      for (const element of elements) {
        for (const [attribute, property] of mappings) {
          if (!element.hasAttribute(attribute)) continue;
          bindings.push({ element, property, key: element.getAttribute(attribute) });
        }
      }

      let active = true;
      let generation = 0;
      let localeOff = null;
      let pageHideHandler = null;

      const alive = (binding) => {
        if (!active) return false;
        if (binding.element && typeof binding.element.isConnected === 'boolean' && !binding.element.isConnected) return false;
        if (target !== document && target.contains && !target.contains(binding.element)) return false;
        return true;
      };

      const apply = async (currentGeneration) => {
        const jobs = bindings.map(binding =>
          Promise.resolve(i18n.t(binding.key))
            .then(value => {
              if (!active || currentGeneration !== generation || !alive(binding)) return;
              binding.element[binding.property] = value;
            })
            .catch(error => {
              if (active && currentGeneration === generation) {
                console.error('BannerlordHtmlUI i18n bind failed:', error);
              }
            })
        );
        await Promise.all(jobs);
      };

      const dispose = () => {
        if (!active) return;
        active = false;
        generation++;
        if (localeOff) {
          try { localeOff(); } catch (_) {}
          localeOff = null;
        }
        if (pageHideHandler) {
          try { window.removeEventListener('pagehide', pageHideHandler); } catch (_) {}
          pageHideHandler = null;
        }
      };

      localeOff = i18n.onLocaleChanged(() => {
        if (!active) return;
        const currentGeneration = ++generation;
        apply(currentGeneration).catch(error => {
          if (active && currentGeneration === generation) console.error('BannerlordHtmlUI i18n locale refresh failed:', error);
        });
      });

      pageHideHandler = dispose;
      window.addEventListener('pagehide', pageHideHandler, { once: true });

      await apply(generation);
      if (!active) return () => {};
      return dispose;
    };

    i18n['" + Marker + @"'] = true;
  };

  if (window.game && window.game.i18n) install();
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
                HtmlUiLogger.Info("i18n binding lifecycle patch installed.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install i18n binding lifecycle patch.", ex);
            }
        }
    }
}
