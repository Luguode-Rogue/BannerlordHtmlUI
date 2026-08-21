using System;
using System.Drawing;
using System.Reflection;
using HarmonyLib;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Sole owner of the periodic overlay placement policy.
    /// It may restore focus when the user returns to Bannerlord, but it must never
    /// steal foreground focus from an unrelated external application.
    /// </summary>
    internal static class HtmlUiFocusPolicyPatch
    {
        private static readonly object Sync = new object();
        private static Harmony _harmony;
        private static bool _installed;

        private static FieldInfo _formField;
        private static FieldInfo _inputModeField;
        private static FieldInfo _requestedVisibleField;

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;

            lock (Sync)
            {
                if (_installed) return;

                var followTarget = AccessTools.Method(typeof(HtmlUiHost), "FollowBannerlordWindow");
                var hiddenTarget = AccessTools.Method(typeof(HtmlUiHost), "ApplyHiddenInputState");
                if (followTarget == null)
                    throw new MissingMethodException("HtmlUiHost.FollowBannerlordWindow was not found.");
                if (hiddenTarget == null)
                    throw new MissingMethodException("HtmlUiHost.ApplyHiddenInputState was not found.");

                _formField = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                _inputModeField = typeof(HtmlUiHost).GetField("_inputMode", BindingFlags.Instance | BindingFlags.NonPublic);
                _requestedVisibleField = typeof(HtmlUiHost).GetField("_requestedVisible", BindingFlags.Instance | BindingFlags.NonPublic);

                if (_formField == null || _inputModeField == null || _requestedVisibleField == null)
                    throw new MissingMemberException("HtmlUiHost focus policy fields are unavailable.");

                _harmony = new Harmony("BannerlordHtmlUI.FocusPolicy");
                _harmony.Patch(
                    followTarget,
                    prefix: new HarmonyMethod(
                        typeof(HtmlUiFocusPolicyPatch),
                        nameof(BeforeFollowBannerlordWindow)));

                _harmony.Patch(
                    hiddenTarget,
                    prefix: new HarmonyMethod(
                        typeof(HtmlUiFocusPolicyPatch),
                        nameof(BeforeApplyHiddenInputState)));

                _installed = true;
                HtmlUiLogger.Info("Non-stealing overlay focus policy installed.");
            }
        }

        public static void Uninstall()
        {
            lock (Sync)
            {
                if (!_installed) return;
                try { _harmony?.UnpatchAll("BannerlordHtmlUI.FocusPolicy"); }
                finally
                {
                    _harmony = null;
                    _formField = null;
                    _inputModeField = null;
                    _requestedVisibleField = null;
                    _installed = false;
                }
            }
        }

        private static bool BeforeFollowBannerlordWindow(HtmlUiHost __instance)
        {
            try
            {
                var form = _formField.GetValue(__instance) as HtmlUiOverlayForm;
                var mode = (HtmlUiInputMode)_inputModeField.GetValue(__instance);
                var requestedVisible = (bool)_requestedVisibleField.GetValue(__instance);

                if (form == null || form.IsDisposed || !form.IsHandleCreated)
                    return false;

                var excluded = form.Handle;
                if (!Win32.TryGetGameWindowHandle(excluded, out var gameHwnd) ||
                    !Win32.GetWindowRect(gameHwnd, out var rect))
                {
                    if (mode == HtmlUiInputMode.Hidden || !requestedVisible)
                    {
                        try { form.Hide(); } catch { }
                    }
                    return false;
                }

                var width = Math.Max(1, rect.Right - rect.Left);
                var height = Math.Max(1, rect.Bottom - rect.Top);
                form.SetOwner(gameHwnd);
                form.Bounds = new Rectangle(rect.Left, rect.Top, width, height);

                if (mode == HtmlUiInputMode.Hidden || !requestedVisible)
                {
                    // Hidden input restoration is handled by the dedicated prefix below.
                    // Do not let the original FollowBannerlordWindow implementation run.
                    ApplyHiddenInputStateSafe(__instance, gameHwnd);
                    return false;
                }

                if (Win32.IsIconic(gameHwnd) || !Win32.IsWindowVisible(gameHwnd))
                {
                    try { form.Hide(); } catch { }
                    return false;
                }

                if (!form.Visible)
                    form.Show();

                var foreground = Win32.GetForegroundWindow();
                var gameIsForeground = foreground == gameHwnd;
                var overlayIsForeground = foreground == form.Handle;

                if (mode == HtmlUiInputMode.Passive)
                {
                    form.SetPassThrough(true);
                    Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                    Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                    return false;
                }

                form.SetPassThrough(false);
                Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);

                // Critical rule: when another process/window owns the foreground,
                // do absolutely nothing to focus. This makes Alt+Tab work normally.
                if (gameIsForeground && !overlayIsForeground)
                {
                    Win32.SetForegroundWindow(form.Handle);
                    form.Activate();
                    try { form.Controls[0]?.Focus(); } catch { }
                }

                return false;
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Focus policy failed: " + ex.GetBaseException().Message);
                return false;
            }
        }

        private static bool BeforeApplyHiddenInputState(HtmlUiHost __instance, IntPtr gameWindow)
        {
            try
            {
                ApplyHiddenInputStateSafe(__instance, gameWindow);
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Hidden input focus restoration failed.", ex);
            }

            // Always suppress the original implementation. The original order is
            // Hide() -> SetForegroundWindow(game), which can leave Bannerlord without
            // keyboard focus because the overlay was the foreground window at the time.
            return false;
        }

        private static void ApplyHiddenInputStateSafe(HtmlUiHost host, IntPtr gameWindow)
        {
            var form = _formField?.GetValue(host) as HtmlUiOverlayForm;

            try { Win32.ReleaseMouseCapture(); } catch { }
            try
            {
                var webField = typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
                var web = webField?.GetValue(host) as System.Windows.Forms.Control;
                if (web != null) web.Enabled = false;
            }
            catch { }

            if (form != null && !form.IsDisposed && form.IsHandleCreated)
            {
                try { form.SetPassThrough(true); } catch { }

                // IMPORTANT: restore Bannerlord foreground while the overlay is still
                // the active window, then hide the overlay. This preserves the foreground
                // activation chain and allows the game's Input.IsKeyPressed() to see M/ESC
                // immediately after the HTML UI closes.
                if (gameWindow != IntPtr.Zero && Win32.IsWindow(gameWindow))
                {
                    try { Win32.SetForegroundWindow(gameWindow); } catch { }
                }

                try { form.Hide(); } catch { }
            }
            else if (gameWindow != IntPtr.Zero && Win32.IsWindow(gameWindow))
            {
                try { Win32.SetForegroundWindow(gameWindow); } catch { }
            }
        }
    }
}
