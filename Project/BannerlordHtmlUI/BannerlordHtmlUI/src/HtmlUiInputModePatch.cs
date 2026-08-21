using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Microsoft.Web.WebView2.WinForms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiInputModePatch
    {
        private const string HarmonyId = "BannerlordHtmlUI.InputMode";
        private static readonly object Sync = new object();
        private static Harmony _harmony;
        private static bool _installed;

        private static readonly FieldInfo FormField = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo WebField = typeof(HtmlUiHost).GetField("_web", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo InputModeField = typeof(HtmlUiHost).GetField("_inputMode", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RequestedVisibleField = typeof(HtmlUiHost).GetField("_requestedVisible", BindingFlags.Instance | BindingFlags.NonPublic);

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
                finally { _harmony = null; _installed = false; }
            }
        }

        private static bool BeforeSetInputMode(HtmlUiHost __instance, HtmlUiInputMode mode)
        {
            if (__instance == null) return false;

            var previous = GetInputMode(__instance);
            SetInputModeField(__instance, mode);
            SetRequestedVisible(__instance, mode != HtmlUiInputMode.Hidden);

            try { __instance.State?.Set("framework.inputMode", mode.ToString()); }
            catch (Exception ex) { HtmlUiLogger.Debug("Failed to publish input mode state: " + ex.GetBaseException().Message); }

            var form = FormField.GetValue(__instance) as HtmlUiOverlayForm;
            var web = WebField.GetValue(__instance) as WebView2;
            HtmlUiLogger.Info("Input mode request: " + previous + " -> " + mode + ", form=" + DescribeForm(form) + ", webEnabled=" + (web == null ? "<null>" : web.Enabled.ToString()));
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

        private static HtmlUiInputMode GetInputMode(HtmlUiHost host) => (HtmlUiInputMode)InputModeField.GetValue(host);
        private static bool GetRequestedVisible(HtmlUiHost host) => (bool)RequestedVisibleField.GetValue(host);
        private static void SetInputModeField(HtmlUiHost host, HtmlUiInputMode mode) => InputModeField.SetValue(host, mode);
        private static void SetRequestedVisible(HtmlUiHost host, bool visible) => RequestedVisibleField.SetValue(host, visible);

        private static void RunOnUiThread(HtmlUiOverlayForm form, Action action)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated)
            {
                HtmlUiLogger.Warn("Input mode application skipped: overlay form unavailable.");
                return;
            }
            try
            {
                if (form.InvokeRequired) form.BeginInvoke((Action)(() => SafeInvoke(action)));
                else SafeInvoke(action);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private static void SafeInvoke(Action action)
        {
            try { action(); }
            catch (Exception ex) { HtmlUiLogger.Error("Authoritative HtmlUI input mode application failed.", ex); }
        }

        private static void ApplyMode(HtmlUiHost host, HtmlUiOverlayForm form, WebView2 web, HtmlUiInputMode mode, HtmlUiInputMode previous)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated)
            {
                HtmlUiLogger.Warn("Input mode application failed: overlay form unavailable.");
                return;
            }

            if (mode == HtmlUiInputMode.Hidden)
            {
                try { Native.ReleaseMouseCapture(); } catch { }
                try { if (web != null) web.Enabled = false; } catch { }
                try { form.SetPassThrough(true); } catch { }
                try { form.Hide(); } catch { }
                try
                {
                    var hiddenGameWindow = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                    if (hiddenGameWindow != IntPtr.Zero && Win32.IsWindow(hiddenGameWindow))
                        Win32.SetForegroundWindow(hiddenGameWindow);
                }
                catch { }
                HtmlUiLogger.Info("Input mode applied: Hidden; hwnd=" + form.Handle + ", visible=" + form.Visible + ", webEnabled=" + (web != null && web.Enabled));
                return;
            }

            try { if (web != null) web.Enabled = true; } catch { }
            if (!TryResolveGameWindow(form, out var resolvedGameWindow, out var rect))
            {
                try { form.Hide(); } catch { }
                HtmlUiLogger.Warn("Input mode application deferred: Bannerlord window could not be resolved. mode=" + mode);
                return;
            }

            try { form.SetOwner(resolvedGameWindow); } catch { }
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
                    if (previous != HtmlUiInputMode.Captured)
                    {
                        Win32.SetForegroundWindow(form.Handle);
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

            HtmlUiLogger.Info("Input mode applied: mode=" + mode
                + ", previous=" + previous
                + ", overlayHwnd=" + form.Handle
                + ", gameHwnd=" + resolvedGameWindow
                + ", visible=" + form.Visible
                + ", active=" + (Win32.GetForegroundWindow() == form.Handle)
                + ", webEnabled=" + (web != null && web.Enabled));
        }

        private static void ApplyWindowTracking(HtmlUiHost host, HtmlUiOverlayForm form, WebView2 web, HtmlUiInputMode mode, bool requestedVisible)
        {
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;

            if (mode == HtmlUiInputMode.Hidden || !requestedVisible)
            {
                try { Native.ReleaseMouseCapture(); } catch { }
                try { if (web != null) web.Enabled = false; } catch { }
                try { form.SetPassThrough(true); } catch { }
                try { form.Hide(); } catch { }
                return;
            }

            if (!TryResolveGameWindow(form, out var trackedGameWindow, out var rect))
            {
                try { form.Hide(); } catch { }
                return;
            }
            if (Win32.IsIconic(trackedGameWindow) || !Win32.IsWindowVisible(trackedGameWindow))
            {
                try { form.Hide(); } catch { }
                return;
            }

            var foreground = Win32.GetForegroundWindow();
            var gameForeground = foreground == trackedGameWindow;
            var overlayForeground = foreground == form.Handle;
            if (!gameForeground && !overlayForeground)
            {
                try { form.Hide(); } catch { }
                return;
            }

            try { form.SetOwner(trackedGameWindow); } catch { }
            try
            {
                form.Bounds = new Rectangle(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
            }
            catch { }
            try { if (web != null) web.Enabled = true; } catch { }

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
        }

        private static bool TryResolveGameWindow(HtmlUiOverlayForm form, out IntPtr hwnd, out Win32.RECT rect)
        {
            hwnd = IntPtr.Zero;
            rect = default(Win32.RECT);
            var excluded = form != null && form.IsHandleCreated ? form.Handle : IntPtr.Zero;
            if (!Win32.TryGetGameWindowHandle(excluded, out hwnd)) return false;
            return Win32.GetWindowRect(hwnd, out rect);
        }

        private static string DescribeForm(HtmlUiOverlayForm form)
        {
            if (form == null) return "<null>";
            return "hwnd=" + (form.IsHandleCreated ? form.Handle.ToString() : "<none>")
                + ", visible=" + form.Visible
                + ", enabled=" + form.Enabled;
        }

        private static class Native
        {
            [DllImport("user32.dll", EntryPoint = "ReleaseCapture")]
            private static extern bool ReleaseCaptureNative();

            internal static void ReleaseMouseCapture() => ReleaseCaptureNative();
        }
    }
}
