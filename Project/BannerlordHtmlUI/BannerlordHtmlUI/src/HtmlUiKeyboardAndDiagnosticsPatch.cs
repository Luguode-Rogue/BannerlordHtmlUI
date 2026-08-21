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
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonUp = 0x0202;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;
        private const int WmMButtonDown = 0x0207;
        private const int WmMButtonUp = 0x0208;
        private const int WmMouseWheel = 0x020A;
        private const int VkEscape = 0x1B;
        private const int VkF12 = 0x7B;
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
                    HtmlUiLogger.Info("Global UI ESC/F12 safety filter installed.");
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
            // ESC is a page lifecycle control, not a permission check on input mode.
            // If a page explicitly opts into CloseOnEscape, allow the Framework fallback to close it
            // even while the active mode is Passive (e.g. a consumer temporarily hands input back).
            if (host == null) return false;
            var page = host.Pages.Current;
            if (page == null || !page.CloseOnEscape) return false;
            try
            {
                HtmlUiInputTraceLogger.Event("ESC_CLOSE_ATTEMPT source=" + source + " page=" + page.Id + " inputMode=" + host.InputMode);
                HtmlUiLogger.Info("ESC close requested from " + source + ": page=" + page.Id);
                host.Pages.CloseCurrent();
                HtmlUiInputTraceLogger.Event("ESC_CLOSE_RESULT source=" + source + " current=" + (host.Pages.CurrentId ?? "<null>") + " inputMode=" + host.InputMode);
                return true;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("ESC page close failed.", ex);
                HtmlUiInputTraceLogger.Event("ESC_CLOSE_ERROR source=" + source + " " + ex.GetBaseException().Message);
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
            if (e == null) return;
            if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown && e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown) return;

            var host = _host;
            if (host == null) return;

            if (e.VirtualKey == VkF12 && !host.DevToolsEnabled)
            {
                // F12 is only a Framework safety block while HTML owns keyboard input.
                // Passive and MouseCaptured must let the game receive keyboard diagnostics.
                if (host.InputMode == HtmlUiInputMode.Captured)
                {
                    HtmlUiInputTraceLogger.Event("WEBVIEW_ACCELERATOR F12 blocked; DevTools disabled by Framework policy");
                    e.Handled = true;
                }
                return;
            }

            if (e.VirtualKey != VkEscape) return;
            HtmlUiInputTraceLogger.Event("WEBVIEW_ACCELERATOR vk=0x" + e.VirtualKey.ToString("X2") + " kind=" + e.KeyEventKind);
            var page = host.Pages.Current;
            if (page == null || !page.CloseOnEscape) return;
            e.Handled = TryCloseFromEscape(host, "webview2");
            HtmlUiInputTraceLogger.Event("WEBVIEW_ACCELERATOR_RESULT handled=" + e.Handled);
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
                switch (m.Msg)
                {
                    case WmKeyDown:
                    case WmSysKeyDown:
                    case WmKeyUp:
                    case WmSysKeyUp:
                        HtmlUiInputTraceLogger.KeyMessage(m.Msg, m.WParam.ToInt64(), m.LParam.ToInt64(), "winforms-filter");
                        if (m.Msg != WmKeyDown && m.Msg != WmSysKeyDown) return false;
                        var host = _host;
                        if (host == null) return false;
                        var key = unchecked((int)m.WParam.ToInt64());
                        if (key == VkF12 && !host.DevToolsEnabled)
                        {
                            // The Framework only owns the keyboard in Captured mode.
                            // MouseCaptured and Passive must not suppress the game's F12.
                            if (host.InputMode == HtmlUiInputMode.Captured)
                            {
                                HtmlUiInputTraceLogger.Event("WINFORMS F12 blocked; DevTools disabled by Framework policy");
                                return true;
                            }
                            return false;
                        }
                        if (key != VkEscape) return false;
                        return TryCloseFromEscape(host, "winforms");

                    case WmLButtonDown:
                    case WmLButtonUp:
                    case WmRButtonDown:
                    case WmRButtonUp:
                    case WmMButtonDown:
                    case WmMButtonUp:
                    case WmMouseWheel:
                        HtmlUiInputTraceLogger.MouseMessage(m.Msg, m.WParam.ToInt64(), m.LParam.ToInt64(), "winforms-filter");
                        return false;

                    default:
                        return false;
                }
            }
        }
    }
}
