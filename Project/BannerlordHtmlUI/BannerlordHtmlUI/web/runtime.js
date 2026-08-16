(() => {
  const VERSION = 'runtime-modular-i18n-fix/1';
  const baseUri = (() => {
    try {
      return new URL('.', window.location.href).toString();
    } catch (_) {
      return '';
    }
  })();

  const loadScriptSynchronously = (relativePath) => {
    const uri = new URL(relativePath, baseUri || window.location.href).toString();
    const xhr = new XMLHttpRequest();
    xhr.open('GET', uri, false);
    xhr.send(null);

    if (xhr.status < 200 || xhr.status >= 300) {
      throw new Error(`BannerlordHtmlUI runtime module load failed: ${relativePath} (${xhr.status})`);
    }

    const source = String(xhr.responseText || '') + `\n//# sourceURL=${uri}`;
    (0, eval)(source);
  };

  try {
    window.__bannerlordHtmlUiRuntime = window.__bannerlordHtmlUiRuntime || {};
    window.__bannerlordHtmlUiRuntime.version = VERSION;
    window.__bannerlordHtmlUiRuntime.loadScriptSynchronously = loadScriptSynchronously;

    loadScriptSynchronously('./runtime-bootstrap.js');
    loadScriptSynchronously('./runtime-core.js');
    window.__bannerlordHtmlUiRuntime.runtimeCoreLoaded = true;
    loadScriptSynchronously('./runtime-i18n.js');
  } catch (error) {
    console.error('BannerlordHtmlUI modular runtime bootstrap failed:', error);
    try {
      window.__bannerlordHtmlUiRuntime = window.__bannerlordHtmlUiRuntime || {};
      window.__bannerlordHtmlUiRuntime.bootstrapError = String(error && error.stack || error);
    } catch (_) {}
    throw error;
  }
})();