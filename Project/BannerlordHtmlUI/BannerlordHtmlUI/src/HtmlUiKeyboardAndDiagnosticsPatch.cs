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
        private static object _controller;
        private static EventInfo _acceleratorEvent;
        private static Delegate _acceleratorHandler;

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
                    form.EscapePressed = () => TryCloseFromEscape(host, "overlay");
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
                    DetachWebViewAccelerator();
                    DetachCoreNavigationHandler();
                    _core = core;
                    AttachWebViewAccelerator(web);
                    core.NavigationCompleted += OnNavigationCompleted;
                }
            }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to install keyboard/diagnostics hook.", ex); }
        }

        public static void Uninstall(HtmlUiHost host)
        {
            if (_filter != null)
            {
                try { Application.RemoveMessageFilter(_filter); } catch { }
                _filter = null;
            }
            DetachWebViewAccelerator();
            DetachCoreNavigationHandler();
            _core = null;
            if (host != null)
            {
                try
                {
                    var field = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                    var form = field?.GetValue(host) as HtmlUiOverlayForm;
                    if (form != null) form.EscapePressed = null;
                }
                catch { }
            }
            if (ReferenceEquals(_host, host)) _host = null;
        }

        private static bool TryCloseFromEscape(HtmlUiHost host, string source)
        {
            if (host == null || !host.IsInputCaptured) return false;
            var page = host.Pages.Current;
            if (page == null || !page.CloseOnEscape) return false;
            try
            {
                HtmlUiLogger.Info("ESC close requested from " + source + ": page=" + page.Id);
                host.Pages.CloseCurrent();
                return true;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("ESC page close failed.", ex);
                return false;
            }
        }

        private static void AttachWebViewAccelerator(WebView2 web)
        {
            if (web == null) return;
            try
            {
                var fields = web.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
                object controller = null;
                foreach (var field in fields)
                {
                    if (!typeof(CoreWebView2Controller).IsAssignableFrom(field.FieldType)) continue;
                    controller = field.GetValue(web);
                    if (controller != null) break;
                }
                if (controller == null) return;
                var eventInfo = controller.GetType().GetEvent("AcceleratorKeyPressed", BindingFlags.Instance | BindingFlags.Public);
                var method = typeof(HtmlUiKeyboardAndDiagnosticsPatch).GetMethod(nameof(OnWebViewAcceleratorKeyPressed), BindingFlags.Static | BindingFlags.NonPublic);
                if (eventInfo == null || method == null) return;
                var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, method);
                eventInfo.AddEventHandler(controller, handler);
                _controller = controller;
                _acceleratorEvent = eventInfo;
                _acceleratorHandler = handler;
            }
            catch (Exception ex) { HtmlUiLogger.Warn("WebView2 accelerator hook failed: " + ex.GetBaseException().Message); }
        }

        private static void DetachWebViewAccelerator()
        {
            if (_controller != null && _acceleratorEvent != null && _acceleratorHandler != null)
            {
                try { _acceleratorEvent.RemoveEventHandler(_controller, _acceleratorHandler); } catch { }
            }
            _controller = null;
            _acceleratorEvent = null;
            _acceleratorHandler = null;
        }

        private static void DetachCoreNavigationHandler()
        {
            if (_core == null) return;
            try { _core.NavigationCompleted -= OnNavigationCompleted; } catch { }
        }

        private static void OnWebViewAcceleratorKeyPressed(object sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
        {
            if (e == null || e.VirtualKey != VkEscape) return;
            if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown && e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown) return;
            var host = _host;
            if (host == null || !host.IsInputCaptured) return;
            var page = host.Pages.Current;
            if (page == null || !page.CloseOnEscape) return;
            e.Handled = TryCloseFromEscape(host, "webview2");
        }

        private static async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var core = sender as CoreWebView2 ?? _core;
            if (!e.IsSuccess || core == null) return;
            const string script = "(() => { try { const selector='[data-bhui-i18n],[data-bhui-i18n-placeholder],[data-bhui-i18n-title],[data-bhui-i18n-alt]'; const items=[...document.querySelectorAll(selector)].map(el=>({key:el.getAttribute('data-bhui-i18n')||el.getAttribute('data-bhui-i18n-placeholder')||el.getAttribute('data-bhui-i18n-title')||el.getAttribute('data-bhui-i18n-alt')||'',text:el.textContent||'',placeholder:el.getAttribute('placeholder')||'',title:el.getAttribute('title')||'',alt:el.getAttribute('alt')||''})); return JSON.stringify({url:location.href,items}); } catch(e){ return JSON.stringify({error:String(e)}); } })();";
            try { var result = await core.ExecuteScriptAsync(script); HtmlUiLogger.Debug("i18n DOM audit: " + result); } catch { }
        }

        private sealed class KeyboardFilter : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WmKeyDown && m.Msg != WmSysKeyDown) return false;
                var host = _host;
                if (host == null || !host.IsInputCaptured) return false;
                if (unchecked((int)m.WParam.ToInt64()) != VkEscape) return false;
                return TryCloseFromEscape(host, "winforms");
            }
        }
    }
}
