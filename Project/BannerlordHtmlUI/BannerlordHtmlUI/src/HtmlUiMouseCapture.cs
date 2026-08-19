using System;
using System.Reflection;
using HarmonyLib;
using System.Windows.Forms;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Mouse-only input capture for overlays. The overlay is allowed to become the
    /// active window for the mouse click, then immediately returns keyboard focus
    /// to Bannerlord after the button-down message has entered WebView2.
    /// </summary>
    public static class HtmlUiMouseCapture
    {
        private const string HarmonyId = "BannerlordHtmlUI.MouseCapture";
        private static readonly object Sync = new object();
        private static Harmony _harmony;
        private static bool _installed;
        private static FieldInfo _inputModeField;
        private static FieldInfo _requestedVisibleField;
        private static FieldInfo _formField;
        private static MethodInfo _applyInputModeOnUiThread;

        public static void Capture()
        {
            Install();
            HtmlUiService.SetInputMode(HtmlUiInputMode.MouseCaptured);
        }

        private static void Install()
        {
            lock (Sync)
            {
                if (_installed) return;

                _inputModeField = typeof(HtmlUiHost).GetField("_inputMode", BindingFlags.Instance | BindingFlags.NonPublic);
                _requestedVisibleField = typeof(HtmlUiHost).GetField("_requestedVisible", BindingFlags.Instance | BindingFlags.NonPublic);
                _formField = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                _applyInputModeOnUiThread = AccessTools.Method(typeof(HtmlUiHost), "ApplyInputModeOnUiThread");

                if (_inputModeField == null || _requestedVisibleField == null || _formField == null || _applyInputModeOnUiThread == null)
                    throw new MissingMemberException("HtmlUiHost mouse-capture internals were not found.");

                _harmony = new Harmony(HarmonyId);
                var setInputMode = AccessTools.Method(typeof(HtmlUiHost), "SetInputMode");
                _harmony.Patch(
                    setInputMode,
                    prefix: new HarmonyMethod(typeof(HtmlUiMouseCapture), nameof(SetInputModePrefix)));
                _harmony.Patch(
                    _applyInputModeOnUiThread,
                    postfix: new HarmonyMethod(typeof(HtmlUiMouseCapture), nameof(ApplyInputModePostfix)));

                _installed = true;
                HtmlUiLogger.Info("Mouse-only HTML UI input capture installed.");
            }
        }

        private static bool SetInputModePrefix(HtmlUiHost __instance, HtmlUiInputMode mode)
        {
            if (mode != HtmlUiInputMode.MouseCaptured) return true;

            try
            {
                _inputModeField.SetValue(__instance, mode);
                _requestedVisibleField.SetValue(__instance, true);
                __instance.State?.Set("framework.inputMode", mode.ToString());

                var form = _formField.GetValue(__instance) as HtmlUiOverlayForm;
                if (form == null || form.IsDisposed) return false;

                Action apply = () => ApplyMouseWindow(form);
                if (form.InvokeRequired) form.BeginInvoke(apply);
                else apply();
                return false;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Mouse-only input capture failed.", ex);
                return false;
            }
        }

        private static void ApplyInputModePostfix(HtmlUiHost __instance)
        {
            if (__instance.InputMode != HtmlUiInputMode.MouseCaptured) return;
            try
            {
                var form = _formField.GetValue(__instance) as HtmlUiOverlayForm;
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

            // MouseCaptured must not carry WS_EX_NOACTIVATE. That style prevents the
            // inactive WinForms/WebView2 window from receiving the click at all.
            Win32.SetNoActivate(form.Handle, false);
            Win32.ShowWindow(form.Handle, 1 /* SW_SHOWNORMAL */);
            HtmlUiLogger.Info("MouseCaptured native window configured as activatable/hit-testable.");
        }
    }
}
