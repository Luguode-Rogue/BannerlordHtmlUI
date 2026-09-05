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
        internal const string FrameworkHostName = "bannerlord-htmlui.local";

        /// <summary>
        /// Shell-relative prefix for surface resources. Keeps every consumer's content reachable
        /// from the shell origin so surface iframes stay same-origin.
        /// </summary>
        internal const string SurfaceUrlPrefix = "https://" + FrameworkHostName + "/__surface/";

        /// <summary>The document that hosts surfaces. Registered as a virtual "framework:/" path.</summary>
        internal const string ShellFileName = "shell.html";
        internal const string ShellRelativePath = "framework:/" + ShellFileName;

        private readonly string _webRoot;
        private readonly GameThreadDispatcher _gameThread;
        private readonly HtmlUiInputCoordinator _inputCoordinator;
        private Thread _uiThread;
        private TaskCompletionSource<bool> _ready;
        private HtmlUiOverlayForm _form;
        private WebView2 _web;
        private CoreWebView2Environment _environment;
        private HtmlUiBridge _bridge;
        private FileSystemWatcher _watcher;
        private string _currentRelativePath;
        private volatile HtmlUiPage _pendingPage;
        private volatile bool _pendingShell;
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

        /// <summary>Parallel overlay surfaces that live alongside pages in the same WebView2.</summary>
        public HtmlUiSurfaceManager Surfaces { get; }

        public bool DevToolsEnabled { get; set; } = true;
        public bool HotReloadEnabled { get; set; } = false;
        public bool IsVisible => _requestedVisible && _form != null && !_form.IsDisposed && _form.Visible;
        public bool IsWebViewReady => _webViewReady;
        public bool IsInputCaptured => _inputMode == HtmlUiInputMode.Captured || _inputMode == HtmlUiInputMode.MouseCaptured;
        public HtmlUiInputMode InputMode => _inputMode;
        public string CurrentPagePath => _currentRelativePath;
        public int ContentRootCount => _contentRoots.Count;
        public bool NavigationInProgress => _navigationInProgress;
        public bool IsHostCreated => _form != null && !_form.IsDisposed;
        public bool IsDisposed => _disposed;
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
            Surfaces = new HtmlUiSurfaceManager();
            Surfaces.Attach(this);
            State = new HtmlUiStateStore(this);
            _inputCoordinator = new HtmlUiInputCoordinator(this);
            Surfaces.SurfacesChanged += () => _inputCoordinator.OnSurfacesChanged();
            _contentRoots["framework"] = _webRoot;
            _contentHosts["framework"] = FrameworkHostName;
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
                _followTimer.Tick += (s, e) =>
                {
                    if (_inputMode == HtmlUiInputMode.Hidden || !_requestedVisible)
                    {
                        StopFollowTimer();
                        return;
                    }
                    FollowBannerlordWindow();
                };
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

        private void StartFollowTimer()
        {
            if (_followTimer == null || _followTimer.Enabled) return;
            _followTimer.Start();
        }

        private void StopFollowTimer()
        {
            if (_followTimer == null || !_followTimer.Enabled) return;
            _followTimer.Stop();
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
            try
            {
                _web.CoreWebView2.AddWebResourceRequestedFilter(SurfaceUrlPrefix + "*", CoreWebView2WebResourceContext.All);
                HtmlUiLogger.Info("Surface resource filter registered: " + SurfaceUrlPrefix + "*");
            }
            catch (Exception ex) { HtmlUiLogger.Error("Failed to register surface resource filter.", ex); }
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
            try { HtmlUiKeyboardAndDiagnosticsPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install keyboard/diagnostics patch.", ex); }
            try { HtmlUiBindingLifecyclePatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install binding lifecycle patch.", ex); }
            try { HtmlUiI18nBindingPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install i18n binding lifecycle patch.", ex); }
            try { HtmlUiStateBootstrapPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install state bootstrap patch.", ex); }
            try { HtmlUiBindingSchedulerPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install binding scheduler patch.", ex); }
            try { HtmlUiErrorModelPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install bridge error model patch.", ex); }
            try { HtmlUiRequestCancellationPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install request cancellation patch.", ex); }
            // Document-script patches must ALL live here: recovery re-runs this method on a
            // rebuilt CoreWebView2, and anything registered only at SubModule-ready time is
            // silently lost for every document created after a WebView2 process recovery.
            try { HtmlUiStateRemovalPatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install state removal compatibility patch.", ex); }
            try { HtmlUiNavigationRacePatch.Install(this); } catch (Exception ex) { HtmlUiLogger.Error("Failed to install navigation race guard.", ex); }
        }

        private void FollowBannerlordWindow()
        {
            try
            {
                var hwnd = Win32.TryGetGameWindowHandle(_form != null && _form.IsHandleCreated ? _form.Handle : IntPtr.Zero, out var resolved) ? resolved : IntPtr.Zero;
                if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var rect))
                {
                    return;
                }
                var minimized = Win32.IsIconic(hwnd);
                var windowVisible = Win32.IsWindowVisible(hwnd);
                if (_inputMode == HtmlUiInputMode.Hidden || !_requestedVisible)
                {
                    StopFollowTimer();
                    return;
                }
                if (minimized || !windowVisible)
                {
                    ReleaseNativeCaptureOnly();
                    _form?.Hide();
                    return;
                }

                _form.SetOwner(hwnd);
                _form.Bounds = new Rectangle(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
                if (!_form.Visible) _form.Show();
                if (_inputMode == HtmlUiInputMode.Passive)
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
                }
            }
            catch (Exception ex) { HtmlUiLogger.Debug("Legacy window tracking failed: " + ex.GetBaseException().Message); }
        }

        private void ReleaseNativeCaptureOnly()
        {
            try { Win32.ReleaseMouseCapture(); } catch { }
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
                if (!gate.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Timed out waiting for the HTML UI thread.");
            }
            if (error != null) throw error;
        }

        private void MapContentRoot(string id, string directory)
        {
            var host = id.Equals("framework", StringComparison.OrdinalIgnoreCase) ? FrameworkHostName : "bannerlord-htmlui-" + SanitizeHostPart(id) + ".local";
            _contentRoots[id] = directory;
            _contentHosts[id] = host;
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(host, directory, CoreWebView2HostResourceAccessKind.Allow);
        }

        private static string SanitizeHostPart(string value)
        {
            var chars = value.ToLowerInvariant().ToCharArray();
            for (var i = 0; i < chars.Length; i++) if (!((chars[i] >= 'a' && chars[i] <= 'z') || (chars[i] >= '0' && chars[i] <= '9') || chars[i] == '-')) chars[i] = '-';
            var result = new string(chars).Trim('-');
            return result.Length == 0 ? "mod" : result;
        }

        private string GetContentHost(string contentRootId)
        {
            if (!_contentHosts.TryGetValue(contentRootId, out var host)) throw new InvalidOperationException("Content root is not registered: " + contentRootId);
            return host;
        }

        private string GetContentRoot(string contentRootId)
        {
            if (!_contentRoots.TryGetValue(contentRootId, out var root)) throw new InvalidOperationException("Content root is not registered: " + contentRootId);
            return root;
        }

        /// <summary>
        /// Resolves a resource path inside a content root and rejects traversal outside it.
        /// Returns null when the path escapes the root or the file does not exist.
        /// </summary>
        private string ResolveContentFile(string contentRootId, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(contentRootId) || string.IsNullOrWhiteSpace(relativePath)) return null;

            string root;
            if (!_contentRoots.TryGetValue(contentRootId, out root)) return null;

            var normalized = relativePath.Replace('\\', '/').TrimStart('/', '?');
            var queryIndex = normalized.IndexOf('?');
            if (queryIndex >= 0) normalized = normalized.Substring(0, queryIndex);
            var hashIndex = normalized.IndexOf('#');
            if (hashIndex >= 0) normalized = normalized.Substring(0, hashIndex);
            if (normalized.Length == 0) return null;

            if (normalized == ".." || normalized.StartsWith("../", StringComparison.Ordinal) || normalized.IndexOf("/../", StringComparison.Ordinal) >= 0) return null;

            var rootFull = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch { return null; }

            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(full) ? full : null;
        }

        private void InstallFrameworkRuntime()
        {
            var runtimePath = Path.Combine(_webRoot, "runtime.js");
            if (!File.Exists(runtimePath)) { HtmlUiLogger.Warn("runtime.js not found in framework web root."); return; }
            _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(runtimePath));
        }

        private void InstallRuntimeErrorForwarder()
        {
            var js = @"(() => { const send=(kind,error)=>{ try { chrome.webview.postMessage({version:1,type:'command',id:null,name:'runtime.error',payload:{kind,message:String(error)}}); } catch(_){} }; window.addEventListener('error',e=>send('error',e.error||e.message)); window.addEventListener('unhandledrejection',e=>send('unhandledrejection',e.reason)); })();";
            _web.ExecuteScriptAsync(js);
        }

        /// <summary>
        /// Serves surface resources from the shell's own origin. This is what keeps surface
        /// iframes same-origin with the shell, so no postMessage bridge is required.
        /// </summary>
        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            string uri = null;
            try { uri = e?.Request?.Uri; } catch { }
            if (string.IsNullOrEmpty(uri) || !uri.StartsWith(SurfaceUrlPrefix, StringComparison.OrdinalIgnoreCase)) return;

            var remainder = uri.Substring(SurfaceUrlPrefix.Length);
            var separator = remainder.IndexOf('/');
            if (separator <= 0) { FailResourceRequest(e, 404); return; }

            var contentRootId = Uri.UnescapeDataString(remainder.Substring(0, separator));
            var relativePath = remainder.Substring(separator + 1);
            var full = ResolveContentFile(contentRootId, relativePath);
            if (full == null)
            {
                HtmlUiLogger.Warn("Surface resource not found: " + uri);
                FailResourceRequest(e, 404);
                return;
            }

            try
            {
                var bytes = File.ReadAllBytes(full);
                var stream = new MemoryStream(bytes.Length);
                stream.Write(bytes, 0, bytes.Length);
                stream.Position = 0;
                var headers = "Content-Type: " + MimeTypeFor(full);
                e.Response = _web.CoreWebView2.Environment.CreateWebResourceResponse(stream, 200, "OK", headers);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Failed to serve surface resource: " + uri, ex);
                FailResourceRequest(e, 500);
            }
        }

        private void FailResourceRequest(CoreWebView2WebResourceRequestedEventArgs e, int status)
        {
            try
            {
                if (e == null || _web?.CoreWebView2 == null) return;
                var empty = new MemoryStream(0);
                e.Response = _web.CoreWebView2.Environment.CreateWebResourceResponse(empty, status, status == 404 ? "Not Found" : "Error", "Content-Type: text/plain");
            }
            catch { }
        }

        private static string MimeTypeFor(string path)
        {
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension)) return "application/octet-stream";
            switch (extension.ToLowerInvariant())
            {
                case ".html":
                case ".htm": return "text/html; charset=utf-8";
                case ".js": return "text/javascript; charset=utf-8";
                case ".mjs": return "text/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".ico": return "image/x-icon";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                case ".ttf": return "font/ttf";
                case ".map": return "application/json; charset=utf-8";
                default: return "application/octet-stream";
            }
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
            GetContentRoot(page.ContentRootId);
            GetContentHost(page.ContentRootId);
            var full = ResolveContentFile(page.ContentRootId, page.RelativePath);
            if (full == null) throw new FileNotFoundException("HTML page was not found inside its content root.", page.ContentRootId + ":/" + page.RelativePath);
        }

        /// <summary>Validates that a surface's entry file exists inside its registered content root.</summary>
        internal void ValidateSurface(HtmlUiSurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (!_contentRoots.ContainsKey(surface.ContentRootId))
                throw new InvalidOperationException("Content root is not registered: " + surface.ContentRootId +
                    ". Call scope.RegisterContentRoot before registering a surface.");

            var full = ResolveContentFile(surface.ContentRootId, surface.RelativePath);
            if (full == null) throw new FileNotFoundException("HTML surface entry was not found inside its content root.", surface.ContentRootId + ":/" + surface.RelativePath);
        }

        /// <summary>
        /// Builds the same-origin URL used to mount a surface inside the framework shell.
        /// The "__surface" prefix is served by WebResourceRequested so every consumer's content
        /// root is reachable from the shell's own origin, keeping surface iframes same-origin.
        /// </summary>
        internal string BuildSurfaceUri(HtmlUiSurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (!_contentRoots.ContainsKey(surface.ContentRootId))
                throw new InvalidOperationException("Content root is not registered: " + surface.ContentRootId);

            var encodedPath = Uri.EscapeUriString(surface.RelativePath);
            var separator = encodedPath.IndexOf('?') >= 0 ? "&" : "?";
            var owner = Uri.EscapeDataString(surface.OwnerId ?? "framework");
            var surfaceId = Uri.EscapeDataString(surface.Id ?? string.Empty);
            return SurfaceUrlPrefix + Uri.EscapeDataString(surface.ContentRootId) + "/" + encodedPath
                 + separator + "__bannerlord_htmlui_owner=" + owner
                 + "&__bannerlord_htmlui_surface=" + surfaceId;
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
            _pendingShell = false;
        }

        /// <summary>True while the framework shell is the loaded document.</summary>
        internal bool IsShellActive => string.Equals(_currentRelativePath, ShellRelativePath, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Loads the framework shell, which is the document that hosts surfaces.
        /// Pages and the shell are mutually exclusive: opening a page navigates away from it.
        /// </summary>
        internal void NavigateToShell()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HtmlUiHost));

            // A queued or open page owns the document. Navigating to the shell here would either
            // be replaced by that page anyway or, while the WebView is still starting, silently
            // drop the page's pending navigation and leave the page manager in a phantom-open state.
            if (_pendingPage != null || Pages.CurrentId != null) return;

            if (!IsWebViewReady) { _pendingShell = true; return; }
            NavigateShellOnUiThread();
        }

        private void NavigateShellOnUiThread()
        {
            EnsureUiThread(() =>
            {
                try
                {
                    if (_web?.CoreWebView2 == null) { _pendingShell = true; return; }
                    if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
                    _currentRelativePath = ShellRelativePath;
                    _navigationInProgress = true;
                    _web.Source = new Uri("https://" + FrameworkHostName + "/" + ShellFileName);
                }
                catch (Exception ex)
                {
                    _navigationInProgress = false;
                    HtmlUiDiagnostics.RecordBrowserError("Navigate to shell failed: " + ex.Message);
                    HtmlUiLogger.Error("Navigate to shell failed.", ex);
                    throw;
                }
            });
        }

        private void FlushPendingPage()
        {
            if (_disposed || !IsWebViewReady) return;

            if (_pendingShell)
            {
                _pendingShell = false;
                _pendingPage = null;
                NavigateShellOnUiThread();
                return;
            }

            var page = _pendingPage;
            if (page == null) return;
            _pendingPage = null;
            NavigateOnUiThread(page);
        }

        private void NavigateOnUiThread(HtmlUiPage page)
        {
            EnsureUiThread(() =>
            {
                try
                {
                    if (_web?.CoreWebView2 == null) { _pendingPage = page; return; }
                    _currentRelativePath = page.ContentRootId + ":/" + page.RelativePath;
                    EnableWatcherIfNeeded(page);
                    var host = GetContentHost(page.ContentRootId);
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
            var full = ResolveContentFile(page.ContentRootId, page.RelativePath);
            if (full == null) return;
            var dir = Path.GetDirectoryName(full);
            if (dir == null || !Directory.Exists(dir)) return;
            _watcher = new FileSystemWatcher(dir) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size, EnableRaisingEvents = true };
            FileSystemEventHandler onChange = (s, e) => Reload();
            RenamedEventHandler onRename = (s, e) => Reload();
            _watcher.Changed += onChange; _watcher.Created += onChange; _watcher.Deleted += onChange; _watcher.Renamed += onRename;
        }

        public void Reload() { if (!_disposed) EnsureUiThread(() => _web.Reload()); }
        public void OpenDevTools() { if (DevToolsEnabled) EnsureUiThread(() => _web.CoreWebView2?.OpenDevToolsWindow()); }
        public void Show() => SetInputMode(HtmlUiInputMode.Passive);
        public void Hide() => SetInputMode(HtmlUiInputMode.Hidden);
        public void CaptureInput() => SetInputMode(HtmlUiInputMode.Captured);
        public void CaptureMouse() => SetInputMode(HtmlUiInputMode.MouseCaptured);
        public void ReleaseInput() => SetInputMode(HtmlUiInputMode.Passive);

        /// <summary>Called by the page manager whenever a page becomes the active host owner.</summary>
        internal void NotifyPageOpened() => _inputCoordinator.OnPageOpened();

        /// <summary>Called by the page manager whenever the active page is released.</summary>
        internal void NotifyPageClosed() => _inputCoordinator.OnPageClosed();

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
                StopFollowTimer();
                ReleaseNativeCaptureOnly();
                try { if (_web != null) _web.Enabled = false; } catch { }
                try { _form.SetPassThrough(true); } catch { }
                try { _form.Hide(); } catch { }
                if (gameWindow != IntPtr.Zero) { try { Win32.SetForegroundWindow(gameWindow); } catch { } }
                HtmlUiLogger.Info("Input mode applied: Hidden");
                return;
            }
            _requestedVisible = true;
            StartFollowTimer();
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

        /// <summary>The overlay form, for framework-internal window probing (native mouse dispatcher).</summary>
        internal HtmlUiOverlayForm GetOverlayForm() => _form;

        /// <summary>
        /// Runs a script in the current page (fire-and-forget). Used by the native mouse
        /// dispatcher to deliver focus-independent clicks; safe to call from any thread.
        /// </summary>
        internal bool TryExecutePageScript(string script)
        {
            if (_disposed || !IsWebViewReady) return false;
            EnsureUiThread(() =>
            {
                try { _ = _web?.CoreWebView2?.ExecuteScriptAsync(script); }
                catch (Exception ex) { HtmlUiLogger.Debug("Page script dispatch failed: " + ex.GetBaseException().Message); }
            });
            return true;
        }

        /// <summary>Runs task continuations on the game thread through Drain.</summary>
        public System.Threading.Tasks.TaskScheduler GameThreadScheduler => _gameThread.Scheduler;
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
            EnsureUiThread(async () =>
            {
                if (_web?.CoreWebView2 == null) return;
                var msg = JsonConvert.SerializeObject(new { version = 1, type = "event", name, payload });
                await _web.CoreWebView2.ExecuteScriptAsync($"window.game&&window.game.__receive({JsonConvert.SerializeObject(msg)})");
            });
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
            try { HtmlUiKeyboardAndDiagnosticsPatch.Uninstall(this); } catch { }
            try { HtmlUiWindowTracker.Uninstall(this); } catch { }
            try { StopFollowTimer(); } catch { }
            try { if (_followTimer != null) { _followTimer.Tick -= (s, e) => FollowBannerlordWindow(); _followTimer.Dispose(); _followTimer = null; } } catch { }
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
