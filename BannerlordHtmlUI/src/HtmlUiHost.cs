using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        private volatile HtmlUiPage _pendingPage;
        private bool _navigationInProgress;
        private bool _disposed;
        private volatile bool _webViewReady;
        // Cached CoreWebView2 reference. Reading WebView2.CoreWebView2 after the underlying
        // WebView has been disposed raises COM failures (RPC_E_DISCONNECTED / DisconnectedContext)
        // instead of returning null, so every script dispatch must go through this field.
        private CoreWebView2 _coreWebView2;
        private HtmlUiInputMode _inputMode = HtmlUiInputMode.Hidden;
        private bool _requestedVisible;
        private readonly Dictionary<string, string> _contentRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _contentHosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public HtmlUiStateStore State { get; }
        public HtmlUiPageManager Pages { get; }
        public bool DevToolsEnabled { get; set; } = true;
        public bool HotReloadEnabled { get; set; } = false;
        public bool IsVisible => _requestedVisible && _form != null && !_form.IsDisposed && _form.Visible;
        /// <summary>
        /// Whether the framework currently intends the overlay to be shown. Unlike <see cref="IsVisible"/>
        /// this does not depend on the form's actual visibility, so window tracking can decide whether
        /// to show the overlay without a circular dependency on the very state it is producing.
        /// </summary>
        internal bool IsOverlayRequested => _requestedVisible;
        public bool IsWebViewReady => _webViewReady;
        public bool IsInputCaptured => _inputMode == HtmlUiInputMode.Captured || _inputMode == HtmlUiInputMode.MouseCaptured;
        public HtmlUiInputMode InputMode => _inputMode;
        public string CurrentPagePath => _currentRelativePath;
        public int ContentRootCount => _contentRoots.Count;
        public bool NavigationInProgress => _navigationInProgress;
        public bool IsHostCreated => _form != null && !_form.IsDisposed;
        public HtmlUiWindowState GetWindowState() => HtmlUiWindowTracker.GetState(this);
        public event Action Ready;
        public event Action<string> BrowserError;
        public event Action<HtmlUiWindowState> WindowStateChanged;

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
                _form = new HtmlUiOverlayForm { BackColor = Color.Black, Opacity = 1.0 };
                _web = new WebView2 { Dock = DockStyle.Fill };
                _form.Controls.Add(_web);
                _form.Load += OnFormLoad;
                HtmlUiLogger.Info("WebView2 UI form created. Starting WinForms message loop.");
                Application.Run(_form);
                HtmlUiLogger.Info("WebView2 UI message loop exited.");
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("WebView2 UI thread failed.", ex);
                _ready?.TrySetException(ex);
            }
        }

        private void OnFormLoad(object sender, EventArgs e)
        {
            _form.Load -= OnFormLoad;
            _form.BeginInvoke(new Action(async () => await InitializeWebView2Async()));
        }

        private async Task InitializeWebView2Async()
        {
            try
            {
                var cache = Path.Combine(Path.GetTempPath(), "BannerlordHtmlUI", "WebView2");
                Directory.CreateDirectory(cache);
                HtmlUiLogger.Info("Creating WebView2 environment. Cache=" + cache);
                _environment = await CoreWebView2Environment.CreateAsync(null, cache);
                await _web.EnsureCoreWebView2Async(_environment);
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

        /// <summary>
        /// Single owner of the ready flag. Process recovery rebuilds CoreWebView2 in place, so the
        /// cached reference must be invalidated together with the flag; otherwise script dispatch
        /// can reach a torn-down COM object during the recovery window.
        /// </summary>
        internal void SetWebViewReady(bool ready)
        {
            if (!ready) Volatile.Write(ref _coreWebView2, null);
            _webViewReady = ready;
        }

        private void ConfigureAfterWebViewReady()
        {
            var core = _web?.CoreWebView2;
            if (core == null)
            {
                var ex = new InvalidOperationException("WebView2 reported initialization complete but CoreWebView2 is null.");
                HtmlUiLogger.Error("WebView2 initialization produced no CoreWebView2 instance.", ex);
                _ready.TrySetException(ex);
                return;
            }

            Volatile.Write(ref _coreWebView2, core);

            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = DevToolsEnabled;
            core.Settings.IsStatusBarEnabled = false;
            core.WebResourceRequested += OnWebResourceRequested;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.SourceChanged += (s, e2) => HtmlUiLogger.Info("WebView2 source changed: " + (_web.Source == null ? "<null>" : _web.Source.ToString()));
            core.ContentLoading += (s, e2) => HtmlUiLogger.Info("WebView2 content loading: " + e2.NavigationId);
            core.ProcessFailed += (s, args) =>
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

            SetWebViewReady(true);
            _ready.TrySetResult(true);
            HtmlUiLogger.Info("WebView2 ready. Host is operational.");
            Ready?.Invoke();
            FlushPendingPage();
        }

        private void InstallRuntimePatchesOnUiThread()
        {
            try { HtmlUiKeyboardAndDiagnosticsPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install keyboard/diagnostics patch.", ex); }
            try { HtmlUiBindingLifecyclePatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install binding lifecycle patch.", ex); }
            try { HtmlUiI18nBindingPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install i18n binding lifecycle patch.", ex); }
            try { HtmlUiStateBootstrapPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install state bootstrap patch.", ex); }
            try { HtmlUiBindingSchedulerPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install binding scheduler patch.", ex); }
            try { HtmlUiErrorModelPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install bridge error model patch.", ex); }
            try { HtmlUiRequestCancellationPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install request cancellation patch.", ex); }
            try { HtmlUiNavigationRacePatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install navigation race guard.", ex); }
        }

        // Overlay geometry and follow behaviour are owned exclusively by HtmlUiWindowTracker.
        // Host.SetInputMode only carries input semantics; keeping a second timer-driven follow
        // mechanism here is what previously let two components fight over the same overlay.

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
                if (!gate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Timed out waiting for the HTML UI thread.");
            }
            if (error != null) throw error;
        }

        private void MapContentRoot(string id, string directory)
        {
            var host = id.Equals("framework", StringComparison.OrdinalIgnoreCase) ? "bannerlord-htmlui.local" : "bannerlord-htmlui-" + SanitizeHostPart(id) + ".local";
            _contentRoots[id] = directory;
            _contentHosts[id] = host;
            var core = Volatile.Read(ref _coreWebView2);
            if (core == null) return;
            core.SetVirtualHostNameToFolderMapping(host, directory, CoreWebView2HostResourceAccessKind.Allow);
        }

        private static string SanitizeHostPart(string value)
        {
            var chars = value.ToLowerInvariant().ToCharArray();
            for (var i = 0; i < chars.Length; i++) if (!((chars[i] >= 'a' && chars[i] <= 'z') || (chars[i] >= '0' && chars[i] <= '9') || chars[i] == '-')) chars[i] = '-';
            var result = new string(chars).Trim('-');
            return result.Length == 0 ? "mod" : result;
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
            var core = Volatile.Read(ref _coreWebView2);
            if (core == null) return;
            core.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(runtimePath));
        }

        private void InstallRuntimeErrorForwarder()
        {
            var js = @"(() => { const send=(kind,error)=>{ try { chrome.webview.postMessage({version:1,type:'command',id:null,name:'runtime.error',payload:{kind,message:String(error)}}); } catch(_){} }; window.addEventListener('error',e=>send('error',e.error||e.message)); window.addEventListener('unhandledrejection',e=>send('unhandledrejection',e.reason)); })();";
            var core = Volatile.Read(ref _coreWebView2);
            if (core != null) DispatchScript(core, js, null);
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

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _navigationInProgress = false;
            if (e.IsSuccess)
            {
                try
                {
                    var page = Pages.Current;
                    if (page != null)
                    {
                        State.Set("framework.page.lifecycle", new { state = "ready", pageId = page.Id, ownerId = page.OwnerId, path = page.RelativePath });
                        SendEvent("framework.page.lifecycle", new { state = "ready", pageId = page.Id, ownerId = page.OwnerId, path = page.RelativePath });
                    }
                }
                catch (Exception ex) { HtmlUiLogger.Error("Failed to publish page ready lifecycle.", ex); }
                return;
            }
            var message = "WebView2 navigation failed. Status=" + e.WebErrorStatus;
            HtmlUiDiagnostics.RecordBrowserError(message);
            HtmlUiLogger.Error(message);
            BrowserError?.Invoke(message);
        }

        internal void ValidatePage(HtmlUiPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            GetContentRoot(page); GetContentHost(page);
            var full = Path.GetFullPath(Path.Combine(GetContentRoot(page), page.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            var root = GetContentRoot(page).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) throw new FileNotFoundException("HTML page was not found inside its content root.", full);
        }

        internal void Navigate(HtmlUiPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            if (_disposed) throw new ObjectDisposedException(nameof(HtmlUiHost));
            if (!IsWebViewReady) { _pendingPage = page; return; }
            NavigateOnUiThread(page);
        }

        internal void ClearPendingNavigation()
        {
            _pendingPage = null;
        }

        private void FlushPendingPage()
        {
            var page = _pendingPage;
            if (page == null || _disposed || !IsWebViewReady) return;
            _pendingPage = null;
            NavigateOnUiThread(page);
        }

        private void NavigateOnUiThread(HtmlUiPage page)
        {
            EnsureUiThread(() =>
            {
                try
                {
                    if (_web == null || Volatile.Read(ref _coreWebView2) == null) { _pendingPage = page; return; }
                    _currentRelativePath = page.ContentRootId + ":/" + page.RelativePath;
                    EnableWatcherIfNeeded(page);
                    var host = GetContentHost(page);
                    var encodedPath = Uri.EscapeUriString(page.RelativePath);
                    var separator = encodedPath.IndexOf("?", StringComparison.OrdinalIgnoreCase) >= 0 ? "&" : "?";
                    var owner = Uri.EscapeDataString(page.OwnerId ?? "framework");
                    var pageId = Uri.EscapeDataString(page.Id ?? string.Empty);
                    var uri = new Uri("https://" + host + "/" + encodedPath + separator + "__bannerlord_htmlui_owner=" + owner + "&__bannerlord_htmlui_page=" + pageId);
                    _navigationInProgress = true;
                    _web.Source = uri;
                }
                catch (Exception ex)
                {
                    _navigationInProgress = false;
                    HtmlUiDiagnostics.RecordBrowserError("Navigate failed: " + ex.Message);
                    HtmlUiLogger.Error("Navigate failed for page " + page.Id, ex);
                    throw;
                }
            });
        }

        private void EnableWatcherIfNeeded(HtmlUiPage page)
        {
            if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
            if (!HotReloadEnabled || !page.HotReload) return;
            var full = Path.Combine(GetContentRoot(page), page.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(full);
            if (dir == null || !Directory.Exists(dir)) return;
            _watcher = new FileSystemWatcher(dir) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size, EnableRaisingEvents = true };
            FileSystemEventHandler onChange = (s, e) => Reload();
            RenamedEventHandler onRename = (s, e) => Reload();
            _watcher.Changed += onChange; _watcher.Created += onChange; _watcher.Deleted += onChange; _watcher.Renamed += onRename;
        }

        public void Reload() { if (!_disposed) EnsureUiThread(() => _web.Reload()); }
        public void OpenDevTools() { if (DevToolsEnabled) EnsureUiThread(() => Volatile.Read(ref _coreWebView2)?.OpenDevToolsWindow()); }
        public void Show() => SetInputMode(HtmlUiInputMode.Passive);
        public void Hide() => SetInputMode(HtmlUiInputMode.Hidden);
        public void CaptureInput() => SetInputMode(HtmlUiInputMode.Captured);
        public void CaptureMouse() => SetInputMode(HtmlUiInputMode.MouseCaptured);
        public void ReleaseInput() => SetInputMode(HtmlUiInputMode.Passive);

        /// <summary>
        /// Normally intercepted by HtmlUiInputControllerPatch, which owns input semantics. This body
        /// remains as the fallback path when that patch is unavailable: it still applies the overlay
        /// state for the requested mode, while continuous window following belongs to
        /// HtmlUiWindowTracker and is intentionally not duplicated here.
        /// </summary>
        public void SetInputMode(HtmlUiInputMode mode)
        {
            if (_disposed) return;
            _inputMode = mode;
            _requestedVisible = mode != HtmlUiInputMode.Hidden;
            try { State?.Set("framework.inputMode", mode.ToString()); } catch { }
            EnsureUiThread(() => ApplyInputModeOnUiThread(mode));
        }

        private void ApplyInputModeOnUiThread(HtmlUiInputMode mode)
        {
            if (_form == null || _form.IsDisposed || !_form.IsHandleCreated) return;
            var gameWindow = Win32.TryGetGameWindowHandle(_form.Handle, out var hwnd) ? hwnd : IntPtr.Zero;
            if (mode == HtmlUiInputMode.Hidden)
            {
                _requestedVisible = false;
                try { Win32.ReleaseMouseCapture(); } catch { }
                try { if (_web != null) _web.Enabled = false; } catch { }
                try { _form.SetPassThrough(true); } catch { }
                try { _form.Hide(); } catch { }
                if (gameWindow != IntPtr.Zero) { try { Win32.SetForegroundWindow(gameWindow); } catch { } }
                HtmlUiLogger.Info("Input mode applied: Hidden");
                return;
            }
            _requestedVisible = true;
            if (gameWindow != IntPtr.Zero)
            {
                try { _form.SetOwner(gameWindow); } catch { }
                if (Win32.GetWindowRect(gameWindow, out var rect)) _form.Bounds = new Rectangle(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
            }
            try { if (_web != null) _web.Enabled = true; } catch { }
            try { _form.Show(); } catch { }
            if (mode == HtmlUiInputMode.Passive)
            {
                _form.SetPassThrough(true);
                Win32.ShowWindow(_form.Handle, Win32.SW_SHOWNOACTIVATE);
                Win32.BringWindowAboveOwnerWithoutActivate(_form.Handle);
            }
            else
            {
                _form.SetPassThrough(false);
                Win32.ShowWindow(_form.Handle, Win32.SW_SHOWNOACTIVATE);
                Win32.BringWindowAboveOwnerWithoutActivate(_form.Handle);
                if (mode == HtmlUiInputMode.Captured)
                {
                    Win32.SetForegroundWindow(_form.Handle);
                    _form.Activate();
                    _web?.Focus();
                }
            }
            HtmlUiLogger.Info("Input mode applied: " + mode + ", overlayHwnd=" + _form.Handle + ", gameHwnd=" + gameWindow);
        }

        internal void DispatchToGameThread(Action action) { _gameThread.Post(action); }
        public bool CommandExists(string name) { return _bridge != null && _bridge.CommandExists(name); }
        public bool UnregisterCommand(string name) { return _bridge != null && _bridge.UnregisterCommand(name); }
        public bool UnregisterRequest(string name) { return _bridge != null && _bridge.UnregisterRequest(name); }
        public void RegisterCommand(string name, Action<JToken> handler) { if (_bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); _bridge.RegisterCommand(name, handler); }
        internal void RegisterCommand(string name, Action<JToken> handler, string ownerId) { if (_bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); _bridge.RegisterCommand(name, handler, ownerId); }
        public void RegisterRequest(string name, Func<JToken, Task<object>> handler) { if (_bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); _bridge.RegisterRequest(name, handler); }
        public void RegisterRequest(string name, Func<JToken, CancellationToken, Task<object>> handler) { if (_bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); _bridge.RegisterRequest(name, handler); }
        internal void RegisterRequest(string name, Func<JToken, Task<object>> handler, string ownerId) { if (_bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); _bridge.RegisterRequest(name, handler, ownerId); }
        internal void RegisterRequest(string name, Func<JToken, CancellationToken, Task<object>> handler, string ownerId) { if (_bridge == null) throw new InvalidOperationException("HTML UI host is not ready."); _bridge.RegisterRequest(name, handler, ownerId); }

        public void SendEvent(string name, object payload)
        {
            EnsureUiThread(() =>
            {
                var core = Volatile.Read(ref _coreWebView2);
                if (_disposed || core == null) return;
                var msg = JsonConvert.SerializeObject(new { version = 1, type = "event", name, payload });
                DispatchScript(core, "window.game&&window.game.__receive(" + JsonConvert.SerializeObject(msg) + ")", null);
            });
        }

        internal Task SendResponseAsync(string id, object payload, string error)
        {
            if (_disposed) return Task.CompletedTask;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // EnsureUiThread silently drops the callback once the overlay form is gone, which would
            // leave the caller awaiting a completion source that can never settle.
            var form = _form;
            if (form == null || form.IsDisposed) { completion.TrySetResult(false); return completion.Task; }

            EnsureUiThread(() =>
            {
                var core = Volatile.Read(ref _coreWebView2);
                if (_disposed || core == null) { completion.TrySetResult(false); return; }
                var msg = JsonConvert.SerializeObject(new { version = 1, type = "response", id, ok = error == null, payload, error });
                DispatchScript(core, "window.game&&window.game.__receive(" + JsonConvert.SerializeObject(msg) + ")", completion);
            });
            return completion.Task;
        }

        /// <summary>
        /// Executes a script against a cached CoreWebView2 reference and normalizes the WebView2
        /// shutdown races (RPC_E_DISCONNECTED / DisconnectedContext / ObjectDisposed) into a
        /// non-throwing result. Never rethrows onto the WinForms message loop.
        /// </summary>
        private static void DispatchScript(CoreWebView2 core, string script, TaskCompletionSource<bool> completion)
        {
            if (core == null) { completion?.TrySetResult(false); return; }
            try
            {
                var task = core.ExecuteScriptAsync(script);
                if (task == null) { completion?.TrySetResult(false); return; }
                task.ContinueWith(t =>
                {
                    if (t.IsCanceled) { completion?.TrySetResult(false); return; }
                    var error = t.Exception?.GetBaseException();
                    if (error != null)
                    {
                        if (IsWebViewShutdownFailure(error)) { completion?.TrySetResult(false); return; }
                        HtmlUiLogger.Error("Failed to dispatch script to the browser.", error);
                        if (completion != null) completion.TrySetException(error);
                        return;
                    }
                    completion?.TrySetResult(true);
                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                var error = ex.GetBaseException();
                if (!IsWebViewShutdownFailure(error)) HtmlUiLogger.Error("Failed to dispatch script to the browser.", error);
                completion?.TrySetResult(false);
            }
        }

        private static bool IsWebViewShutdownFailure(Exception error)
        {
            if (error is ObjectDisposedException || error is OperationCanceledException) return true;
            var com = error as System.Runtime.InteropServices.COMException;
            if (com != null)
            {
                switch ((uint)com.HResult)
                {
                    case 0x80010108u: // RPC_E_DISCONNECTED
                    case 0x8001010Du: // RPC_E_SERVER_DIED_DNE
                    case 0x8001010Eu: // RPC_E_WRONG_THREAD
                    case 0x800706BAu: // RPC_S_SERVER_UNAVAILABLE (DisconnectedContext)
                    case 0x800706BEu: // RPC_S_CALL_FAILED
                        return true;
                }
                return false;
            }
            // WebView2 surfaces a torn-down CoreWebView2 as InvalidOperationException.
            return error is InvalidOperationException && error.Message.IndexOf("disposed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void EnsureUiThread(Action action)
        {
            if (action == null || _form == null || _form.IsDisposed) return;
            void ExecuteSafe()
            {
                try { action(); }
                catch (Exception ex) { HtmlUiLogger.Error("UI thread callback failed.", ex); HtmlUiDiagnostics.RecordBrowserError("UI thread callback failed: " + ex.GetBaseException().Message); }
            }
            try
            {
                if (_form.InvokeRequired) _form.BeginInvoke((Action)ExecuteSafe); else ExecuteSafe();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Drop the cached CoreWebView2 first: any in-flight script dispatch started before
            // disposal must observe a dead host rather than touch a torn-down COM object.
            _webViewReady = false;
            Volatile.Write(ref _coreWebView2, null);
            try { HtmlUiKeyboardAndDiagnosticsPatch.Uninstall(this); } catch { }
            try { HtmlUiWindowTracker.Uninstall(this); } catch { }
            try { _watcher?.Dispose(); } catch { }
            _watcher = null;
            try { _bridge?.Dispose(); } catch { }
            _bridge = null;
            if (_form != null && !_form.IsDisposed)
            {
                try { if (_form.InvokeRequired) _form.BeginInvoke(new Action(() => { try { _form.Close(); } catch { } })); else _form.Close(); } catch { }
            }
            _webViewReady = false;
        }
    }
}
