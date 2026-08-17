using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiProcessRecovery
    {
        private static readonly object Sync = new object();
        private static bool _installed;
        private static HtmlUiHost _host;
        private static int _recoveryInProgress;

        private static FieldInfo _webField;
        private static FieldInfo _environmentField;
        private static FieldInfo _formField;
        private static FieldInfo _pendingPageField;
        private static FieldInfo _readyEventField;
        private static MethodInfo _configureMethod;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;

            lock (Sync)
            {
                if (_installed)
                {
                    if (ReferenceEquals(_host, host))
                        ScheduleAttachToCurrentWebView();
                    return;
                }

                _host = host;
                _webField = typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
                _environmentField = typeof(HtmlUiHost).GetField("_environment", BindingFlags.Instance | BindingFlags.NonPublic);
                _formField = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                _pendingPageField = typeof(HtmlUiHost).GetField("_pendingPage", BindingFlags.Instance | BindingFlags.NonPublic);
                _readyEventField = typeof(HtmlUiHost).GetField("Ready", BindingFlags.Instance | BindingFlags.NonPublic);
                _configureMethod = AccessToolsCompat.Method(typeof(HtmlUiHost), "ConfigureAfterWebViewReady");

                if (_webField == null || _environmentField == null || _formField == null ||
                    _pendingPageField == null || _configureMethod == null)
                {
                    throw new MissingMemberException("HtmlUiHost WebView2 recovery members are incomplete.");
                }

                _recoveryInProgress = 0;
                _installed = true;
                ScheduleAttachToCurrentWebView();
                HtmlUiLogger.Info("WebView2 process recovery installed.");
            }
        }

        public static void Uninstall()
        {
            lock (Sync)
            {
                _installed = false;
                _host = null;
                _webField = null;
                _environmentField = null;
                _formField = null;
                _pendingPageField = null;
                _readyEventField = null;
                _configureMethod = null;
                _recoveryInProgress = 0;
            }
        }

        private static void ScheduleAttachToCurrentWebView()
        {
            try
            {
                var host = _host;
                var form = GetForm(host);
                if (form == null || form.IsDisposed || !form.IsHandleCreated)
                {
                    HtmlUiLogger.Debug("WebView2 recovery attach deferred: host form is not ready.");
                    return;
                }

                if (!form.InvokeRequired)
                {
                    AttachToCurrentWebViewOnUiThread();
                    return;
                }

                form.BeginInvoke(new Action(AttachToCurrentWebViewOnUiThread));
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Failed to schedule WebView2 recovery handler: " + ex.GetBaseException().Message);
            }
        }

        private static void AttachToCurrentWebViewOnUiThread()
        {
            try
            {
                var host = _host;
                if (host == null || !_installed) return;

                var web = GetWebView(host);
                if (web == null || web.IsDisposed)
                {
                    HtmlUiLogger.Debug("WebView2 recovery attach skipped: WebView2 instance unavailable.");
                    return;
                }

                var core = web.CoreWebView2;
                if (core == null)
                {
                    HtmlUiLogger.Debug("WebView2 recovery attach deferred: CoreWebView2 is not ready yet.");
                    return;
                }

                core.ProcessFailed -= OnProcessFailed;
                core.ProcessFailed += OnProcessFailed;
                HtmlUiLogger.Info("WebView2 process recovery handler attached to current WebView2 instance.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Failed to attach WebView2 recovery handler: " + ex.GetBaseException().Message);
            }
        }

        private static void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs args)
        {
            HtmlUiHost host;
            lock (Sync)
            {
                if (!_installed || _host == null)
                    return;

                if (_recoveryInProgress != 0)
                    return;

                _recoveryInProgress = 1;
                host = _host;
            }

            var kind = args == null ? "Unknown" : args.ProcessFailedKind.ToString();
            HtmlUiLogger.Warn("WebView2 process recovery requested. kind=" + kind + ", currentPage=" + (host.Pages.CurrentId ?? "<null>"));

            try
            {
                var form = GetForm(host);
                if (form == null || form.IsDisposed || !form.IsHandleCreated)
                    throw new InvalidOperationException("WebView2 recovery host form is unavailable.");

                form.BeginInvoke(new Action(async () => await RecoverAsync(host, kind)));
            }
            catch (Exception ex)
            {
                lock (Sync) _recoveryInProgress = 0;
                HtmlUiLogger.Error("Failed to schedule WebView2 recovery.", ex);
            }
        }

        private static async Task RecoverAsync(HtmlUiHost host, string kind)
        {
            try
            {
                if (host == null || host.IsHostCreated == false || host.IsDisposedForRecovery())
                    throw new ObjectDisposedException(nameof(HtmlUiHost));

                var currentPage = host.Pages.Current;
                var requestedInputMode = host.InputMode;
                var oldWeb = GetWebView(host);
                var oldCore = oldWeb?.CoreWebView2;

                SetWebViewReady(host, false);

                if (currentPage != null)
                    _pendingPageField.SetValue(host, currentPage);
                else
                    _pendingPageField.SetValue(host, null);

                if (oldCore != null)
                {
                    try { oldCore.ProcessFailed -= OnProcessFailed; } catch { }
                }

                var form = GetForm(host);
                if (form == null || form.IsDisposed)
                    throw new InvalidOperationException("WebView2 recovery form is unavailable.");

                if (oldWeb != null)
                {
                    try { form.Controls.Remove(oldWeb); } catch { }
                    try { oldWeb.Dispose(); } catch (Exception ex) { HtmlUiLogger.Debug("Old WebView2 dispose failed during recovery: " + ex.GetBaseException().Message); }
                }

                var replacement = new WebView2 { Dock = System.Windows.Forms.DockStyle.Fill };
                form.Controls.Add(replacement);
                _webField.SetValue(host, replacement);

                var environment = GetEnvironment(host);
                if (environment == null)
                {
                    environment = await CreateEnvironmentAsync();
                    _environmentField.SetValue(host, environment);
                }

                try
                {
                    await replacement.EnsureCoreWebView2Async(environment);
                }
                catch (Exception firstEx)
                {
                    HtmlUiLogger.Warn("WebView2 recovery reuse of existing environment failed; recreating environment. reason=" + firstEx.GetBaseException().Message);
                    try { replacement.Dispose(); } catch { }
                    try { form.Controls.Remove(replacement); } catch { }

                    environment = await CreateEnvironmentAsync();
                    _environmentField.SetValue(host, environment);

                    replacement = new WebView2 { Dock = System.Windows.Forms.DockStyle.Fill };
                    form.Controls.Add(replacement);
                    _webField.SetValue(host, replacement);
                    await replacement.EnsureCoreWebView2Async(environment);
                }

                var readyField = _readyEventField;
                var readyHandlers = readyField == null ? null : readyField.GetValue(host);
                if (readyField != null)
                    readyField.SetValue(host, null);

                try
                {
                    _configureMethod.Invoke(host, null);
                }
                finally
                {
                    if (readyField != null)
                        readyField.SetValue(host, readyHandlers);
                }

                AttachToCurrentWebViewOnUiThread();

                if (requestedInputMode != HtmlUiInputMode.Hidden)
                    host.SetInputMode(requestedInputMode);

                SetWebViewReady(host, true);
                HtmlUiLogger.Info("WebView2 process recovery completed. kind=" + kind + ", page=" + (currentPage == null ? "<none>" : currentPage.Id));
            }
            catch (Exception ex)
            {
                SetWebViewReady(host, false);
                HtmlUiDiagnostics.RecordBrowserError("WebView2 recovery failed: " + ex.GetBaseException().Message);
                HtmlUiLogger.Error("WebView2 process recovery failed; entering safe close.", ex);

                try
                {
                    host.Pages.CloseCurrent();
                }
                catch (Exception closeEx)
                {
                    HtmlUiLogger.Error("Failed to safely close page after WebView2 recovery failure.", closeEx);
                }
            }
            finally
            {
                lock (Sync) _recoveryInProgress = 0;
            }
        }

        private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
        {
            var cache = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "BannerlordHtmlUI",
                "WebView2");
            System.IO.Directory.CreateDirectory(cache);
            return await CoreWebView2Environment.CreateAsync(null, cache);
        }

        private static WebView2 GetWebView(HtmlUiHost host)
        {
            return _webField == null ? null : _webField.GetValue(host) as WebView2;
        }

        private static CoreWebView2Environment GetEnvironment(HtmlUiHost host)
        {
            return _environmentField == null ? null : _environmentField.GetValue(host) as CoreWebView2Environment;
        }

        private static HtmlUiOverlayForm GetForm(HtmlUiHost host)
        {
            return _formField == null ? null : _formField.GetValue(host) as HtmlUiOverlayForm;
        }

        private static void SetWebViewReady(HtmlUiHost host, bool ready)
        {
            var field = typeof(HtmlUiHost).GetField("_webViewReady", BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(host, ready);
        }

        private static class AccessToolsCompat
        {
            public static MethodInfo Method(Type type, string name)
            {
                return type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            }
        }
    }

    internal static class HtmlUiHostRecoveryExtensions
    {
        private static readonly FieldInfo DisposedField = typeof(HtmlUiHost).GetField(
            "_disposed",
            BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool IsDisposedForRecovery(this HtmlUiHost host)
        {
            return DisposedField != null && (bool)DisposedField.GetValue(host);
        }
    }
}
