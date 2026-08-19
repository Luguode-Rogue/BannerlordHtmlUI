using System;
using System.Diagnostics;
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

                // MouseCaptured is allowed to activate when the user clicks the
                // overlay. The old tracking path repeatedly re-applied
                // WS_EX_NOACTIVATE every 100 ms, which prevented WebView2 from ever
                // receiving pointerdown. Keep the overlay visible/hit-testable and
                // let the overlay form manage keyboard focus explicitly.
                if (__instance.InputMode == HtmlUiInputMode.MouseCaptured)
                {
                    var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                    if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd) || !Win32.GetWindowRect(hwnd, out var rect))
                    {
                        if (!_diagnosticLogged)
                        {
                            HtmlUiLogger.Warn(
                                "MouseCaptured window tracking skipped: Bannerlord main window is temporarily unavailable; preserving overlay state.");
                            _diagnosticLogged = true;
                        }
                        return false;
                    }

                    _diagnosticLogged = false;
                    var form = _formField?.GetValue(__instance) as HtmlUiOverlayForm;
                    if (form == null || form.IsDisposed || !form.IsHandleCreated)
                        return false;

                    var width = Math.Max(0, rect.Right - rect.Left);
                    var height = Math.Max(0, rect.Bottom - rect.Top);
                    form.Bounds = new Rectangle(rect.Left, rect.Top, width, height);
                    form.SetPassThrough(false);
                    Win32.SetNoActivate(form.Handle, false);
                    Win32.ShowWindow(form.Handle, 1 /* SW_SHOWNORMAL */);
                    Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                    return false;
                }

                var normalHwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (normalHwnd != IntPtr.Zero && Win32.IsWindow(normalHwnd))
                {
                    _diagnosticLogged = false;
                    return true;
                }

                if (!_diagnosticLogged)
                {
                    HtmlUiLogger.Warn(
                        "Window tracking skipped: Bannerlord main window is temporarily unavailable; preserving requested HtmlUI visibility.");
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
