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
                    BackColor = Color.Magenta,
                    TransparencyKey = Color.Magenta,
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

                HtmlUiLogger.Info("WebView2 form created with transparent overlay surface. Starting WinForms message loop.");
                Application.Run(_form);
                HtmlUiLogger.Info("WebView2 host form message loop exited.");
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

                var controllerOptions = _environment.CreateCoreWebView2ControllerOptions();
                controllerOptions.DefaultBackgroundColor = Color.Transparent;
                await _web.EnsureCoreWebView2Async(_environment, controllerOptions);
                HtmlUiLogger.Info("EnsureCoreWebView2Async completed with transparent controller background.");

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

            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
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
                }

                ApplyWindowState(new HtmlUiWindowState(foreground, active, minimized, rect.Left, rect.Top, width, height));
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Window tracking failed.", ex);
            }
        }

        private void ApplyWindowState(HtmlUiWindowState state)
        {
            if (_lastWindowState.IsForeground == state.IsForeground &&
                _lastWindowState.IsVisible == state.IsVisible &&
                _lastWindowState.IsMinimized == state.IsMinimized &&
                _lastWindowState.Left == state.Left && _lastWindowState.Top == state.Top &&
                _lastWindowState.Width == state.Width && _lastWindowState.Height == state.Height) return;
            _lastWindowState = state;
            HtmlUiLogger.Info("Window state changed: foreground=" + state.IsForeground
                + ", visible=" + state.IsVisible
                + ", minimized=" + state.IsMinimized
                + ", bounds=" + state.Left + "," + state.Top + " " + state.Width + "x" + state.Height
                + ", requestedVisible=" + _requestedVisible
                + ", inputMode=" + _inputMode);
            WindowStateChanged?.Invoke(state);
        }

        private void ConfigureLocalHost() => MapContentRoot("framework", _webRoot);

        internal bool UnregisterContentRoot(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || string.Equals(id, "framework", StringComparison.OrdinalIgnoreCase)) return false;
            if (!_contentRoots.Remove(id)) return false;
            _contentHosts.Remove(id);
            HtmlUiLogger.Info("Content root unregistered: " + id);
            return true;
        }

        public void RegisterContentRoot(string id, string directory)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Content root id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Content root directory is required.", nameof(directory));
            var full = Path.GetFullPath(directory);
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);

            if (_contentRoots.TryGetValue(id, out var existing))
            {
                if (string.Equals(existing, full, StringComparison.OrdinalIgnoreCase)) return;
                throw new InvalidOperationException("Content root id is already registered: " + id);
            }

            RunOnUiThreadSync(() => MapContentRoot(id, full));
        }

        private void RunOnUiThreadSync(Action action)
        {
            if (_form == null || _form.IsDisposed) throw new InvalidOperationException("HTML UI host is not ready.");
            if (!_form.InvokeRequired)
            {
                action();
                return;
            }

            Exception error = null;
            using (var gate = new ManualResetEventSlim(false))
            {
                _form.BeginInvoke(new Action(() =>
                {
                    try { action(); }
                    catch (Exception ex) { error = ex; }
                    finally { gate.Set(); }
                }));
                gate.Wait();
            }
            if (error != null) throw error;
        }

        private void MapContentRoot(string id, string directory)
        {
            var host = id.Equals("framework", StringComparison.OrdinalIgnoreCase)
                ? "bannerlord-htmlui.local"
                : "bannerlord-htmlui-" + SanitizeHostPart(id) + ".local";
            _contentRoots[id] = directory;
            _contentHosts[id] = host;
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(host, directory, CoreWebView2HostResourceAccessKind.Allow);
        }

        private static string SanitizeHostPart(string value)
        {
            var chars = value.ToLowerInvariant().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (!((chars[i] >= 'a' && chars[i] <= 'z') || (chars[i] >= '0' && chars[i] <= '9') || chars[i] == '-'))
                    chars[i] = '-';
            }
            var result = new string(chars).Trim('-');
            if (result.Length == 0) result = "mod";
            return result;
        }

        private string GetContentHost(HtmlUiPage page)
        {
            if (!_contentHosts.TryGetValue(page.ContentRootId, out var host))
                throw new InvalidOperationException("Content root is not registered: " + page.ContentRootId);
            return host;
        }

        private string GetContentRoot(HtmlUiPage page)
        {
            if (!_contentRoots.TryGetValue(page.ContentRootId, out var root))
                throw new InvalidOperationException("Content root is not registered: " + page.ContentRootId);
            return root;
        }

        private void InstallFrameworkRuntime()
        {
            var runtimePath = Path.Combine(_webRoot, "runtime.js");
            if (!File.Exists(runtimePath))
            {
                HtmlUiLogger.Warn("runtime.js not found in framework web root.");
                return;
            }

            var script = File.ReadAllText(runtimePath);
            _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
        }

        private void InstallRuntimeErrorForwarder()
        {
            var js = @"
                (() => {
                    const send = (kind, error) => {
                        try { chrome.webview.postMessage({version:1,type:'command',id:null,name:'runtime.error',payload:{kind, message:String(error)}}); } catch (_) {}
                    };
                    window.addEventListener('error', e => send('error', e.error || e.message));
                    window.addEventListener('unhandledrejection', e => send('unhandledrejection', e.reason));
                })();";
            _web.ExecuteScriptAsync(js);
        }

        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Uri)) return;
            var allowedPrefix = false;
            var relative = string.Empty;
            foreach (var host in _contentHosts.Values)
            {
                var prefix = "https://" + host + "/";
                if (!e.Uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                allowedPrefix = true;
                relative = e.Uri.Substring(prefix.Length).Split('?')[0];
                break;
            }
            if (!allowedPrefix)
            {
                e.Cancel = true;
                HtmlUiLogger.Warn("Navigation blocked outside registered content roots: " + e.Uri);
            }
            else
            {
                _navigationInProgress = true;
                _currentRelativePath = relative;
                HtmlUiLogger.Info("Navigation starting: " + e.Uri);
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _navigationInProgress = false;
            if (e.IsSuccess)
            {
                HtmlUiLogger.Info("WebView2 navigation completed successfully.");
                return;
            }
            var message = "WebView2 navigation failed: " + e.WebErrorStatus;
            HtmlUiDiagnostics.RecordBrowserError(message);
            BrowserError?.Invoke(message);
            HtmlUiLogger.Error(message);
        }

        public void Show() => SetRequestedVisible(true);
        public void Hide() => SetRequestedVisible(false);
        public void CaptureInput() => SetInputMode(HtmlUiInputMode.Captured);
        public void ReleaseInput() => SetInputMode(HtmlUiInputMode.Hidden);
        public void SetInputMode(HtmlUiInputMode mode)
        {
            _inputMode = mode;
            if (_form == null || _form.IsDisposed) return;
            if (mode == HtmlUiInputMode.Hidden)
            {
                _form.SetPassThrough(true);
                SetRequestedVisible(false);
                return;
            }
            _form.SetPassThrough(mode == HtmlUiInputMode.Passive);
            if (mode != HtmlUiInputMode.Hidden) SetRequestedVisible(true);
        }

        private void SetRequestedVisible(bool visible)
        {
            _requestedVisible = visible;
            if (_form == null || _form.IsDisposed) return;
            _form.SetPassThrough(_inputMode == HtmlUiInputMode.Passive);
            FollowBannerlordWindow();
        }

        public void OpenPage(HtmlUiPage page)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HtmlUiHost));
            if (!_webViewReady)
            {
                _pendingPage = page;
                return;
            }

            RunOnUiThreadSync(() => NavigateToPage(page));
        }

        public void ClosePage()
        {
            if (!_webViewReady)
            {
                _pendingPage = null;
                return;
            }
            RunOnUiThreadSync(() =>
            {
                _navigationInProgress = false;
                _pendingPage = null;
                _currentRelativePath = string.Empty;
                _web.CoreWebView2.Navigate("about:blank");
                SetRequestedVisible(false);
            });
        }

        public void Reload()
        {
            if (!_webViewReady || _disposed) return;
            RunOnUiThreadSync(() => _web.Reload());
        }

        public void OpenDevTools() => _web?.CoreWebView2?.OpenDevToolsWindow();

        private void FlushPendingPage()
        {
            if (_pendingPage == null) return;
            var page = _pendingPage;
            _pendingPage = null;
            OpenPage(page);
        }

        private void NavigateToPage(HtmlUiPage page)
        {
            _pendingPage = null;
            _navigationInProgress = true;
            _currentRelativePath = page.Path;
            var host = GetContentHost(page);
            var ownerQuery = Uri.EscapeDataString(page.OwnerId ?? string.Empty);
            var pageQuery = Uri.EscapeDataString(page.Id ?? string.Empty);
            var url = "https://" + host + "/" + page.Path.TrimStart('/')
                + "?__bannerlord_htmlui_owner=" + ownerQuery
                + "&__bannerlord_htmlui_page=" + pageQuery;
            HtmlUiLogger.Info("Navigating WebView2 to " + url + ", formVisible=" + _requestedVisible + ", requestedVisible=" + _requestedVisible + ", inputMode=" + _inputMode);
            _web.CoreWebView2.Navigate(url);
            SetRequestedVisible(true);
        }

        internal CoreWebView2 WebView => _web?.CoreWebView2;

        internal void SetPagePath(string path) => _currentRelativePath = path ?? string.Empty;
        internal void SetNavigationInProgress(bool value) => _navigationInProgress = value;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _watcher?.Dispose();
                _followTimer?.Stop();
                if (_form != null && !_form.IsDisposed)
                {
                    if (_form.InvokeRequired) _form.BeginInvoke(new Action(() => _form.Close()));
                    else _form.Close();
                }
            }
            catch { }
            finally
            {
                _watcher = null;
                _followTimer = null;
                _webViewReady = false;
            }
        }
    }
}
