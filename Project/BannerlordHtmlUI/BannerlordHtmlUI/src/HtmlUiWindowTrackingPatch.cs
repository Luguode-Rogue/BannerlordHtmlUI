using System;
using System.Drawing;
using System.Reflection;
using HarmonyLib;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiWindowTrackingPatch
    {
        private static readonly object Sync = new object();
        private static bool _installed;
        private static Harmony _harmony;
        private static FieldInfo _requestedVisibleField;
        private static FieldInfo _formField;
        private static bool _diagnosticLogged;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;

            lock (Sync)
            {
                if (_installed) return;

                var method = AccessTools.Method(typeof(HtmlUiHost), "FollowBannerlordWindow");
                if (method == null)
                    throw new MissingMethodException("HtmlUiHost.FollowBannerlordWindow was not found.");

                _requestedVisibleField = typeof(HtmlUiHost).GetField(
                    "_requestedVisible", BindingFlags.Instance | BindingFlags.NonPublic);
                _formField = typeof(HtmlUiHost).GetField(
                    "_form", BindingFlags.Instance | BindingFlags.NonPublic);

                _harmony = new Harmony("BannerlordHtmlUI.WindowTracking");
                _harmony.Patch(
                    method,
                    prefix: new HarmonyMethod(
                        typeof(HtmlUiWindowTrackingPatch),
                        nameof(BeforeFollowBannerlordWindow)));

                _installed = true;
                _diagnosticLogged = false;
                HtmlUiLogger.Info("Window tracking HWND=0 stability guard installed.");
            }
        }

        public static void Uninstall()
        {
            lock (Sync)
            {
                if (!_installed) return;

                try
                {
                    _harmony?.UnpatchAll("BannerlordHtmlUI.WindowTracking");
                }
                finally
                {
                    _harmony = null;
                    _requestedVisibleField = null;
                    _formField = null;
                    _installed = false;
                    _diagnosticLogged = false;
                }
            }
        }

        private static bool BeforeFollowBannerlordWindow(HtmlUiHost __instance)
        {
            try
            {
                var requestedVisible = _requestedVisibleField != null &&
                    (bool)_requestedVisibleField.GetValue(__instance);

                if (!requestedVisible)
                {
                    _diagnosticLogged = false;
                    return true;
                }

                var form = _formField?.GetValue(__instance) as HtmlUiOverlayForm;
                var excludedWindow = form != null && form.IsHandleCreated ? form.Handle : IntPtr.Zero;

                if (__instance.InputMode == HtmlUiInputMode.MouseCaptured)
                {
                    if (!Win32.TryGetGameWindowHandle(excludedWindow, out var hwnd) ||
                        !Win32.GetWindowRect(hwnd, out var rect))
                    {
                        if (!_diagnosticLogged)
                        {
                            HtmlUiLogger.Warn(
                                "MouseCaptured window tracking skipped: Bannerlord window could not be resolved; preserving overlay state.");
                            _diagnosticLogged = true;
                        }
                        return false;
                    }

                    _diagnosticLogged = false;
                    if (form == null || form.IsDisposed || !form.IsHandleCreated)
                        return false;

                    // The overlay may be created before Bannerlord exposes a usable main HWND.
                    // Rebind the owner every time the game HWND is successfully resolved so the
                    // overlay is always in Bannerlord's owned-window Z-order.
                    form.SetOwner(hwnd);

                    var foreground = Win32.GetForegroundWindow();
                    if (Win32.IsIconic(hwnd) || !Win32.IsWindowVisible(hwnd))
                    {
                        if (form.Visible) form.Hide();
                        return false;
                    }

                    // MouseCaptured deliberately allows the overlay to become foreground
                    // for the duration of a real WebView2 mouse interaction. MouseUp restores
                    // Bannerlord focus through HtmlUiKeyboardAndDiagnosticsPatch.
                    var overlayForeground = foreground == form.Handle;
                    var gameForeground = foreground == hwnd;
                    if (!gameForeground && !overlayForeground)
                    {
                        if (form.Visible) form.Hide();
                        return false;
                    }

                    var width = Math.Max(0, rect.Right - rect.Left);
                    var height = Math.Max(0, rect.Bottom - rect.Top);
                    form.Bounds = new Rectangle(rect.Left, rect.Top, width, height);
                    form.SetPassThrough(false);

                    // MouseCaptured must be activatable. SetPassThrough(false) removes
                    // WS_EX_NOACTIVATE; do not re-add it here or WM_MOUSEACTIVATE/pointer
                    // delivery will be suppressed at the native window boundary.
                    Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                    Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                    return false;
                }

                if (Win32.TryGetGameWindowHandle(excludedWindow, out var passiveHwnd))
                {
                    _diagnosticLogged = false;
                    if (form != null && !form.IsDisposed && form.IsHandleCreated)
                        form.SetOwner(passiveHwnd);
                    return true;
                }

                if (!_diagnosticLogged)
                {
                    HtmlUiLogger.Warn(
                        "Window tracking skipped: Bannerlord window could not be resolved; preserving requested HtmlUI visibility.");
                    _diagnosticLogged = true;
                }

                return false;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug(
                    "Window tracking guard failed: " + ex.GetBaseException().Message);
                return true;
            }
        }
    }
}
