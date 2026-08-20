using System;
using System.Reflection;
using HarmonyLib;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Compatibility facade for consumers that historically called HtmlUiMouseCapture.Capture().
    /// The Framework host now owns the MouseCaptured state and native-window lifecycle; this class
    /// must not intercept or rewrite HtmlUiHost.SetInputMode().
    /// </summary>
    public static class HtmlUiMouseCapture
    {
        private const string HarmonyId = "BannerlordHtmlUI.MouseCapture";
        private static readonly object Sync = new object();
        private static Harmony _harmony;
        private static bool _installed;
        private static FieldInfo _formField;
        private static MethodInfo _applyInputModeOnUiThread;

        internal static void Install()
        {
            lock (Sync)
            {
                if (_installed) return;

                _formField = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                _applyInputModeOnUiThread = AccessTools.Method(typeof(HtmlUiHost), "ApplyInputModeOnUiThread");
                if (_formField == null || _applyInputModeOnUiThread == null)
                    throw new MissingMemberException("HtmlUiHost mouse-capture internals were not found.");

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(
                    _applyInputModeOnUiThread,
                    postfix: new HarmonyMethod(typeof(HtmlUiMouseCapture), nameof(ApplyInputModePostfix)));

                _installed = true;
                HtmlUiLogger.Info("Mouse-only HTML UI input compatibility hook installed; Host owns MouseCaptured state.");
            }
        }

        internal static void Uninstall()
        {
            lock (Sync)
            {
                if (!_installed) return;
                try { _harmony?.UnpatchAll(HarmonyId); }
                finally
                {
                    _harmony = null;
                    _formField = null;
                    _applyInputModeOnUiThread = null;
                    _installed = false;
                }
            }
        }

        public static void Capture()
        {
            Install();
            HtmlUiService.SetInputMode(HtmlUiInputMode.MouseCaptured);
        }

        private static void ApplyInputModePostfix(HtmlUiHost __instance)
        {
            if (__instance == null || __instance.InputMode != HtmlUiInputMode.MouseCaptured) return;
            try
            {
                var form = _formField?.GetValue(__instance) as HtmlUiOverlayForm;
                if (form == null || form.IsDisposed) return;
                ApplyMouseWindow(form);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Failed to reapply mouse input policy: " + ex.GetBaseException().Message);
            }
        }

        private static void ApplyMouseWindow(HtmlUiOverlayForm form)
        {
            form.SetPassThrough(false);
            if (!form.IsHandleCreated) return;

            // MouseCaptured is the only Framework mode that intentionally allows the overlay
            // to activate. This is required for a native mouse sequence to reach WebView2.
            Win32.SetNoActivate(form.Handle, false);
            Win32.ShowWindow(form.Handle, 1 /* SW_SHOWNORMAL */);
            Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
            HtmlUiLogger.Info("MouseCaptured native window configured as activatable/hit-testable.");
        }
    }
}
