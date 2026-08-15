using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    public readonly struct HtmlUiWindowState
    {
        public readonly bool IsForeground;
        public readonly bool IsVisible;
        public readonly bool IsMinimized;
        public readonly int Left;
        public readonly int Top;
        public readonly int Width;
        public readonly int Height;

        public HtmlUiWindowState(bool isForeground, bool isVisible, bool isMinimized, int left, int top, int width, int height)
        {
            IsForeground = isForeground; IsVisible = isVisible; IsMinimized = isMinimized;
            Left = left; Top = top; Width = width; Height = height;
        }
    }
    public sealed class HtmlUiHost : IDisposable
    {
        private readonly string _webRoot;
        private readonly GameThreadDispatcher _gameThread;
        private Thread _uiThread;
        private TaskCompletionSource<bool> _ready;
        private HtmlUiOverlayForm _form;
        private WebView2 _web;
        private CoreWebView2Environment _environment;
        private HtmlUiBridge _bridge;
        private FileSystemWatcher _watcher;
        private string _currentRelativePath;
        private HtmlUiPage _pendingPage;
        private bool _navigationInProgress;
        private bool _disposed;
        private volatile bool _webViewReady;
        private HtmlUiInputMode _inputMode = HtmlUiInputMode.Hidden;
        private bool _requestedVisible;
        private System.Windows.Forms.Timer _followTimer;
        private readonly Dictionary<string, string> _contentRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _contentHosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public HtmlUiStateStore State { get; }
        public HtmlUiPageManager Pages { get; }
        public bool DevToolsEnabled { get; set; } = true;
        public bool HotReloadEnabled { get; set; } = false;
        public bool IsVisible => _requestedVisible;
        public bool IsWebViewReady => _webViewReady;
        public bool IsInputCaptured => _inputMode == HtmlUiInputMode.Captured;
        public HtmlUiInputMode InputMode => _inputMode;
        public string CurrentPagePath => _currentRelativePath;
        public int ContentRootCount => _contentRoots.Count;
        public bool NavigationInProgress => _navigationInProgress;
        public bool IsHostCreated => _form != null && !_form.IsDisposed;
        public HtmlUiWindowState GetWindowState() => _lastWindowState;
        public event Action Ready;
        public event Action<string> BrowserError;
        public event Action<HtmlUiWindowState> WindowStateChanged;
        private HtmlUiWindowState _lastWindowState;
        private bool _hasWindowDiagnostic;

        public HtmlUiHost(string webRoot, GameThreadDispatcher gameThread)
        {
            _webRoot = Path.GetFullPath(webRoot);
            _gameThread = gameThread ?? throw new ArgumentNullException(nameof(gameThread));
            Pages = new HtmlUiPageManager();
            Pages.Attach(this);
            State = new HtmlUiStateStore(this);
            _contentRoots["framework"] = _webRoot;
            _contentHosts["framework"] = "bannerlord-htmlui.local";
        }

        public Task InitializeAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HtmlUiHost));
            if (_ready != null) return _ready.Task;
            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _uiThread = new Thread(UiThreadMain) { IsBackground = true, Name = "BannerlordHtmlUI.WebView2" };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            return _ready.Task;
        }

        private void UiThreadMain()
        {
            try
            {
                HtmlUiLogger.Info("WebView2 UI thread starting.");

                _form = new HtmlUiOverlayForm
                {
                    BackColor = Color.Black,
                    Opacity = 1.0
                };

                _web = new WebView2
                {
                    Dock = DockStyle.Fill
                };

                _form.Controls.Add(_web);

                _followTimer = new System.Windows.Forms.Timer { Interval = 100 };
                _followTimer.Tick += (s, e) => FollowBannerlordWindow();
                _followTimer.Start();

                _form.Load += OnFormLoad;

                HtmlUiLogger.Info("WebView2 UI form created. Starting WinForms message loop.");
                Application.Run(_form);
                HtmlUiLogger.Info("WebView2 UI thread message loop exited.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("WebView2 UI thread failed.", ex);
                _ready.TrySetException(ex);
            }
        }

        private void OnFormLoad(object sender, EventArgs e)
        {
            _form.Load -= OnFormLoad;
            HtmlUiLogger.Info("WebView2 host form loaded. Scheduling asynchronous WebView2 initialization.");
            _form.BeginInvoke(new Action(async () => await InitializeWebView2Async()));
        }

        private async Task InitializeWebView2Async()
        {
            try
            {
                HtmlUiLogger.Info("WebView2 asynchronous initialization started.");
                var cache = Path.Combine(Path.GetTempPath(), "BannerlordHtmlUI", "WebView2");
                Directory.CreateDirectory(cache);
                HtmlUiLogger.Info("Creating WebView2 environment. Cache=" + cache);

                _environment = await CoreWebView2Environment.CreateAsync(null, cache);
                HtmlUiLogger.Info("WebView2 environment created.");

                await _web.EnsureCoreWebView2Async(_environment);
                HtmlUiLogger.Info("EnsureCoreWebView2Async completed.");

                ConfigureAfterWebViewReady();
            }
            catch (Exception ex)
            {
                HtmlUiDiagnostics.RecordBrowserError("WebView2 initialization failed: " + ex.Message);
                HtmlUiLogger.Error("WebView2 asynchronous initialization failed.", ex);
                BrowserError?.Invoke(ex.Message);
                _ready.TrySetException(ex);
            }
        }

        private void ConfigureAfterWebViewReady()
        {
            if (_web?.CoreWebView2 == null)
            {
                var ex = new InvalidOperationException("WebView2 reported initialization complete but CoreWebView2 is null.");
                HtmlUiLogger.Error("WebView2 initialization produced no CoreWebView2 instance.", ex);
                _ready.TrySetException(ex);
                return;
            }

            // Bannerlord HtmlUI is an in-game overlay, not a general-purpose browser.
            // Suppress Chromium's native page context menu so right-click does not expose
            // browser actions such as Back/Forward/Reload/Save/Inspect.
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = DevToolsEnabled;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _web.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
            _web.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _web.CoreWebView2.SourceChanged += (s, e2) => HtmlUiLogger.Info("WebView2 source changed: " + (_web.Source == null ? "<null>" : _web.Source.ToString()));
            _web.CoreWebView2.ContentLoading += (s, e2) => HtmlUiLogger.Info("WebView2 content loading: " + e2.NavigationId);
            _web.CoreWebView2.ProcessFailed += (s, args) =>
            {
                var message = "WebView2 process failed: " + args.ProcessFailedKind;
                HtmlUiDiagnostics.RecordBrowserError(message);
                BrowserError?.Invoke(message);
                HtmlUiLogger.Error(message);
            };

            _bridge = new HtmlUiBridge(this);
            _bridge.Attach(_web.CoreWebView2);
            ConfigureLocalHost();
            InstallFrameworkRuntime();
            InstallRuntimeErrorForwarder();
            InstallRuntimePatchesOnUiThread();

            _webViewReady = true;

            _ready.TrySetResult(true);
            HtmlUiLogger.Info("WebView2 ready. Host is operational.");
            Ready?.Invoke();
            FlushPendingPage();
        }

        private void InstallRuntimePatchesOnUiThread()
        {
            try { HtmlUiKeyboardAndDiagnosticsPatch.Install(this); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to install keyboard/diagnostics patch.", ex); }
            try { HtmlUiI18nBindingPatch.Install(this); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to install i18n binding lifecycle patch.", ex); }
            try { HtmlUiStateBootstrapPatch.Install(this); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to install state bootstrap patch.", ex); }
            try { HtmlUiBindingSchedulerPatch.Install(this); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to install binding scheduler patch.", ex); }
            try { HtmlUiErrorModelPatch.Install(this); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to install bridge error model patch.", ex); }
            try { HtmlUiRequestCancellationPatch.Install(this); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to install request cancellation patch.", ex); }
            try { HtmlUiNavigationRacePatch.Install(this); }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to install navigation race guard.", ex); }
        }

        private void FollowBannerlordWindow()
        {
            try
            {
                var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd) || !Win32.GetWindowRect(hwnd, out var rect))
                {
                    if (!_hasWindowDiagnostic)
                    {
                        HtmlUiLogger.Warn("Bannerlord main window could not be resolved. hwnd=" + hwnd);
                        _hasWindowDiagnostic = true;
                    }
                    ApplyWindowState(new HtmlUiWindowState(false, false, false, 0, 0, 0, 0));
                    if (_form != null && _form.Visible) _form.Hide();
                    return;
                }
                _hasWindowDiagnostic = false;

                var minimized = Win32.IsIconic(hwnd);
                var windowVisible = Win32.IsWindowVisible(hwnd);
                var foreground = Win32.GetForegroundWindow() == hwnd;
                var overlayForeground = _form != null && !_form.IsDisposed && _form.IsHandleCreated
                    && Win32.GetForegroundWindow() == _form.Handle;
                var width = Math.Max(0, rect.Right - rect.Left);
                var height = Math.Max(0, rect.Bottom - rect.Top);

                var focusAccepted = foreground ||
                                    (_inputMode == HtmlUiInputMode.Captured && overlayForeground);
                var active = !minimized && windowVisible && focusAccepted && _requestedVisible;

                if (active)
                {
                    _form.Bounds = new Rectangle(rect.Left, rect.Top, width, height);
                    if (!_form.Visible) _form.Show();
                }
                else if (_form.Visible)
                {
                    _form.Hide();
