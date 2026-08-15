using System;
using System.Diagnostics;
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
                    "_requestedVisible",
                    BindingFlags.Instance | BindingFlags.NonPublic);

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

                var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero && Win32.IsWindow(hwnd))
                {
                    _diagnosticLogged = false;
                    return true;
                }

                // MainWindowHandle can transiently become zero during Bannerlord
                // focus/scene transitions. A missing HWND means "cannot resync
                // position this tick", not "hide the active HTML UI".
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
                    "Window tracking HWND=0 guard failed: " + ex.GetBaseException().Message);
                return true;
            }
        }
    }
}
