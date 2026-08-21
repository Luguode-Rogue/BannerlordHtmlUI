using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Single arbitration layer for the native overlay input state.
    /// The legacy Host implementation performs part of this work asynchronously while
    /// FollowBannerlordWindow also changes visibility/focus. That combination can leave
    /// a hidden WebView holding native mouse capture or repeatedly fight foreground focus.
    /// This patch makes HtmlUiInputMode authoritative.
    /// </summary>
    internal static class HtmlUiInputModePatch
    {
        private const string HarmonyId = "BannerlordHtmlUI.InputMode";
        private static readonly object Sync = new object();
        private static Harmony _harmony;
        private static bool _installed;

        private static readonly FieldInfo FormField = typeof(HtmlUiHost).GetField(
            "_form", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo WebField = typeof(HtmlUiHost).GetField(
            "_web", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo InputModeField = typeof(HtmlUiHost).GetField(
            "_inputMode", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RequestedVisibleField = typeof(HtmlUiHost).GetField(
            "_requestedVisible", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Install(HtmlUiHost host)
        {
            if (host == null) return;

            lock (Sync)
            {
                if (_installed) return;

                if (FormField == null || WebField == null || InputModeField == null || RequestedVisibleField == null)
                    throw new MissingMemberException("HtmlUiHost input fields could not be resolved.");

                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(
                    AccessTools.Method(typeof(HtmlUiHost), nameof(HtmlUiHost.SetInputMode)),
                    prefix: new HarmonyMethod(typeof(HtmlUiInputModePatch), nameof(BeforeSetInputMode)));
                _harmony.Patch(
                    AccessTools.Method(typeof(HtmlUiHost), "FollowBannerlordWindow"),
                    prefix: new HarmonyMethod(typeof(HtmlUiInputModePatch), nameof(BeforeFollowBannerlordWindow)));

                _installed = true;
                HtmlUiLogger.Info("Authoritative HtmlUI input mode patch installed.");
            }
        }

        public static void Uninstall()
        {
            lock (Sync)
            {
                if (!_installed) return;
                try { _harmony?.UnpatchAll(HarmonyId); }
                finally
                {
                    _harmony = null;
                    _installed = false;
                }
            }
        }

        private static bool BeforeSetInputMode(HtmlUiHost __instance, HtmlUiInputMode mode)
        {
            if (__instance == null) return false;

            var previous = GetInputMode(__instance);
            SetInputModeField(__instance, mode);
            SetRequestedVisible(__instance, mode != HtmlUiInputMode.Hidden);

            try
            {
                __instance.State?.Set("framework.inputMode", mode.ToString());
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Failed to publish input mode state: " + ex.GetBaseException().Message);
            }

            var form = FormField.GetValue(__instance) as HtmlUiOverlayForm;
            var web = WebField.GetValue(__instance) as WebView2;

            RunOnUiThread(form, () => ApplyMode(__instance, form, web, mode, previous));
            return false;
        }

        private static bool BeforeFollowBannerlordWindow(HtmlUiHost __instance)
        {
            if (__instance == null) return false;

            var form = FormField.GetValue(__instance) as HtmlUiOverlayForm;
            var web = WebField.GetValue(__instance) as WebView2;
            var mode = GetInputMode(__instance);
            var requestedVisible = GetRequestedVisible(__instance);

            RunOnUiThread(form, () => ApplyWindowTracking(__instance, form, web, mode, requestedVisible));
            return false;
        }

        private static HtmlUiInputMode GetInputMode(HtmlUiHost host)
        {
            return (HtmlUiInputMode)InputModeField.GetValue(host);
        }

        private static bool GetRequestedVisible(HtmlUiHost host)
        {
            return (bool)RequestedVisibleField.GetValue(host);
        }

        private static void SetInputModeField(HtmlUiHost host, HtmlUiInputMode mode)
        {
            InputModeField.SetValue(host, mode);
        }

        private static void SetRequestedVisible(HtmlUiHost host, bool visible)
        {
            RequestedVisibleField.SetValue(host, visible);
        }

        private static void RunOnUiThread(HtmlUiOverlayForm form, Action action)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;

            try
            {
                if (form.InvokeRequired)
                    form.BeginInvoke((Action)(() => SafeInvoke(action)));
                else
                    SafeInvoke(action);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private static void SafeInvoke(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                HtmlUiLogger.Error("Authoritative HtmlUI input mode application failed.", ex);
            }
        }

        private static void ApplyMode(
            HtmlUiHost host,
            HtmlUiOverlayForm form,
            WebView2 web,
            HtmlUiInputMode mode,
            HtmlUiInputMode previous)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;

            if (mode == HtmlUiInputMode.Hidden)
            {
                // Hard release: do not leave WebView2/WinForms holding native capture or focus.
                try { Native.ReleaseCapture(); } catch { }
                try { if (web != null) web.Enabled = false; } catch { }
                try { form.SetPassThrough(true); } catch { }
                try { Native.SetActiveWindow(IntPtr.Zero); } catch { }
                try { form.Hide(); } catch { }

                // The game must regain the foreground after a captured UI closes.
                try
                {
                    var gameWindow = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                    if (gameWindow != IntPtr.Zero && Win32.IsWindow(gameWindow))
                        Win32.SetForegroundWindow(gameWindow);
                }
                catch { }

                HtmlUiLogger.Info("Input mode -> Hidden: native mouse capture released, WebView disabled, overlay hidden.");
                return;
            }

            try { if (web != null) web.Enabled = true; } catch { }

            if (!TryResolveGameWindow(form, out var gameWindow, out var rect))
            {
                // Do not steal focus or invent a window. Preserve the logical mode and wait for tracking.
                try { form.Hide(); } catch { }
                return;
            }

            try { form.SetOwner(gameWindow); } catch { }
            try { form.Bounds = new Rectangle(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top)); } catch { }

            switch (mode)
            {
                case HtmlUiInputMode.Passive:
                    form.SetPassThrough(true);
                    form.Enabled = true;
                    form.Show();
                    Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                    Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                    break;

                case HtmlUiInputMode.Captured:
                    form.SetPassThrough(false);
                    form.Enabled = true;
                    form.Show();
                    Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                    Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                    // Only transition into captured focus when entering captured mode.
                    // Do not re-activate every 100 ms in the window tracker.
                    if (previous != HtmlUiInputMode.Captured)
                    {
                        form.Activate();
                        try { web?.Focus(); } catch { }
                    }
                    break;

                case HtmlUiInputMode.MouseCaptured:
                    form.SetPassThrough(false);
                    form.Enabled = true;
                    form.Show();
                    Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                    Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                    break;
            }
        }

        private static void ApplyWindowTracking(
            HtmlUiHost host,
            HtmlUiOverlayForm form,
            WebView2 web,
            HtmlUiInputMode mode,
            bool requestedVisible)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;

            if (mode == HtmlUiInputMode.Hidden || !requestedVisible)
            {
                try { Native.ReleaseCapture(); } catch { }
                try { if (web != null) web.Enabled = false; } catch { }
                try { form.SetPassThrough(true); } catch { }
                try { form.Hide(); } catch { }
                return;
            }

            if (!TryResolveGameWindow(form, out var gameWindow, out var rect))
            {
                try { form.Hide(); } catch { }
                return;
            }

            if (Win32.IsIconic(gameWindow) || !Win32.IsWindowVisible(gameWindow))
            {
                try { form.Hide(); } catch { }
                return;
            }

            var foreground = Win32.GetForegroundWindow();
            var gameForeground = foreground == gameWindow;
            var overlayForeground = foreground == form.Handle;

            // ALT-TAB / another application owns the foreground: the overlay must not cover it.
            if (!gameForeground && !overlayForeground)
            {
                try { form.Hide(); } catch { }
                return;
            }

            try { form.SetOwner(gameWindow); } catch { }
            try
            {
                form.Bounds = new Rectangle(
                    rect.Left,
                    rect.Top,
                    Math.Max(0, rect.Right - rect.Left),
                    Math.Max(0, rect.Bottom - rect.Top));
            }
            catch { }

            try { web.Enabled = true; } catch { }

            if (mode == HtmlUiInputMode.Passive)
            {
                form.SetPassThrough(true);
                Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
                Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
                return;
            }

            form.SetPassThrough(false);
            Win32.ShowWindow(form.Handle, Win32.SW_SHOWNOACTIVATE);
            Win32.BringWindowAboveOwnerWithoutActivate(form.Handle);
            // Never call SetForegroundWindow/Activate from the periodic tracker.
            // Focus transitions are owned exclusively by ApplyMode.
        }

        private static bool TryResolveGameWindow(
            HtmlUiOverlayForm form,
            out IntPtr hwnd,
            out Win32.RECT rect)
        {
            hwnd = IntPtr.Zero;
            rect = default(Win32.RECT);
            var excluded = form != null && form.IsHandleCreated ? form.Handle : IntPtr.Zero;
            if (!Win32.TryGetGameWindowHandle(excluded, out hwnd)) return false;
            return Win32.GetWindowRect(hwnd, out rect);
        }

        private static class Native
        {
            [DllImport("user32.dll")]
            private static extern bool ReleaseCapture();

            [DllImport("user32.dll")]
            private static extern IntPtr SetActiveWindow(IntPtr hWnd);

            internal static void ReleaseCapture() => Native.ReleaseCapture();
            internal static void SetActiveWindow(IntPtr hWnd) => Native.SetActiveWindow(hWnd);
        }
    }
}
