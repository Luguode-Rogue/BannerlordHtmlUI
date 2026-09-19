using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Owns only input semantics: Hidden/Passive/Captured/MouseCaptured, native capture,
    /// WebView enablement and the one-time foreground transition required by an explicit mode change.
    /// Window geometry/state is owned by HtmlUiWindowTracker.
    /// </summary>
    internal static class HtmlUiInputControllerPatch
    {
        private sealed class HostState
        {
            public long Generation;
            public HtmlUiInputMode LastAppliedMode = HtmlUiInputMode.Hidden;
            public bool Applying;
        }

        private static readonly object Sync = new object();
        private static readonly ConditionalWeakTable<HtmlUiHost, HostState> States = new ConditionalWeakTable<HtmlUiHost, HostState>();
        private static Harmony _harmony;
        private static bool _installed;
        private static FieldInfo _formField;
        private static FieldInfo _webField;
        private static FieldInfo _inputModeField;
        private static FieldInfo _requestedVisibleField;
        private static FieldInfo _disposedField;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;
            lock (Sync)
            {
                if (_installed) return;
                var setMode = AccessTools.Method(typeof(HtmlUiHost), nameof(HtmlUiHost.SetInputMode));
                if (setMode == null) throw new MissingMethodException("HtmlUiHost.SetInputMode was not found.");

                _formField = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                _webField = typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
                _inputModeField = typeof(HtmlUiHost).GetField("_inputMode", BindingFlags.Instance | BindingFlags.NonPublic);
                _requestedVisibleField = typeof(HtmlUiHost).GetField("_requestedVisible", BindingFlags.Instance | BindingFlags.NonPublic);
                _disposedField = typeof(HtmlUiHost).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic);

                if (_formField == null || _webField == null || _inputModeField == null || _requestedVisibleField == null || _disposedField == null)
                    throw new MissingMemberException("HtmlUiHost input controller fields are unavailable.");

                _harmony = new Harmony("BannerlordHtmlUI.InputController");
                _harmony.Patch(setMode, prefix: new HarmonyMethod(typeof(HtmlUiInputControllerPatch), nameof(SetInputModePrefix)));
                _installed = true;
                HtmlUiLogger.Info("Unified HTML UI input controller installed.");
            }
        }

        public static void Uninstall(HtmlUiHost host)
        {
            lock (Sync)
            {
                if (!_installed) return;
                try { _harmony?.UnpatchAll("BannerlordHtmlUI.InputController"); }
                catch (Exception ex) { HtmlUiLogger.Debug("Input controller uninstall failed: " + ex.GetBaseException().Message); }
                finally
                {
                    HtmlUiCursorController.SetOwned(__instance, false);
                    _harmony = null;
                    _formField = null;
                    _webField = null;
                    _inputModeField = null;
                    _requestedVisibleField = null;
                    _disposedField = null;
                    _installed = false;
                }
            }
        }

        private static bool SetInputModePrefix(HtmlUiHost __instance, HtmlUiInputMode mode)
        {
            if (__instance == null || IsDisposed(__instance)) return false;
            try
            {
                var previousMode = (HtmlUiInputMode)_inputModeField.GetValue(__instance);
                _inputModeField.SetValue(__instance, mode);
                _requestedVisibleField.SetValue(__instance, mode != HtmlUiInputMode.Hidden);
                try { __instance.State?.Set("framework.inputMode", mode.ToString()); } catch { }

                HtmlUiInputTraceLogger.Event(
                    "INPUT_MODE_REQUEST previous=" + previousMode +
                    " requested=" + mode);

                var form = GetForm(__instance);
                if (form == null || form.IsDisposed || !form.IsHandleCreated)
                {
                    // The overlay cannot own input it cannot show. Never leave the game blocked.
                    HtmlUiInputBlocker.SetBlocking(false, false);
                    HtmlUiCursorController.SetOwned(host, false);
                    HtmlUiInputTraceLogger.Event(
                        "INPUT_MODE_REQUEST_UNAPPLIED requested=" + mode +
                        " reason=form-not-ready");
                    return false;
                }

                var state = States.GetOrCreateValue(__instance);
                var generation = Interlocked.Increment(ref state.Generation);
                PostToUi(form, () => ApplyRequestedMode(__instance, mode, generation));
                return false;
            }
            catch (Exception ex)
            {
                HtmlUiInputBlocker.SetBlocking(false, false);
                HtmlUiNativeMouseDispatcher.Stop();
                HtmlUiCursorController.SetOwned(__instance, false);
                HtmlUiLogger.Error("Unified input mode transition failed.", ex);
                HtmlUiInputTraceLogger.Event("INPUT_MODE_REQUEST_ERROR requested=" + mode + " error=" + ex.GetBaseException().Message);
                return false;
            }
        }

        private static void ApplyRequestedMode(HtmlUiHost host, HtmlUiInputMode mode, long generation)
        {
            var state = States.GetOrCreateValue(host);
            if (generation != Volatile.Read(ref state.Generation)) return;
            if (state.Applying) return;

            state.Applying = true;
            try
            {
                if (generation != Volatile.Read(ref state.Generation)) return;
                var form = GetForm(host);
                var web = GetWeb(host);
                if (form == null || form.IsDisposed || !form.IsHandleCreated) return;

                if (!Win32.TryGetGameWindowHandle(form.Handle, out var gameHwnd) || gameHwnd == IntPtr.Zero)
                {
                    HtmlUiLogger.Warn("Input mode applied without a resolved Bannerlord window. mode=" + mode);
                    HtmlUiInputBlocker.SetBlocking(false, false);
                    HtmlUiNativeMouseDispatcher.Stop();
                    HtmlUiCursorController.SetOwned(host, false);
                    HtmlUiInputTraceLogger.Event("INPUT_MODE_APPLY_UNRESOLVED_HWND mode=" + mode);
                    if (mode == HtmlUiInputMode.Hidden || mode == HtmlUiInputMode.Passive)
                    {
                        try { Win32.ReleaseMouseCapture(); } catch { }
                        try { if (web != null) web.Enabled = false; } catch { }
                        try { form.SetPassThrough(true); } catch { }
                        try { form.Hide(); } catch { }
                    }
                    return;
                }

                HtmlUiWindowTracker.RequestSync(host);
                if (generation != Volatile.Read(ref state.Generation)) return;

                if (mode == HtmlUiInputMode.Hidden)
                {
                    HtmlUiInputBlocker.SetBlocking(false, false);
                    HtmlUiNativeMouseDispatcher.Stop();
                    HtmlUiCursorController.SetOwned(host, false);
                    RestoreGameInput(host, gameHwnd, form);
                    state.LastAppliedMode = HtmlUiInputMode.Hidden;
                    HtmlUiLogger.Info("Input mode applied: Hidden; game input restored.");
                    HtmlUiInputTraceLogger.Event(
                        "INPUT_MODE_APPLIED mode=Hidden htmlMouse=false htmlKeyboard=false nativeCapture=released webEnabled=false passThrough=true");
                    return;
                }

                if (mode == HtmlUiInputMode.Passive)
                {
                    // Passive means the game keeps everything; the overlay is display only.
                    HtmlUiInputBlocker.SetBlocking(false, false);
                    HtmlUiNativeMouseDispatcher.Stop();
                    HtmlUiCursorController.SetOwned(host, false);
                    bool captureReleased = true;
                    try { Win32.ReleaseMouseCapture(); }
                    catch (Exception ex)
                    {
                        captureReleased = false;
                        HtmlUiInputTraceLogger.Event("INPUT_MODE_PASSIVE_CAPTURE_RELEASE_ERROR error=" + ex.GetBaseException().Message);
                    }
                    try { if (web != null) web.Enabled = false; } catch { }
                    try { form.SetOwner(gameHwnd); } catch { }
                    try { form.Show(); } catch { }
                    form.SetPassThrough(true);
                    Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                    Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);

                    // Leaving MouseCaptured may have left the foreground with the WebView2 child
                    // (clicking the map activates it by design). Passive is display-only, so hand
                    // activation back to the game immediately.
                    if (Win32.GetForegroundWindow() != gameHwnd)
                    {
                        Win32.ForceSetForegroundWindow(gameHwnd);
                    }

                    HtmlUiInputTraceLogger.Event(
                        "INPUT_MODE_PASSIVE_APPLIED htmlMouse=false htmlKeyboard=false nativeCaptureReleased=" + captureReleased +
                        " webEnabled=false passThrough=true mouseOnly=false");
                }
                else if (mode == HtmlUiInputMode.MouseCaptured)
                {
                    // The overlay owns the mouse, the game keeps the keyboard.
                    HtmlUiInputBlocker.SetBlocking(true, false);
                    HtmlUiCursorController.SetOwned(host, true);
                    HtmlUiNativeMouseDispatcher.EnsureStarted(host);
                    try { if (web != null) web.Enabled = true; } catch { }
                    try { form.SetOwner(gameHwnd); } catch { }
                    try { form.Show(); } catch { }
                    form.SetMouseOnly(true);
                    Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                    Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);

                    // Do NOT fight the activation here, and do NOT MoveFocus: probe logs proved
                    // Chromium's input pipeline stays fully closed in this state either way. The
                    // game keeps polling input through its own tick (Input.IsKeyDown /
                    // GetAsyncKeyState work unfocused), which is what ESC and N rely on.
                    // Passive/Hidden restore the game foreground.

                    HtmlUiInputTraceLogger.Event(
                        "INPUT_MODE_MOUSE_CAPTURED_APPLIED htmlMouse=true htmlKeyboard=false mouseOnly=true noActivate=true");
                }
                else
                {
                    HtmlUiInputBlocker.SetBlocking(true, true);
                    HtmlUiCursorController.SetOwned(host, true);
                    HtmlUiNativeMouseDispatcher.EnsureStarted(host);
                    try { if (web != null) web.Enabled = true; } catch { }
                    try { form.SetOwner(gameHwnd); } catch { }
                    try { form.Show(); } catch { }
                    form.SetPassThrough(false);
                    Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                    Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                    if (mode == HtmlUiInputMode.Captured)
                        ActivateCapturedForm(form, web, gameHwnd);
                }

                if (generation != Volatile.Read(ref state.Generation)) return;
                state.LastAppliedMode = mode;
                HtmlUiLogger.Info("Input mode applied: " + mode + ", overlayHwnd=" + form.Handle + ", gameHwnd=" + gameHwnd);
                HtmlUiInputTraceLogger.Event(
                    "INPUT_MODE_APPLIED mode=" + mode +
                    " htmlMouse=" + (mode == HtmlUiInputMode.Captured || mode == HtmlUiInputMode.MouseCaptured) +
                    " htmlKeyboard=" + (mode == HtmlUiInputMode.Captured) +
                    " passThrough=" + (mode == HtmlUiInputMode.Passive) +
                    " mouseOnly=" + (mode == HtmlUiInputMode.MouseCaptured));
                HtmlUiWindowTracker.RequestSync(host);
            }
            finally
            {
                state.Applying = false;
            }
        }

        private static void RestoreGameInput(HtmlUiHost host, IntPtr gameHwnd, HtmlUiOverlayForm form)
        {
            var web = GetWeb(host);
            try { Win32.ReleaseMouseCapture(); } catch { }
            try { if (web != null) web.Enabled = false; } catch { }
            try { form.SetPassThrough(true); } catch { }

            try
            {
                var foreground = Win32.GetForegroundWindow();
                if (foreground == form.Handle && gameHwnd != IntPtr.Zero && Win32.IsWindow(gameHwnd))
                    Win32.SetForegroundWindow(gameHwnd);
            }
            catch { }

            try { form.Hide(); } catch { }
        }

        private static void ActivateCapturedForm(HtmlUiOverlayForm form, System.Windows.Forms.Control web, IntPtr gameHwnd)
        {
            var before = Win32.GetForegroundWindow();
            HtmlUiInputTraceLogger.Event(
                "CAPTURED_ACTIVATE_ATTEMPT foreground=" + before +
                " gameHwnd=" + gameHwnd +
                " overlayHwnd=" + form.Handle);
            try
            {
                Win32.SetForegroundWindow(form.Handle);
                form.Activate();
                if (web != null) web.Focus();
                else if (form.Controls.Count > 0) form.Controls[0]?.Focus();
            }
            catch (Exception ex)
            {
                HtmlUiInputTraceLogger.Event("CAPTURED_ACTIVATE_ERROR error=" + ex.GetBaseException().Message);
                try { form.Activate(); } catch { }
            }
            var after = Win32.GetForegroundWindow();
            HtmlUiInputTraceLogger.Event(
                "CAPTURED_ACTIVATE_RESULT foreground=" + after +
                " overlayHwnd=" + form.Handle +
                " webFocused=" + (web != null && web.Focused));
        }

        internal static void EnsureCapturedFocus(HtmlUiHost host)
        {
            if (host == null || host.InputMode != HtmlUiInputMode.Captured || IsDisposed(host)) return;
            var form = GetForm(host);
            var web = GetWeb(host);
            if (form == null || form.IsDisposed || !form.Visible || !form.IsHandleCreated) return;

            var foreground = Win32.GetForegroundWindow();
            bool overlayOwnsForeground = foreground == form.Handle ||
                                         (foreground != IntPtr.Zero && Win32.IsChild(form.Handle, foreground));
            if (overlayOwnsForeground && (web == null || web.Focused)) return;
            bool webWasFocused = web != null && web.Focused;

            // Captured may remain active while the user Alt+Tabs. Only recover from Bannerlord
            // reclaiming focus; never pull an unrelated foreground application back to the game.
            if (!overlayOwnsForeground &&
                (!Win32.TryGetGameWindowHandle(form.Handle, out var gameHwnd) || foreground != gameHwnd))
                return;

            try
            {
                if (!overlayOwnsForeground)
                {
                    Win32.SetForegroundWindow(form.Handle);
                    form.Activate();
                }
                if (web != null && !web.Focused) web.Focus();
                bool webIsFocused = web != null && web.Focused;
                if (!overlayOwnsForeground || (!webWasFocused && webIsFocused))
                {
                    HtmlUiInputTraceLogger.Event(
                        "CAPTURED_FOCUS_RECOVERY previousForeground=" + foreground +
                        " foreground=" + Win32.GetForegroundWindow() +
                        " webFocused=" + webIsFocused);
                }
            }
            catch (Exception ex)
            {
                HtmlUiInputTraceLogger.Event("CAPTURED_FOCUS_RECOVERY_ERROR error=" + ex.GetBaseException().Message);
            }
        }

        private static object GetCoreWebView2Controller(WebView2 web)
        {
            if (web == null) return null;
            foreach (var field in web.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (typeof(Microsoft.Web.WebView2.Core.CoreWebView2Controller).IsAssignableFrom(field.FieldType))
                    return field.GetValue(web);
            }
            return null;
        }

        private static HtmlUiOverlayForm GetForm(HtmlUiHost host) { return _formField?.GetValue(host) as HtmlUiOverlayForm; }
        private static System.Windows.Forms.Control GetWeb(HtmlUiHost host) { return _webField?.GetValue(host) as System.Windows.Forms.Control; }
        private static bool IsDisposed(HtmlUiHost host) { return _disposedField != null && (bool)_disposedField.GetValue(host); }

        private static void PostToUi(HtmlUiOverlayForm form, Action action)
        {
            try
            {
                if (form.InvokeRequired) form.BeginInvoke(action);
                else action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }
}
