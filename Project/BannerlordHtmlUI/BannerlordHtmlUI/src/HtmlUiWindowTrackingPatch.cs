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

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;

            lock (Sync)
            {
                if (_installed) return;

                var method = AccessTools.Method(typeof(HtmlUiHost), "FollowBannerlordWindow");
                if (method == null)
                    throw new MissingMethodException("HtmlUiHost.FollowBannerlordWindow was not found.");

                _requestedVisibleField = typeof(HtmlUiHost).GetField("_requestedVisible", BindingFlags.Instance | BindingFlags.NonPublic);
                _harmony = new Harmony("BannerlordHtmlUI.WindowTracking");
                _harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(HtmlUiWindowTrackingPatch), nameof(BeforeFollowBannerlordWindow)));

                _installed = true;
                HtmlUiLogger.Info("Window tracking stability patch installed.");
            }
        }

        private static bool BeforeFollowBannerlordWindow(HtmlUiHost __instance)
        {
            try
            {
                var requestedVisible = _requestedVisibleField != null &&
                    (bool)_requestedVisibleField.GetValue(__instance);

                if (!requestedVisible) return true;

                var hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero) return true;

                // Bannerlord can transiently expose MainWindowHandle == 0 during
                // focus/scene transitions. Do not hide an active HTML overlay just
                // because the native HWND is temporarily unavailable.
                HtmlUiLogger.Debug("Window tracking skipped: Bannerlord MainWindowHandle is temporarily zero while HTML UI is visible.");
                return false;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Window tracking stability guard failed: " + ex.GetBaseException().Message);
                return true;
            }
        }
    }
}
