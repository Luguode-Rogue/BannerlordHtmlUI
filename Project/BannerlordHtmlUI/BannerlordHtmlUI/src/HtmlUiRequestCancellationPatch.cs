using System;
using System.Reflection;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiRequestCancellationPatch
    {
        private const string Marker = "__bannerlordHtmlUiRequestCancellationPatched";

        private const string Script = @"
(() => {
  const install = () => {
    const game = window.game;
    if (!game || game[\"" + Marker + @"\"] || typeof game.request !== 'function' || !window.chrome?.webview) return false;

    const makeAbortError = (reason) => {
      const error = new Error(reason || 'Request aborted.');
      error.name = 'BannerlordHtmlUiError';
      error.code = 'REQUEST_ABORTED';
      error.raw = error.message;
      error.operation = 'request';
      error.requestName = null;
      return error;
    };

    const patchRequestOwner = owner => {
      if (!owner || typeof owner.request !== 'function' || owner.requestCancellable) return;
      const originalRequest = owner.request.bind(owner);

      owner.requestCancellable = (name, payload = {}, timeoutMs = 10000, signal = null) => {
        if (signal?.aborted) return Promise.reject(makeAbortError('Request aborted: ' + name));

        const webview = window.chrome.webview;
        const originalPostMessage = webview.postMessage;
        let requestId = null;

        webview.postMessage = function(message) {
          if (!requestId && message && message.version === 1 && message.type === 'request' && message.id && message.name === name)
            requestId = String(message.id);
          return originalPostMessage.call(this, message);
        };

        let requestPromise;
        try {
          requestPromise = originalRequest(name, payload, timeoutMs);
        } finally {
          webview.postMessage = originalPostMessage;
        }

        let settled = false;
        let timeoutHandle = null;
        let abortHandler = null;

        const sendCancel = () => {
          if (settled || !requestId) return;
          try {
            originalPostMessage.call(webview, {
              version: 1,
              type: 'cancel',
              id: requestId,
              name: '',
              payload: null
            });
          } catch (_) {}
        };

        const cleanup = () => {
          settled = true;
          if (timeoutHandle) clearTimeout(timeoutHandle);
          if (signal && abortHandler) {
            try { signal.removeEventListener('abort', abortHandler); } catch (_) {}
          }
        };

        if (signal) {
          abortHandler = () => sendCancel();
          signal.addEventListener('abort', abortHandler, { once: true });
        }

        const safeTimeout = Math.max(1, Number(timeoutMs) || 10000);
        timeoutHandle = setTimeout(sendCancel, safeTimeout + 25);

        return new Promise((resolve, reject) => {
          requestPromise.then(resolve, reject).finally(cleanup);
          if (signal) {
            abortHandler = () => {
              sendCancel();
              reject(makeAbortError('Request aborted: ' + name));
            };
            signal.removeEventListener('abort', abortHandler);
            signal.addEventListener('abort', abortHandler, { once: true });
          }
        });
      };
    };

    patchRequestOwner(game);
    patchRequestOwner(game.app);

    if (typeof game.scope === 'function' && !game.__bannerlordHtmlUiRequestCancellationScopePatched) {
      const originalScope = game.scope;
      game.scope = (...args) => {
        const scope = originalScope(...args);
        patchRequestOwner(scope);
        return scope;
      };
      game.__bannerlordHtmlUiRequestCancellationScopePatched = true;
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
                HtmlUiLogger.Info("request cancellation patch installed.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install request cancellation patch.", ex);
            }
        }
    }
}
