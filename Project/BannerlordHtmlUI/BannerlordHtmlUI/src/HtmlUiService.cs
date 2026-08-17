using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BannerlordHtmlUI
{
    public static class HtmlUiService
    {
        private static bool _initialized;
        private static HtmlUiHost _host;
        private static string _moduleDir;
        private static string _webRoot;
        private static readonly GameThreadDispatcher Dispatcher = new GameThreadDispatcher();
        private static int _testCounter;
        private static HtmlUiLifecycleState _lifecycleState = HtmlUiLifecycleState.Created;
        public static event Action Ready;
        public static event Action<string> LanguageChanged;

        public static HtmlUiHost Host => _host ?? throw new InvalidOperationException("BannerlordHtmlUI is not initialized.");
        public static HtmlUiPageManager Pages => Host.Pages;
        public static HtmlUiStateStore State => Host.State;
        public static bool IsInitialized => _initialized;
        public static HtmlUiLifecycleState LifecycleState => _lifecycleState;
        public static bool IsReady => _initialized && _lifecycleState == HtmlUiLifecycleState.Ready;

        public static void OnReady(Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (IsReady) callback();
            else Ready += callback;
        }

        public static async Task InitializeAsync(string moduleDirectory, string webRoot)
        {
            if (_initialized) return;
            _lifecycleState = HtmlUiLifecycleState.Initializing;
            _moduleDir = Path.GetFullPath(moduleDirectory ?? throw new ArgumentNullException(nameof(moduleDirectory)));
            _webRoot = Path.GetFullPath(webRoot ?? throw new ArgumentNullException(nameof(webRoot)));
            HtmlUiLogger.Initialize(_moduleDir);
            _host = new HtmlUiHost(_webRoot, Dispatcher);
            _host.HotReloadEnabled = true;
            try
            {
                await _host.InitializeAsync().ConfigureAwait(false);
                RegisterBuiltinHandlers();
                _initialized = true;
                _lifecycleState = HtmlUiLifecycleState.Ready;
                HtmlUiLocalization.InitializeState();
                State.Set("framework.lifecycle", _lifecycleState.ToString());
                State.Set("framework.i18n.locale", HtmlUiLocalization.CurrentLanguage);
                Ready?.Invoke();
            }
            catch
            {
                _lifecycleState = HtmlUiLifecycleState.Faulted;
                throw;
            }
        }

        private static void RegisterBuiltinHandlers()
        {
            HtmlUiBrushResourceService.Initialize(Host);
            HtmlUiNativeAtlasAssetService.Initialize(Host);

            Host.RegisterCommand("runtime.error", payload => { HtmlUiLogger.Warn("JavaScript runtime error: " + payload.ToString()); });
            Host.RegisterCommand("framework.openDevTools", _ => Host.OpenDevTools());
            Host.RegisterCommand("framework.reload", _ => Host.Reload());
            Host.RegisterCommand("framework.captureInput", _ => Host.CaptureInput());
            Host.RegisterCommand("framework.releaseInput", _ => Host.ReleaseInput());
            Host.RegisterCommand("framework.passiveInput", _ => Host.SetInputMode(HtmlUiInputMode.Passive));
            Host.RegisterCommand("framework.setInputMode", payload =>
            {
                var value = payload?["mode"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(value)) return;
                if (Enum.TryParse<HtmlUiInputMode>(value, true, out var parsed)) Host.SetInputMode(parsed);
            });
            Host.RegisterCommand("framework.ping", payload =>
            {
                var data = new { received = true, utc = DateTime.UtcNow, payload = payload.ToString() };
                SendEvent("framework:ping", data);
            });
            Host.RegisterRequest("framework.i18n.getLocale", _ => Task.FromResult<object>(new { language = HtmlUiLocalization.CurrentLanguage }));
            Host.RegisterRequest("framework.i18n.getLanguages", _ => Task.FromResult<object>(new { language = HtmlUiLocalization.CurrentLanguage, languages = HtmlUiLocalization.GetLanguages() }));
            Host.RegisterRequest("framework.i18n.translate", payload => Task.FromResult<object>(HtmlUiLocalization.Translate(payload?["key"]?.Value<string>(), payload?["variables"] as JObject, payload?["fallbackLanguage"]?.Value<string>())));
            Host.RegisterRequest("framework.i18n.translateMany", payload => Task.FromResult<object>(HtmlUiLocalization.TranslateMany(payload as JObject)));
            Host.RegisterRequest("framework.i18n.formatDate", payload => Task.FromResult<object>(new { text = HtmlUiLocalization.FormatDate(DateTime.Parse(payload?["value"]?.Value<string>() ?? DateTime.UtcNow.ToString("o"), null, System.Globalization.DateTimeStyles.RoundtripKind)) }));
            Host.RegisterRequest("framework.i18n.formatTime", payload => Task.FromResult<object>(new { text = HtmlUiLocalization.FormatTime(DateTime.Parse(payload?["value"]?.Value<string>() ?? DateTime.UtcNow.ToString("o"), null, System.Globalization.DateTimeStyles.RoundtripKind)) }));
            Host.RegisterCommand("framework.incrementTestState", _ => { var value = System.Threading.Interlocked.Increment(ref _testCounter); State.Set("framework.testCounter", value); });
            Host.RegisterCommand("framework.openPage", payload => { var ownerId = payload?["ownerId"]?.Value<string>(); var pageId = payload?["pageId"]?.Value<string>(); if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(pageId)) return; Pages.Open(MakeScopedName(ownerId, pageId)); });
            Host.RegisterCommand("framework.closePage", payload => { var ownerId = payload?["ownerId"]?.Value<string>(); var current = Pages.Current; if (string.IsNullOrWhiteSpace(ownerId) || current == null) return; if (string.Equals(current.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase)) Pages.CloseCurrent(); });
            Host.RegisterCommand("framework.closeCurrent", _ => Pages.CloseCurrent());

            Host.RegisterRequest("framework.brush.context", _ => Task.FromResult<object>(HtmlUiBrushService.GetContextSnapshot()));
            Host.RegisterRequest("framework.brush.list", payload => Task.FromResult<object>(HtmlUiBrushService.ListBrushes(payload)));
            Host.RegisterRequest("framework.brush.get", payload => Task.FromResult<object>(HtmlUiBrushService.GetBrush(payload)));
            Host.RegisterRequest("framework.brush.resource", payload => Task.FromResult<object>(HtmlUiBrushService.GetBrushResource(payload)));
            Host.RegisterRequest("framework.brush.resourceLegacy", payload => Task.FromResult<object>(HtmlUiBrushService.GetBrushResource(payload)));
            Host.RegisterRequest("framework.brush.nativeAssetProbe", (payload, cancellationToken) => HtmlUiNativeAtlasAssetService.ProbeAsync(payload, cancellationToken));
            Host.RegisterRequest("framework.brush.nativeAssetDiagnostics", (payload, cancellationToken) => HtmlUiNativeAssetDiagnosticsService.RunAsync(payload, cancellationToken));
            Host.RegisterRequest("framework.brush.state", payload => Task.FromResult<object>(HtmlUiBrushService.GetBrushState(payload)));
            Host.RegisterRequest("framework.brush.stateProbe", payload => Task.FromResult<object>(HtmlUiBrushService.GetBrushStateProbe(payload)));

            State.Set("framework.status", "ready");
            State.Set("framework.snapshot", new { version = HtmlUiDiagnostics.FrameworkVersion, protocol = 1 });
            State.Set("framework.lifecycle", _lifecycleState.ToString());
            State.Set("framework.inputMode", Host.InputMode.ToString());
            Host.WindowStateChanged += OnWindowStateChanged;
            Host.RegisterRequest("framework.getStateSnapshot", _ => Task.FromResult<object>(State.GetSnapshot()));
            Host.RegisterRequest("framework.getDiagnostics", _ => Task.FromResult<object>(HtmlUiDiagnostics.Snapshot()));
        }

        private static void OnWindowStateChanged(HtmlUiWindowState state)
        {
            if (!_initialized) return;
            State.Set("window.foreground", state.IsForeground);
            State.Set("window.visible", state.IsVisible);
            State.Set("window.minimized", state.IsMinimized);
            State.Set("window.bounds", new { state.Left, state.Top, state.Width, state.Height });
        }

        public static void NotifyGameContext(string context, bool active)
        {
            if (string.IsNullOrWhiteSpace(context)) throw new ArgumentException("Context is required.", nameof(context));
            State.Set("context." + context, active);
        }

        public static void Tick(int maxGameThreadWork = 256)
        {
            if (!_initialized) return;
            Dispatcher.Drain(maxGameThreadWork);
            if (HtmlUiLocalization.TryPublishLanguageChange(out var language))
            {
                State.Set("framework.i18n.locale", language);
                SendEvent("framework.i18n.localeChanged", new { language });
                try { LanguageChanged?.Invoke(language); } catch (Exception ex) { HtmlUiLogger.Error("Localization change callback failed.", ex); }
            }
        }

        public static void Show() => Host.Show();
        public static void Hide() => Host.Hide();
        public static void CaptureInput() => Host.CaptureInput();
        public static void ReleaseInput() => Host.ReleaseInput();
        public static void SetInputMode(HtmlUiInputMode mode) => Host.SetInputMode(mode);
        public static HtmlUiInputMode InputMode => Host.InputMode;
        public static string CurrentPagePath => Host.CurrentPagePath;
        public static void RegisterContentRoot(string id, string directory) => Host.RegisterContentRoot(id, directory);
        internal static void RegisterContentRoot(string id, string directory, string ownerId) => Host.RegisterContentRoot(id, directory);
        public static HtmlUiConsumerScope CreateScope(string ownerId) => new HtmlUiConsumerScope(ownerId);
        public static string MakeScopedName(string ownerId, string name) { if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("Owner id is required.", nameof(ownerId)); if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name)); return ownerId + "." + name.TrimStart('.'); }
        public static void OpenDevTools() => Host.OpenDevTools();
        public static void Reload() => Host.Reload();
        public static void RegisterCommand(string name, Action<JToken> handler) => Host.RegisterCommand(name, handler);
        internal static void RegisterCommand(string name, Action<JToken> handler, string ownerId) => Host.RegisterCommand(name, handler, ownerId);
        public static void RegisterRequest(string name, Func<JToken, Task<object>> handler) => Host.RegisterRequest(name, handler);
        public static void RegisterRequest(string name, Func<JToken, CancellationToken, Task<object>> handler) => Host.RegisterRequest(name, handler);
        internal static void RegisterRequest(string name, Func<JToken, Task<object>> handler, string ownerId) => Host.RegisterRequest(name, handler, ownerId);
        internal static void RegisterRequest(string name, Func<JToken, CancellationToken, Task<object>> handler, string ownerId) => Host.RegisterRequest(name, handler, ownerId);
        public static bool UnregisterCommand(string name) => Host.UnregisterCommand(name);
        internal static bool UnregisterCommand(string name, string ownerId) { var bridge = HtmlUiBridge.Current; return bridge != null && bridge.UnregisterCommand(name, ownerId); }
        public static bool UnregisterRequest(string name) => Host.UnregisterRequest(name);
        internal static bool UnregisterRequest(string name, string ownerId) { var bridge = HtmlUiBridge.Current; return bridge != null && bridge.UnregisterRequest(name, ownerId); }
        public static bool CancelRequest(string id) { var bridge = HtmlUiBridge.Current; return bridge != null && bridge.CancelRequest(id); }
        public static void SendEvent(string name, object payload) => Host.SendEvent(name, payload);

        public static void Dispose()
        {
            if (!_initialized && _host == null) return;
            _lifecycleState = HtmlUiLifecycleState.Unloading;
            try
            {
                HtmlUiBridgeShutdownPatch.CancelAll(HtmlUiBridge.Current);
                if (_host != null) _host.WindowStateChanged -= OnWindowStateChanged;
                _host?.Dispose();
            }
            finally
            {
                HtmlUiNativeAtlasAssetService.Dispose();
                HtmlUiBrushResourceService.Dispose();
                _host = null;
                _initialized = false;
                Ready = null;
                LanguageChanged = null;
                _lifecycleState = HtmlUiLifecycleState.Unloaded;
            }
        }
    }
}
