using System;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiKeyboardAndDiagnosticsPatch
    {
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private const int VkEscape = 0x1B;

        private static HtmlUiHost _host;
        private static IMessageFilter _filter;
        private static CoreWebView2 _core;
        private static WebView2 _web;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;

            _host = host;
            try
            {
                var formField = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                var form = formField?.GetValue(host) as HtmlUiOverlayForm;
                if (form != null)
                {
                    form.EscapePressed = () =>
                    {
                        try
                        {
                            HtmlUiLogger.Info("ESC callback received by overlay form. Closing current page.");
                            host.Pages.CloseCurrent();
                        }
                        catch (Exception ex)
                        {
                            HtmlUiLogger.Error("ESC overlay callback failed.", ex);
                        }
                    };
                    HtmlUiLogger.Info("Overlay ESC callback wired.");
                }

                if (_filter == null)
                {
                    _filter = new KeyboardFilter();
                    Application.AddMessageFilter(_filter);
                    HtmlUiLogger.Info("Global UI ESC close filter installed.");
                }

                var webField = typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
                var web = webField?.GetValue(host) as WebView2;
                var core = web?.CoreWebView2;
                if (core != null && !ReferenceEquals(_core, core))
                {
                    if (_web != null)
                    {
                        try { _web.AcceleratorKeyPressed -= OnWebViewAcceleratorKeyPressed; }
                        catch { }
                    }

                    _web = web;
                    _core = core;
                    _web.AcceleratorKeyPressed += OnWebViewAcceleratorKeyPressed;
                    core.NavigationCompleted += OnNavigationCompleted;
                    HtmlUiLogger.Info("UI navigation/accelerator diagnostics hooks installed for current WebView2 instance.");
                }

                InstallRuntimeStateRemovalPatch(host);
                HtmlUiWindowTrackingPatch.Install(host);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install ESC/i18n diagnostics hook.", ex);
            }
        }

        public static void Uninstall(HtmlUiHost host)
        {
            if (_filter != null)
            {
                try { Application.RemoveMessageFilter(_filter); }
                catch (Exception ex) { HtmlUiLogger.Debug("Failed to remove global UI ESC close filter: " + ex.GetBaseException().Message); }
                _filter = null;
            }

            if (_web != null)
            {
                try { _web.AcceleratorKeyPressed -= OnWebViewAcceleratorKeyPressed; }
                catch { }
                _web = null;
            }

            if (_core != null)
            {
                try { _core.NavigationCompleted -= OnNavigationCompleted; }
                catch { }
                _core = null;
            }

            if (host != null)
            {
                try
                {
                    var formField = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                    var form = formField?.GetValue(host) as HtmlUiOverlayForm;
                    if (form != null) form.EscapePressed = null;
                }
                catch { }
            }

            if (ReferenceEquals(_host, host)) _host = null;
            HtmlUiLogger.Info("Global UI ESC close diagnostics uninstalled.");
        }

        private static void OnWebViewAcceleratorKeyPressed(object sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
        {
            if (e == null || e.VirtualKey != VkEscape) return;
            if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown &&
                e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown) return;

            var host = _host;
            if (host == null || !host.IsVisible) return;

            try
            {
                e.Handled = true;
                HtmlUiLogger.Info("WebView2 AcceleratorKeyPressed detected Escape. Closing current page: "
                    + (host.Pages.CurrentId ?? "<null>"));
                host.Pages.CloseCurrent();
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("WebView2 Escape close failed.", ex);
            }
        }

        private static void InstallRuntimeStateRemovalPatch(HtmlUiHost host)
        {
            try
            {
                var webField = typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
                var web = webField?.GetValue(host) as WebView2;
                var core = web?.CoreWebView2;
                if (core == null)
                {
                    HtmlUiLogger.Warn("State removal runtime patch deferred: CoreWebView2 is not ready.");
                    return;
                }

                const string script = @"
(() => {
  try {
    // runtime.js keeps its state Map private. Capture that Map from its early
    // framework.* writes, then restore Map.prototype.set immediately so this
    // patch does not permanently affect unrelated page code.
    const originalMapSet = Map.prototype.set;
    let stateMap = null;
    let restored = false;

    Map.prototype.set = function(key, value) {
      if (!stateMap && typeof key === 'string' && key.indexOf('framework.') === 0) {
        stateMap = this;
        if (!restored) {
          Map.prototype.set = originalMapSet;
          restored = true;
        }
      }
      return originalMapSet.call(this, key, value);
    };

    const game = window.game;
    if (!game || typeof game.__receive !== 'function') {
      if (!restored) Map.prototype.set = originalMapSet;
      return;
    }

    const originalReceive = game.__receive;
    game.__receive = function(messageJson) {
      try {
        const msg = typeof messageJson === 'string' ? JSON.parse(messageJson) : messageJson;
        if (msg && msg.type === 'event' && typeof msg.name === 'string' && msg.name.indexOf('state-remove:') === 0) {
          const key = msg.name.substring(13);
          if (stateMap) stateMap.delete(key);
        }
      } catch (_) {}
      return originalReceive(messageJson);
    };
  } catch (_) {}
})();";

                core.AddScriptToExecuteOnDocumentCreatedAsync(script);
                HtmlUiLogger.Info("Runtime state removal patch installed.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install runtime state removal patch.", ex);
            }
        }

        private static async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var core = sender as CoreWebView2 ?? _core;
            if (!e.IsSuccess || core == null) return;

            const string script = @"
(() => {
  try {
    const selector='[data-bhui-i18n],[data-bhui-i18n-placeholder],[data-bhui-i18n-title],[data-bhui-i18n-alt]';
    const items=[...document.querySelectorAll(selector)].map(el=>({
      key:el.getAttribute('data-bhui-i18n')||el.getAttribute('data-bhui-i18n-placeholder')||el.getAttribute('data-bhui-i18n-title')||el.getAttribute('data-bhui-i18n-alt')||'',
      text:el.textContent||'',
      placeholder:el.getAttribute('placeholder')||'',
      title:el.getAttribute('title')||'',
      alt:el.getAttribute('alt')||''
    }));
    const locale=window.game&&window.game.i18n?window.game.i18n.locale:null;
    return JSON.stringify({url:location.href,locale,items});
  } catch(e) {
    return JSON.stringify({error:String(e)});
  }
})();";

            try
            {
                var result = await core.ExecuteScriptAsync(script);
                HtmlUiLogger.Info("i18n DOM audit: " + result);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("i18n DOM audit failed.", ex);
            }
        }

        private sealed class KeyboardFilter : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WmKeyDown && m.Msg != WmSysKeyDown) return false;

                var host = _host;
                if (host == null || !host.IsVisible) return false;

                var key = unchecked((int)m.WParam.ToInt64());
                if (key != VkEscape) return false;

                try
                {
                    HtmlUiLogger.Info("UI keyboard close detected: key=Escape"
                        + ", currentPage=" + (host.Pages.CurrentId ?? "<null>")
                        + ", inputMode=" + host.InputMode);
                    host.Pages.CloseCurrent();
                    HtmlUiLogger.Info("UI keyboard Escape close completed. currentPage="
                        + (host.Pages.CurrentId ?? "<null>")
                        + ", inputMode=" + host.InputMode
                        + ", hostVisible=" + host.IsVisible);
                    return true;
                }
                catch (Exception ex)
                {
                    HtmlUiLogger.Error("UI keyboard Escape close failed.", ex);
                    return false;
                }
            }
        }
    }
}
