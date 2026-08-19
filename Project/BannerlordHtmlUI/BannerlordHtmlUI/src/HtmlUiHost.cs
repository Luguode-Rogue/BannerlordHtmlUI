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

        public HtmlUiWindowState(bool foreground, bool visible, bool minimized, int left, int top, int width, int height)
        {
            IsForeground = foreground;
            IsVisible = visible;
            IsMinimized = minimized;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
    }

    internal sealed class HtmlUiHost : IDisposable
    {
        internal readonly HtmlUiPageManager Pages;
        internal readonly HtmlUiStateStore State;
        internal readonly HtmlUiBridge Bridge;
        internal readonly HtmlUiGameThread GameThread;

        private HtmlUiOverlayForm _form;
        private WebView2 _web;
        private CoreWebView2Environment _environment;
        private Thread _uiThread;
        private TaskCompletionSource<bool> _ready;
        private System.Windows.Forms.Timer _followTimer;
        private HtmlUiInputMode _inputMode = HtmlUiInputMode.Hidden;
        private bool _requestedVisible;
        private bool _disposed;
        private string _webRoot;
        private readonly Dictionary<string, string> _contentRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _contentHosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private HtmlUiWindowState _lastWindowState;

        public bool IsReady => _ready != null && _ready.Task.IsCompletedSuccessfully;
        public HtmlUiInputMode InputMode => _inputMode;
        public bool IsVisible => _requestedVisible;
        public CoreWebView2 Core => _web?.CoreWebView2;
        public bool DevToolsEnabled { get; set; }
        public event Action<HtmlUiWindowState> WindowStateChanged;

        public HtmlUiHost(string webRoot)
        {
            _webRoot = Path.GetFullPath(webRoot ?? throw new ArgumentNullException(nameof(webRoot)));
            Pages = new HtmlUiPageManager(this);
            State = new HtmlUiStateStore();
            GameThread = new HtmlUiGameThread();
            Bridge = new HtmlUiBridge(this, GameThread);
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
                _form = new HtmlUiOverlayForm { BackColor = Color.Black, Opacity = 1.0 };
                _web = new WebView2 { Dock = DockStyle.Fill };
                _form.Controls.Add(_web);
                _followTimer = new System.Windows.Forms.Timer { Interval = 100 };
                _followTimer.Tick += (s, e) => FollowBannerlordWindow();
                _followTimer.Start();
                _form.Load += OnFormLoad;
                HtmlUiLogger.Info("WebView2 UI form created. Starting WinForms message loop.");
                Application.Run(_form);
                HtmlUiLogger.Info("WebView2 UI message loop exited.");
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
                HtmlUiLogger.Info("WebView2 ready. Host is operational.");
                _ready.TrySetResult(true);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("WebView2 initialization failed.", ex);
                _ready.TrySetException(ex);
            }
        }

        private void FollowBannerlordWindow()
        {
            try
            {
                var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd)) return;
                Win32.GetWindowRect(hwnd, out var rect);
                var minimized = Win32.IsIconic(hwnd);
                var windowVisible = Win32.IsWindowVisible(hwnd);
                var foreground = Win32.GetForegroundWindow() == hwnd;
                var overlayForeground = _form != null && !_form.IsDisposed && _form.IsHandleCreated && Win32.GetForegroundWindow() == _form.Handle;
                var width = Math.Max(0, rect.Right - rect.Left);
                var height = Math.Max(0, rect.Bottom - rect.Top);
                var focusAccepted = foreground || (_inputMode == HtmlUiInputMode.Captured && overlayForeground);
                var active = !minimized && windowVisible && focusAccepted && _requestedVisible;
                if (active)
                {
                    _form.Bounds = new Rectangle(rect.Left, rect.Top, width, height);
                    if (!_form.Visible) _form.Show();
                }
                else if (_form.Visible) _form.Hide();
                ApplyWindowState(new HtmlUiWindowState(foreground, active, minimized, rect.Left, rect.Top, width, height));
            }
            catch (Exception ex) { HtmlUiLogger.Error("Window tracking failed.", ex); }
        }

        private void ApplyWindowState(HtmlUiWindowState state)
        {
            if (_lastWindowState.IsForeground == state.IsForeground && _lastWindowState.IsVisible == state.IsVisible && _lastWindowState.IsMinimized == state.IsMinimized && _lastWindowState.Left == state.Left && _lastWindowState.Top == state.Top && _lastWindowState.Width == state.Width && _lastWindowState.Height == state.Height) return;
            _lastWindowState = state;
            HtmlUiLogger.Info("Window state changed: foreground=" + state.IsForeground + ", visible=" + state.IsVisible + ", minimized=" + state.IsMinimized + ", bounds=" + state.Left + "," + state.Top + " " + state.Width + "x" + state.Height + ", requestedVisible=" + _requestedVisible + ", inputMode=" + _inputMode);
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
            if (!_form.InvokeRequired) { action(); return; }
            Exception error = null;
            using (var gate = new ManualResetEventSlim(false))
            {
                _form.BeginInvoke(new Action(() => { try { action(); } catch (Exception ex) { error = ex; } finally { gate.Set(); } }));
                gate.Wait();
            }
            if (error != null) throw error;
        }

        private void MapContentRoot(string id, string directory)
        {
            var host = id.Equals("framework", StringComparison.OrdinalIgnoreCase) ? "bannerlord-htmlui.local" : "bannerlord-htmlui-" + SanitizeHostPart(id) + ".local";
            _contentRoots[id] = directory;
            _contentHosts[id] = host;
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(host, directory, CoreWebView2HostResourceAccessKind.Allow);
        }

        private static string SanitizeHostPart(string value)
        {
            var chars = value.ToLowerInvariant().ToCharArray();
            for (var i = 0; i < chars.Length; i++) if (!((chars[i] >= 'a' && chars[i] <= 'z') || (chars[i] >= '0' && chars[i] <= '9') || chars[i] == '-')) chars[i] = '-';
            var result = new string(chars).Trim('-');
            if (result.Length == 0) result = "mod";
            return result;
        }

        private string GetContentHost(HtmlUiPage page)
        {
            if (!_contentHosts.TryGetValue(page.ContentRootId, out var host)) throw new InvalidOperationException("Content root is not registered: " + page.ContentRootId);
            return host;
        }
        private string GetContentRoot(HtmlUiPage page)
        {
            if (!_contentRoots.TryGetValue(page.ContentRootId, out var root)) throw new InvalidOperationException("Content root is not registered: " + page.ContentRootId);
            return root;
        }
        private void InstallFrameworkRuntime()
        {
            var runtimePath = Path.Combine(_webRoot, "runtime.js");
            if (!File.Exists(runtimePath)) { HtmlUiLogger.Warn("runtime.js not found in framework web root."); return; }
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
        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e) { }
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
                relative = e.Uri.Substring(prefix.Length);
                break;
            }
            if (!allowedPrefix) { e.Cancel = true; HtmlUiLogger.Warn("Blocked navigation outside BannerlordHtmlUI content roots: " + e.Uri); return; }
            if (relative.IndexOf("../", StringComparison.Ordinal) >= 0 || relative.StartsWith("../", StringComparison.Ordinal)) { e.Cancel = true; HtmlUiLogger.Warn("Blocked unsafe relative navigation: " + relative); }
        }

        public void Reload() { if (_disposed) return; EnsureUiThread(() => _web.Reload()); }
        public void OpenDevTools() { if (!DevToolsEnabled) return; EnsureUiThread(() => _web.CoreWebView2?.OpenDevToolsWindow()); }
        public void Show() => SetInputMode(HtmlUiInputMode.Passive);
        public void Hide() => SetInputMode(HtmlUiInputMode.Hidden);
        public void CaptureInput() => SetInputMode(HtmlUiInputMode.Captured);
        public void CaptureMouse() => SetInputMode(HtmlUiInputMode.MouseCaptured);
        public void ReleaseInput() => SetInputMode(HtmlUiInputMode.Passive);

        public void SetInputMode(HtmlUiInputMode mode)
        {
            _inputMode = mode;
            _requestedVisible = mode != HtmlUiInputMode.Hidden;
            State?.Set("framework.inputMode", mode.ToString());
            var gameWindow = Process.GetCurrentProcess().MainWindowHandle;
            EnsureUiThread(() =>
            {
                if (_form == null || _form.IsDisposed) return;
                _form.SetPassThrough(mode == HtmlUiInputMode.Passive);
                if (mode == HtmlUiInputMode.Hidden)
                {
                    _form.Hide();
                    Win32.SetNoActivate(_form.Handle, false);
                    if (gameWindow != IntPtr.Zero && Win32.IsWindow(gameWindow)) Win32.SetForegroundWindow(gameWindow);
                    return;
                }
                if (!_form.Visible) _form.Show();
                if (mode == HtmlUiInputMode.Captured)
                {
                    Win32.SetNoActivate(_form.Handle, false);
                    _form.Activate();
                    _web?.Focus();
                }
                else if (mode == HtmlUiInputMode.MouseCaptured)
                {
                    Win32.SetNoActivate(_form.Handle, true);
                    Win32.ShowWindow(_form.Handle, Win32.SW_SHOWNOACTIVATE);
                    Win32.BringWindowAboveOwnerWithoutActivate(_form.Handle);
                    if (gameWindow != IntPtr.Zero && Win32.IsWindow(gameWindow)) Win32.SetForegroundWindow(gameWindow);
                    HtmlUiLogger.Info("MouseCaptured applied: overlay hit-testing enabled without keyboard focus.");
                }
                else
                {
                    Win32.SetNoActivate(_form.Handle, true);
                    Win32.ShowWindow(_form.Handle, Win32.SW_SHOWNOACTIVATE);
                }
            });
        }

        private void ApplyInputModeOnUiThread()
        {
            if (_form == null || _form.IsDisposed) return;
            _form.SetPassThrough(_inputMode == HtmlUiInputMode.Passive);
            if (_inputMode == HtmlUiInputMode.Hidden) { _form.Hide(); Win32.SetNoActivate(_form.Handle, false); return; }
            if (!_form.Visible) _form.Show();
            if (_inputMode == HtmlUiInputMode.Captured) { Win32.SetNoActivate(_form.Handle, false); _form.Activate(); _web?.Focus(); }
            else if (_inputMode == HtmlUiInputMode.MouseCaptured) { Win32.SetNoActivate(_form.Handle, true); Win32.ShowWindow(_form.Handle, Win32.SW_SHOWNOACTIVATE); Win32.BringWindowAboveOwnerWithoutActivate(_form.Handle); }
            else { Win32.SetNoActivate(_form.Handle, true); Win32.ShowWindow(_form.Handle, Win32.SW_SHOWNOACTIVATE); }
        }

        internal void DispatchToGameThread(Action action) => GameThread.Post(action);
        public bool CommandExists(string name) => Bridge != null && Bridge.CommandExists(name);
        public bool UnregisterCommand(string name) => Bridge != null && Bridge.UnregisterCommand(name);
        public bool UnregisterRequest(string name) => Bridge != null && Bridge.UnregisterRequest(name);
        public void RegisterCommand(string name, Action<JToken> handler) { if (Bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); Bridge.RegisterCommand(name, handler); }
        internal void RegisterCommand(string name, Action<JToken> handler, string ownerId) { if (Bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); Bridge.RegisterCommand(name, handler, ownerId); }
        public void RegisterRequest(string name, Func<JToken, Task<object>> handler) { if (Bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); Bridge.RegisterRequest(name, handler); }
        public void RegisterRequest(string name, Func<JToken, CancellationToken, Task<object>> handler) { if (Bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); Bridge.RegisterRequest(name, handler); }
        internal void RegisterRequest(string name, Func<JToken, Task<object>> handler, string ownerId) { if (Bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); Bridge.RegisterRequest(name, handler, ownerId); }
        internal void RegisterRequest(string name, Func<JToken, CancellationToken, Task<object>> handler, string ownerId) { if (Bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); Bridge.RegisterRequest(name, handler, ownerId); }
        public void SendEvent(string name, object payload)
        {
            EnsureUiThread(async () => { if (_web.CoreWebView2 == null) return; var msg = JsonConvert.SerializeObject(new { version = 1, type = "event", name, payload }); await _web.CoreWebView2.ExecuteScriptAsync($"window.game&&window.game.__receive({JsonConvert.SerializeObject(msg)})"); });
        }
        internal Task SendResponseAsync(string id, object payload, string error)
        {
            if (_disposed) return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EnsureUiThread(async () =>
            {
                try
                {
                    if (_web?.CoreWebView2 == null) { completion.TrySetResult(false); return; }
                    var msg = JsonConvert.SerializeObject(new { version = 1, type = "response", id, ok = error == null, payload, error });
                    await _web.CoreWebView2.ExecuteScriptAsync($"window.game&&window.game.__receive({JsonConvert.SerializeObject(msg)})").ConfigureAwait(true);
                    completion.TrySetResult(true);
                }
                catch (Exception ex) { HtmlUiLogger.Error("Failed to send browser response.", ex); completion.TrySetException(ex); }
            });
            return completion.Task;
        }
        private void EnsureUiThread(Action action)
        {
            if (action == null || _form == null || _form.IsDisposed) return;
            void ExecuteSafe()
            {
                try { action(); }
                catch (Exception ex) { HtmlUiLogger.Error("UI thread callback failed.", ex); HtmlUiDiagnostics.RecordBrowserError("UI thread callback failed: " + ex.GetBaseException().Message); }
            }
            if (!_form.InvokeRequired) ExecuteSafe(); else _form.BeginInvoke((Action)ExecuteSafe);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_followTimer != null) { _followTimer.Stop(); _followTimer.Dispose(); _followTimer = null; }
                if (_form != null && !_form.IsDisposed)
                {
                    if (_form.InvokeRequired) _form.BeginInvoke(new Action(() => { try { Application.ExitThread(); } catch { } }));
                    else Application.ExitThread();
                }
            }
            catch { }
            try { GameThread.Dispose(); } catch { }
        }
    }
}