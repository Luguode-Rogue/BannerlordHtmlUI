using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Single native input/window owner for the HTML overlay.
    /// PageManager decides which page is open; this controller alone decides how the
    /// overlay participates in Win32 focus, visibility, hit testing and WebView input.
    /// </summary>
    internal static class HtmlUiInputControllerPatch
    {
        private sealed class HostState
        {
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
        private static FieldInfo _windowStateChangedField;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;
            lock (Sync)
            {
                if (_installed) return;

                var setMode = AccessTools.Method(typeof(HtmlUiHost), nameof(HtmlUiHost.SetInputMode));
                var follow = AccessTools.Method(typeof(HtmlUiHost), "FollowBannerlordWindow");
                if (setMode == null || follow == null)
                    throw new MissingMethodException("HtmlUiHost input control targets were not found.");

                _formField = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                _webField = typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
                _inputModeField = typeof(HtmlUiHost).GetField("_inputMode", BindingFlags.Instance | BindingFlags.NonPublic);
                _requestedVisibleField = typeof(HtmlUiHost).GetField("_requestedVisible", BindingFlags.Instance | BindingFlags.NonPublic);
                _disposedField = typeof(HtmlUiHost).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic);
                _windowStateChangedField = typeof(HtmlUiHost).GetField("WindowStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);

                if (_formField == null || _webField == null || _inputModeField == null || _requestedVisibleField == null || _disposedField == null)
                    throw new MissingMemberException("HtmlUiHost input controller fields are unavailable.");

                _harmony = new Harmony("BannerlordHtmlUI.InputController");
                _harmony.Patch(
                    setMode,
                    prefix: new HarmonyMethod(typeof(HtmlUiInputControllerPatch), nameof(SetInputModePrefix)));
                _harmony.Patch(
                    follow,
                    prefix: new HarmonyMethod(typeof(HtmlUiInputControllerPatch), nameof(FollowBannerlordWindowPrefix)));

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
                    _harmony = null;
                    _formField = null;
                    _webField = null;
                    _inputModeField = null;
                    _requestedVisibleField = null;
                    _disposedField = null;
                    _windowStateChangedField = null;
                    _installed = false;
                }
            }
        }

        private static bool SetInputModePrefix(HtmlUiHost __instance, HtmlUiInputMode mode)
        {
            if (__instance == null || IsDisposed(__instance)) return false;

            try
            {
                _inputModeField.SetValue(__instance, mode);
                _requestedVisibleField.SetValue(__instance, mode != HtmlUiInputMode.Hidden);
                try { __instance.State?.Set("framework.inputMode", mode.ToString()); } catch { }

                var form = GetForm(__instance);
                if (form == null || form.IsDisposed || !form.IsHandleCreated)
                    return false;

                PostToUi(form, () => ApplyRequestedMode(__instance, mode));
                return false;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Unified input mode transition failed.", ex);
                return false;
            }
        }

        private static bool FollowBannerlordWindowPrefix(HtmlUiHost __instance)
        {
            try
            {
                var form = GetForm(__instance);
                if (form == null || form.IsDisposed || !form.IsHandleCreated) return false;

                if (!Win32.TryGetGameWindowHandle(form.Handle, out var gameHwnd) ||
                    !Win32.GetWindowRect(gameHwnd, out var rect))
                {
                    if (GetMode(__instance) == HtmlUiInputMode.Hidden || !IsRequestedVisible(__instance))
                    {
                        try { form.Hide(); } catch { }
                    }
                    return false;
                }

                var mode = GetMode(__instance);
                var requestedVisible = IsRequestedVisible(__instance);
                PlaceOverlay(form, gameHwnd, rect);

                if (mode == HtmlUiInputMode.Hidden || !requestedVisible)
                {
                    RestoreGameInput(__instance, gameHwnd, form);
                    ApplyWindowState(__instance, false, gameHwnd, rect);
                    return false;
                }

                if (Win32.IsIconic(gameHwnd) || !Win32.IsWindowVisible(gameHwnd))
                {
                    try { Win32.ReleaseMouseCapture(); } catch { }
                    try { form.Hide(); } catch { }
                    ApplyWindowState(__instance, false, gameHwnd, rect);
                    return false;
                }

                var foreground = Win32.GetForegroundWindow();
                var gameForeground = foreground == gameHwnd;
                var overlayForeground = foreground == form.Handle;

                if (!form.Visible)
                {
                    try { form.Show(); } catch { }
                }

                switch (mode)
                {
                    case HtmlUiInputMode.Passive:
                        try { form.SetPassThrough(true); } catch { }
                        Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                        Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                        break;

                    case HtmlUiInputMode.Captured:
                        try { form.SetPassThrough(false); } catch { }
                        Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                        Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                        if (gameForeground && !overlayForeground)
                            ActivateCapturedForm(form);
                        break;

                    case HtmlUiInputMode.MouseCaptured:
                        try { form.SetPassThrough(false); } catch { }
                        Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                        Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                        break;
                }

                ApplyWindowState(__instance, true, gameHwnd, rect);
                return false;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Input controller window tracking failed: " + ex.GetBaseException().Message);
                return false;
            }
        }

        private static void ApplyRequestedMode(HtmlUiHost host, HtmlUiInputMode mode)
        {
            var state = States.GetOrCreateValue(host);
            if (state.Applying) return;
            state.Applying = true;
            try
            {
                var form = GetForm(host);
                var web = GetWeb(host);
                if (form == null || form.IsDisposed || !form.IsHandleCreated) return;

                if (!Win32.TryGetGameWindowHandle(form.Handle, out var gameHwnd) || gameHwnd == IntPtr.Zero)
                {
                    HtmlUiLogger.Warn("Input mode applied without a resolved Bannerlord window. mode=" + mode);
                    if (mode == HtmlUiInputMode.Hidden)
                    {
                        try { Win32.ReleaseMouseCapture(); } catch { }
                        try { if (web != null) web.Enabled = false; } catch { }
                        try { form.SetPassThrough(true); } catch { }
                        try { form.Hide(); } catch { }
                    }
                    return;
                }

                PlaceOverlay(form, gameHwnd, GetWindowRect(gameHwnd));

                if (mode == HtmlUiInputMode.Hidden)
                {
                    RestoreGameInput(host, gameHwnd, form);
                    state.LastAppliedMode = HtmlUiInputMode.Hidden;
                    HtmlUiLogger.Info("Input mode applied: Hidden; game input restored.");
                    return;
                }

                try { if (web != null) web.Enabled = true; } catch { }
                try { form.SetOwner(gameHwnd); } catch { }
                try { form.Show(); } catch { }

                if (mode == HtmlUiInputMode.Passive)
                {
                    form.SetPassThrough(true);
                    Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                    Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                }
                else
                {
                    form.SetPassThrough(false);
                    Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                    Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);

                    var foreground = Win32.GetForegroundWindow();
                    if (mode == HtmlUiInputMode.Captured && (foreground == gameHwnd || foreground == form.Handle))
                        ActivateCapturedForm(form);
                }

                state.LastAppliedMode = mode;
                HtmlUiLogger.Info("Input mode applied: " + mode + ", overlayHwnd=" + form.Handle + ", gameHwnd=" + gameHwnd);
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

            // Restore Bannerlord while the overlay is still alive, then hide it.
            // This is the critical close invariant for repeated M/ESC cycles.
            if (gameHwnd != IntPtr.Zero && Win32.IsWindow(gameHwnd))
            {
                try { Win32.SetForegroundWindow(gameHwnd); } catch { }
            }

            try { form.Hide(); } catch { }
        }

        private static void ActivateCapturedForm(HtmlUiOverlayForm form)
        {
            try
            {
                Win32.SetForegroundWindow(form.Handle);
                form.Activate();
                if (form.Controls.Count > 0)
                    form.Controls[0]?.Focus();
            }
            catch
            {
                try { form.Activate(); } catch { }
            }
        }

        private static void PlaceOverlay(HtmlUiOverlayForm form, IntPtr gameHwnd, Win32.RECT rect)
        {
            var width = Math.Max(1, rect.Right - rect.Left);
            var height = Math.Max(1, rect.Bottom - rect.Top);
            form.SetOwner(gameHwnd);
            form.Bounds = new Rectangle(rect.Left, rect.Top, width, height);
        }

        private static Win32.RECT GetWindowRect(IntPtr hwnd)
        {
            return Win32.GetWindowRect(hwnd, out var rect) ? rect : default(Win32.RECT);
        }

        private static void ApplyWindowState(HtmlUiHost host, bool visible, IntPtr gameHwnd, Win32.RECT rect)
        {
            try
            {
                var foreground = Win32.GetForegroundWindow();
                var overlay = GetForm(host);
                var state = new HtmlUiWindowState(
                    foreground == gameHwnd || (overlay != null && foreground == overlay.Handle),
                    visible,
                    Win32.IsIconic(gameHwnd),
                    rect.Left,
                    rect.Top,
                    Math.Max(0, rect.Right - rect.Left),
                    Math.Max(0, rect.Bottom - rect.Top));

                if (_windowStateChangedField == null) return;
                var handler = _windowStateChangedField.GetValue(host) as Action<HtmlUiWindowState>;
                try { handler?.Invoke(state); } catch (Exception ex) { HtmlUiLogger.Debug("Window state callback failed: " + ex.GetBaseException().Message); }
            }
            catch { }
        }

        private static HtmlUiOverlayForm GetForm(HtmlUiHost host) => _formField?.GetValue(host) as HtmlUiOverlayForm;
        private static System.Windows.Forms.Control GetWeb(HtmlUiHost host) => _webField?.GetValue(host) as System.Windows.Forms.Control;
        private static HtmlUiInputMode GetMode(HtmlUiHost host) => _inputModeField == null ? HtmlUiInputMode.Hidden : (HtmlUiInputMode)_inputModeField.GetValue(host);
        private static bool IsRequestedVisible(HtmlUiHost host) => _requestedVisibleField != null && (bool)_requestedVisibleField.GetValue(host);
        private static bool IsDisposed(HtmlUiHost host) => _disposedField != null && (bool)_disposedField.GetValue(host);

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
