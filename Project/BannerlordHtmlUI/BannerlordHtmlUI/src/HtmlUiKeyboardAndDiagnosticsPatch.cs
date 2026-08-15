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
                    _core = core;
                    core.NavigationCompleted += OnNavigationCompleted;
                    HtmlUiLogger.Info("UI navigation diagnostics hook installed for current WebView2 instance.");
                }

                HtmlUiWindowTrackingPatch.Install(host);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to install ESC/i18n diagnostics hook.", ex);
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
                if (host == null || !host.IsVisible || host.InputMode != HtmlUiInputMode.Captured) return false;

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
