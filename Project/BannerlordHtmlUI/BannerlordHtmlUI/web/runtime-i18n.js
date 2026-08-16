(() => {
  const runtime = window.__bannerlordHtmlUiRuntime || {};
  const game = window.game;
  if (!game || typeof game.request !== 'function') {
    throw new Error('BannerlordHtmlUI i18n module loaded before the runtime core.');
  }

  const legacy = game.i18n || null;
  try { legacy?.dispose?.(); } catch (_) {}

  const localeListeners = new Set();
  const cache = new Map();
  let locale = null;
  let disposed = false;
  let localeOff = null;

  const ensureActive = () => {
    if (disposed) throw new Error('BannerlordHtmlUI i18n runtime is disposed.');
  };

  const emitLocale = payload => {
    if (disposed) return;
    locale = payload && payload.language ? payload.language : locale;
    cache.clear();
    for (const handler of [...localeListeners]) {
      try { handler(locale); } catch (error) { console.error(error); }
    }
  };

  const t = async (key, variables = null, options = {}) => {
    ensureActive();
    if (!key) return '';
    const cacheKey = `${key}|${JSON.stringify(variables || {})}|${options.fallbackLanguage || ''}`;
    if (cache.has(cacheKey)) return cache.get(cacheKey);
    const result = await game.request('framework.i18n.translate', {
      key,
      variables,
      fallbackLanguage: options.fallbackLanguage || null
    });
    ensureActive();
    const value = result && result.text != null ? String(result.text) : String(key);
    cache.set(cacheKey, value);
    return value;
  };

  const getLocale = async () => {
    ensureActive();
    if (locale) return locale;
    const result = await game.request('framework.i18n.getLocale', {});
    ensureActive();
    locale = result && result.language ? result.language : null;
    return locale;
  };

  const getLanguages = () => {
    ensureActive();
    return game.request('framework.i18n.getLanguages', {});
  };

  const formatDate = value => {
    ensureActive();
    return game.request('framework.i18n.formatDate', {
      value: new Date(value).toISOString()
    }).then(result => result && result.text);
  };

  const formatTime = value => {
    ensureActive();
    return game.request('framework.i18n.formatTime', {
      value: new Date(value).toISOString()
    }).then(result => result && result.text);
  };

  const bind = async (root = document) => {
    ensureActive();
    const elements = root.querySelectorAll
      ? root.querySelectorAll('[data-bhui-i18n],[data-bhui-i18n-placeholder],[data-bhui-i18n-title],[data-bhui-i18n-alt]')
      : [];
    const jobs = [];
    const mappings = [
      ['data-bhui-i18n', 'textContent'],
      ['data-bhui-i18n-placeholder', 'placeholder'],
      ['data-bhui-i18n-title', 'title'],
      ['data-bhui-i18n-alt', 'alt']
    ];

    for (const element of elements) {
      for (const [attribute, property] of mappings) {
        if (!element.hasAttribute(attribute)) continue;
        const key = element.getAttribute(attribute);
        jobs.push(t(key).then(value => { element[property] = value; }));
      }
    }

    await Promise.all(jobs);
    ensureActive();
    return () => {};
  };

  const i18n = {
    get locale() { return locale; },
    getLocale,
    getLanguages,
    t,
    bind,
    formatDate,
    formatTime,
    onLocaleChanged(handler) {
      ensureActive();
      if (typeof handler !== 'function') throw new Error('A locale handler is required.');
      localeListeners.add(handler);
      return () => localeListeners.delete(handler);
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      try { localeOff?.(); } catch (_) {}
      localeOff = null;
      localeListeners.clear();
      cache.clear();
      if (game.i18n === i18n) game.i18n = null;
    }
  };

  localeOff = game.on('framework.i18n.localeChanged', emitLocale);
  game.i18n = i18n;
  window.addEventListener('pagehide', () => i18n.dispose(), { once: true });
  runtime.i18nModuleLoaded = true;
})();