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
    if (!game || game['" + Marker + @"'] || typeof game.request !== 'function' || !window.chrome?.webview) return false;

    const activeCancels = new Set();
    const cancellablePending = new Map();
    let nextCancellableId = 1;
    const makeAbortError = (reason, requestName = null) => {
      const error = new Error(reason || 'Request aborted.');
      error.name = 'BannerlordHtmlUiError';
      error.code = 'REQUEST_ABORTED';
      error.raw = error.message;
      error.operation = 'request';
      error.requestName = requestName;
      return error;
    };

    const settleCancellable = (id, ok, payload, error) => {
      const item = cancellablePending.get(id);
      if (!item) return false;
      cancellablePending.delete(id);
      if (item.timer) clearTimeout(item.timer);
      if (item.signal && item.abortHandler) {
        try { item.signal.removeEventListener('abort', item.abortHandler); } catch (_) {}
      }
      activeCancels.delete(item.cancel);
      if (ok) item.resolve(payload);
      else item.reject(error instanceof Error ? error : new Error(String(error || 'Request failed')));
      return true;
    };

    if (!game.__bannerlordHtmlUiRequestCancellationReceivePatched && typeof game.__receive === 'function') {
      const originalReceive = game.__receive;
      game.__receive = function(messageJson) {
        let message = messageJson;
        try { if (typeof messageJson === 'string') message = JSON.parse(messageJson); } catch (_) {}
        if (message && message.type === 'response' && message.id && cancellablePending.has(String(message.id))) {
          settleCancellable(String(message.id), !!message.ok, message.payload, message.error || 'Request failed');
          return;
        }
        return originalReceive.call(this, messageJson);
      };
      game.__bannerlordHtmlUiRequestCancellationReceivePatched = true;
    }

    const sendCancel = id => {
      const item = cancellablePending.get(id);
      if (!item || item.cancelSent) return;
      item.cancelSent = true;
      try {
        window.chrome.webview.postMessage({ version: 1, type: 'cancel', id, name: '', payload: null });
      } catch (_) {}
    };

    const requestCancellableInternal = (name, payload = {}, timeoutMs = 10000, signal = null) => {
      if (signal?.aborted) return Promise.reject(makeAbortError('Request aborted: ' + name, name));
      if (!name) return Promise.reject(new Error('Request name is required.'));

      const id = `c${Date.now()}_${nextCancellableId++}`;
      return new Promise((resolve, reject) => {
        const item = {
          resolve,
          reject,
          timer: null,
          signal,
          abortHandler: null,
          cancel: null,
          cancelSent: false
        };
        item.cancel = () => sendCancel(id);
        item.abortHandler = () => {
          if (!cancellablePending.has(id)) return;
          sendCancel(id);
          settleCancellable(id, false, null, makeAbortError('Request aborted: ' + name, name));
        };
        cancellablePending.set(id, item);
        activeCancels.add(item.cancel);

        const safeTimeout = Math.max(1, Number(timeoutMs) || 10000);
        item.timer = setTimeout(() => {
          if (!cancellablePending.has(id)) return;
          sendCancel(id);
          settleCancellable(id, false, null, new Error('request timeout: ' + name));
        }, safeTimeout);

        if (signal) {
          try { signal.addEventListener('abort', item.abortHandler, { once: true }); } catch (_) {}
          if (signal.aborted) {
            item.abortHandler();
            return;
          }
        }

        try {
          window.chrome.webview.postMessage({ version: 1, type: 'request', id, name, payload });
        } catch (e) {
          settleCancellable(id, false, null, e);
        }
      });
    };

    const patchRequestOwner = owner => {
      if (!owner || typeof owner.request !== 'function' || owner.requestCancellable) return;
      owner.requestCancellable = (name, payload = {}, timeoutMs = 10000, signal = null) =>
        requestCancellableInternal(owner.ownerId ? `${owner.ownerId}.${String(name).replace(/^\.+/, '')}` : name, payload, timeoutMs, signal);
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

    if (!game.__bannerlordHtmlUiRequestCancellationPageLifecycleInstalled) {
      window.addEventListener('pagehide', () => {
        for (const cancel of [...activeCancels]) {
          try { cancel(); } catch (_) {}
        }
        for (const id of [...cancellablePending.keys()])
          settleCancellable(id, false, null, new Error('BannerlordHtmlUI page unloaded'));
        activeCancels.clear();
      }, { once: true });
      game.__bannerlordHtmlUiRequestCancellationPageLifecycleInstalled = true;
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
                HtmlUiLogger.Info("request cancellation patch installed.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install request cancellation patch.", ex);
            }
        }
    }
}
