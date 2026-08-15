using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiKeyboardAndDiagnosticsPatch
    {
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private const int VkEscape = 0x1B;
        private const int VkF12 = 0x7B;

        private static HtmlUiHost _host;
        private static IMessageFilter _filter;
        private static CoreWebView2 _core;
        private static bool _diagnosticsHooked;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;

            _host = host;
            if (_filter == null)
            {
                _filter = new KeyboardFilter();
                Application.AddMessageFilter(_filter);
                HtmlUiLogger.Info("Global UI keyboard close filter installed.");
            }

            try
            {
                var field = typeof(HtmlUiHost).GetField("_web", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var web = field?.GetValue(host) as Microsoft.Web.WebView2.WinForms.WebView2;
                var core = web?.CoreWebView2;
                if (core != null && !_diagnosticsHooked)
                {
                    _core = core;
                    _diagnosticsHooked = true;
                    core.NavigationCompleted += OnNavigationCompleted;
                    HtmlUiLogger.Info("UI navigation diagnostics hook installed.");
                }
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install keyboard/i18n diagnostics hook.", ex);
            }
        }

        private static async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess || _core == null) return;

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
                var result = await _core.ExecuteScriptAsync(script);
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
                if (host == null || !host.IsVisible || host.InputMode != HtmlUiInputMode.Captured) return false;

                var key = unchecked((int)m.WParam.ToInt64());
                if (key != VkF12 && key != VkEscape) return false;

                try
                {
                    HtmlUiLogger.Info("UI keyboard close detected: key=" + (key == VkF12 ? "F12" : "Escape")
                        + ", currentPage=" + (host.Pages.CurrentId ?? "<null>")
                        + ", inputMode=" + host.InputMode);
                    host.Pages.CloseCurrent();
                    return true;
                }
                catch (Exception ex)
                {
                    HtmlUiLogger.Error("UI keyboard close failed.", ex);
                    return false;
                }
            }
        }
    }
}
