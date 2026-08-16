using System;
using System.Reflection;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiErrorModelPatch
    {
        private const string Marker = "__bannerlordHtmlUiErrorModelPatched";
        private const string Script = @"
(() => {
  const install = () => {
    const game = window.game;
    if (!game || game['" + Marker + @"']) return false;

    const classify = (operation, raw) => {
      const message = String(raw || 'Bridge operation failed.');
      if (/^command timeout:/i.test(message)) return 'COMMAND_TIMEOUT';
      if (/^request timeout:/i.test(message)) return 'REQUEST_TIMEOUT';
      if (/^Unknown command:/i.test(message)) return 'COMMAND_UNKNOWN';
      if (/^Unknown request:/i.test(message)) return 'REQUEST_UNKNOWN';
      if (/^Command was unregistered before execution:/i.test(message)) return 'COMMAND_STALE';
      if (/^Command was unregistered while executing:/i.test(message)) return 'COMMAND_UNREGISTERED';
      if (/^Request was unregistered before execution:/i.test(message)) return 'REQUEST_STALE';
      if (/^Request was unregistered while executing:/i.test(message)) return 'REQUEST_UNREGISTERED';
      if (/^Unsupported protocol version:/i.test(message)) return 'PROTOCOL_UNSUPPORTED_VERSION';
      if (/^Unknown message type:/i.test(message)) return 'PROTOCOL_UNKNOWN_TYPE';
      if (/runtime is disposed/i.test(message)) return 'RUNTIME_DISPOSED';
      if (/page unloaded/i.test(message)) return 'PAGE_UNLOADED';
      if (operation === 'command') return 'COMMAND_HANDLER_ERROR';
      if (operation === 'request') return 'REQUEST_HANDLER_ERROR';
      return 'BRIDGE_ERROR';
    };

    const decorate = (operation, name, error) => {
      if (error && error.name === 'BannerlordHtmlUiError' && error.code) return error;
      const raw = error && error.message != null ? String(error.message) : String(error || 'Bridge operation failed.');
      const target = error instanceof Error ? error : new Error(raw);
      target.name = 'BannerlordHtmlUiError';
      target.code = classify(operation, raw);
      target.raw = raw;
      target.operation = operation;
      target.requestName = name || null;
      return target;
    };

    const wrap = (api, operation) => {
      if (!api || typeof api[operation] !== 'function') return;
      const marker = '__bannerlordHtmlUiErrorWrapped_' + operation;
      if (api[marker]) return;
      const original = api[operation];
      const wrapped = function(name, payload, timeoutMs) {
        let result;
        try { result = original.call(this, name, payload, timeoutMs); }
        catch (error) { throw decorate(operation === 'call' ? 'command' : 'request', name, error); }
        return Promise.resolve(result).catch(error => {
          throw decorate(operation === 'call' ? 'command' : 'request', name, error);
        });
      };
      wrapped[marker] = true;
      api[operation] = wrapped;
    };

    const wrapCancellable = api => {
      if (!api || typeof api.requestCancellable !== 'function' || api.__bannerlordHtmlUiErrorWrapped_requestCancellable) return;
      const original = api.requestCancellable;
      const wrapped = function(name, payload, timeoutMs, signal) {
        let result;
        try { result = original.call(this, name, payload, timeoutMs, signal); }
        catch (error) { throw decorate('request', name, error); }
        return Promise.resolve(result).catch(error => {
          throw decorate('request', name, error);
        });
      };
      wrapped.__bannerlordHtmlUiErrorWrapped_requestCancellable = true;
      api.requestCancellable = wrapped;
    };

    const installApi = api => {
      if (!api || api['" + Marker + @"']) return;
      wrap(api, 'call');
      wrap(api, 'request');
      wrapCancellable(api);
      Object.defineProperty(api, 'BannerlordHtmlUiError', { value: Error, configurable: false });
      Object.defineProperty(api, '" + Marker + @"', { value: true, configurable: false });
    };

    installApi(game);
    installApi(game.app);

    if (!game.__bannerlordHtmlUiErrorScopeWrapped && typeof game.scope === 'function') {
      const originalScope = game.scope;
      game.scope = function (...args) {
        const scope = originalScope.apply(this, args);
        installApi(scope);
        return scope;
      };
      Object.defineProperty(game, '__bannerlordHtmlUiErrorScopeWrapped', { value: true, configurable: false });
    }

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
                var core = web?.CoreWebView2;
                if (core == null) return;
                _ = core.AddScriptToExecuteOnDocumentCreatedAsync(Script);
                HtmlUiLogger.Info("bridge error model patch installed.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install bridge error model patch.", ex);
            }
        }
    }
}
