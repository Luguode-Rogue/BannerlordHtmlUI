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
      const bindings = [];
      const bindingIndex = new WeakMap();
      let active = true;
      let generation = 0;
      let localeOff = null;
      let pageHideHandler = null;
      let observer = null;
      let translationCache = new Map();

      const alive = (binding) => {
        if (!active) return false;
        if (!binding.element || !binding.element.isConnected) return false;
        if (target !== document && target.contains && !target.contains(binding.element)) return false;
        return true;
      };

      const getTranslation = (key) => {
        const cacheKey = String(key || '');
        if (!translationCache.has(cacheKey)) {
          translationCache.set(cacheKey, Promise.resolve(i18n.t(cacheKey)));
        }
        return translationCache.get(cacheKey);
      };

      const addBinding = (element, property, key) => {
        if (!element || !key) return;
        let properties = bindingIndex.get(element);
        if (!properties) {
          properties = new Map();
          bindingIndex.set(element, properties);
        }
        const existing = properties.get(property);
        if (existing && existing.key === key) return;
        if (existing) {
          const index = bindings.indexOf(existing);
          if (index >= 0) bindings.splice(index, 1);
        }
        const binding = { element, property, key };
        properties.set(property, binding);
        bindings.push(binding);

        const currentGeneration = generation;
        getTranslation(key)
          .then(value => {
            if (!active || currentGeneration !== generation || !alive(binding)) return;
            binding.element[binding.property] = value;
          })
          .catch(error => {
            if (active && currentGeneration === generation) {
              console.error('BannerlordHtmlUI i18n bind failed:', error);
            }
          });
      };

      const scanElement = (element) => {
        if (!element || element.nodeType !== 1) return;
        for (const [attribute, property] of mappings) {
          if (element.hasAttribute(attribute)) {
            addBinding(element, property, element.getAttribute(attribute));
          }
        }
      };

      const scanRoot = (scanTarget) => {
        if (!scanTarget) return;
        if (scanTarget.nodeType === 1) scanElement(scanTarget);
        if (typeof scanTarget.querySelectorAll === 'function') {
          for (const element of scanTarget.querySelectorAll(selector)) scanElement(element);
        }
      };

      const apply = async (currentGeneration) => {
        const jobs = bindings.map(binding =>
          getTranslation(binding.key)
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
        if (observer) {
          try { observer.disconnect(); } catch (_) {}
          observer = null;
        }
        translationCache.clear();
        bindings.length = 0;
      };

      scanRoot(target);

      localeOff = i18n.onLocaleChanged(() => {
        if (!active) return;
        translationCache.clear();
        const currentGeneration = ++generation;
        apply(currentGeneration).catch(error => {
          if (active && currentGeneration === generation) console.error('BannerlordHtmlUI i18n locale refresh failed:', error);
        });
      });

      pageHideHandler = dispose;
      window.addEventListener('pagehide', pageHideHandler, { once: true });

      if (typeof MutationObserver === 'function') {
        observer = new MutationObserver(mutations => {
          if (!active) return;
          let changed = false;
          for (const mutation of mutations) {
            if (mutation.type === 'childList') {
              for (const node of mutation.addedNodes) {
                scanRoot(node);
                changed = true;
              }
            } else if (mutation.type === 'attributes' && mutation.target) {
              scanElement(mutation.target);
              changed = true;
            }
          }
          if (changed) apply(generation).catch(error => {
            if (active) console.error('BannerlordHtmlUI i18n dynamic refresh failed:', error);
          });
        });
        observer.observe(target, {
          childList: true,
          subtree: true,
          attributes: true,
          attributeFilter: mappings.map(pair => pair[0])
        });
      }

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
