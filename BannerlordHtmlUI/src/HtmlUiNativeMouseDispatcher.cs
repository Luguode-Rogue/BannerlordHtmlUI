using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Dispatches physical mouse input into the page while the overlay owns input.
    ///
    /// Chromium drops real mouse input when its window is not the foreground (the game steals
    /// the foreground back within seconds). This dispatcher polls the physical mouse on the UI
    /// thread while Captured/MouseCaptured is active and, on a click inside the overlay,
    /// programmatically triggers a DOM click at that point via elementFromPoint. Programmatic
    /// clicks do not require window focus, so every existing page handler keeps working.
    /// Keyboard remains with the game (the input blocker hides owned keys from Bannerlord).
    /// </summary>
    internal static class HtmlUiNativeMouseDispatcher
    {
        private const int VkLeftButton = 0x01;
        private const int VkEscape = 0x1B;
        private const int WhMouseLl = 14;
        private const int WmMouseWheel = 0x020A;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelMouseProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        private static readonly object Sync = new object();
        private static HtmlUiHost _host;
        private static Timer _timer;
        private static bool _leftWasDown;
        private static bool _escapeWasDown;
        private static int _lastFocusRecoveryTick;
        private static IntPtr _mouseHook;
        private static LowLevelMouseProc _mouseHookProc;

        public static void EnsureStarted(HtmlUiHost host)
        {
            if (host == null) return;
            lock (Sync)
            {
                if (_timer != null && ReferenceEquals(_host, host)) return;
                Stop();
                _host = host;
                _timer = new Timer { Interval = 50 };
                _timer.Tick += (s, e) => Poll();
                _timer.Start();
                _mouseHookProc = MouseHookCallback;
                _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseHookProc, IntPtr.Zero, 0);
                if (_mouseHook == IntPtr.Zero)
                    HtmlUiLogger.Warn("Native mouse wheel hook installation failed; focus-independent wheel dispatch is unavailable. win32=" + Marshal.GetLastWin32Error());
                HtmlUiLogger.Info("Native input dispatcher started (focus-independent click, wheel and ESC dispatch).");
            }
        }

        public static void Stop()
        {
            lock (Sync)
            {
                if (_timer == null) return;
                try { _timer.Stop(); _timer.Dispose(); } catch { }
                if (_mouseHook != IntPtr.Zero)
                {
                    try { UnhookWindowsHookEx(_mouseHook); } catch { }
                    _mouseHook = IntPtr.Zero;
                }
                _mouseHookProc = null;
                _timer = null;
                _host = null;
                _leftWasDown = false;
                _escapeWasDown = false;
                _lastFocusRecoveryTick = 0;
                HtmlUiLogger.Info("Native mouse dispatcher stopped.");
            }
        }

        private static void Poll()
        {
            var host = _host;
            if (host == null || host.IsDisposed || !host.IsInputCaptured) return;

            var form = host.GetOverlayForm();
            if (form == null || form.IsDisposed || !form.Visible) return;

            var foreground = Win32.GetForegroundWindow();
            bool overlayOwnsForeground = foreground == form.Handle ||
                                         (foreground != IntPtr.Zero && Win32.IsChild(form.Handle, foreground));
            bool gameOwnsForeground = Win32.TryGetGameWindowHandle(form.Handle, out var gameHwnd) &&
                                      foreground == gameHwnd;
            if (!overlayOwnsForeground && !gameOwnsForeground) return;

            bool escapeDown = (GetAsyncKeyState(VkEscape) & 0x8000) != 0;
            bool escapeRising = escapeDown && !_escapeWasDown;
            _escapeWasDown = escapeDown;
            if (escapeRising && host.InputMode == HtmlUiInputMode.Captured)
            {
                HtmlUiKeyboardAndDiagnosticsPatch.TryCloseFromEscape(host, "native-poll");
                return;
            }

            int now = Environment.TickCount;
            if (host.InputMode == HtmlUiInputMode.Captured && unchecked(now - _lastFocusRecoveryTick) >= 250)
            {
                _lastFocusRecoveryTick = now;
                HtmlUiInputControllerPatch.EnsureCapturedFocus(host);
            }

            if (!GetCursorPos(out var cursor)) return;
            var bounds = form.Bounds;
            if (!bounds.Contains(cursor.X, cursor.Y)) return;

            bool leftDown = (GetAsyncKeyState(VkLeftButton) & 0x8000) != 0;
            bool rising = leftDown && !_leftWasDown;
            _leftWasDown = leftDown;
            if (!rising) return;

            // When the WebView really owns focus Chromium receives the physical click itself.
            // Synthesize only for the focus-loss case, otherwise one press fires two DOM clicks.
            if (overlayOwnsForeground && form.ContainsFocus) return;

            // Physical screen coords -> viewport coords inside the page. window.screenX/Y is the
            // window origin in CSS pixels and devicePixelRatio converts physical to CSS, so the
            // element lookup is correct under any DPI scaling.
            string script =
                "(function(px,py){try{const dpr=window.devicePixelRatio||1;" +
                "const cx=px/dpr-(window.screenX||0),cy=py/dpr-(window.screenY||0);" +
                "const el=document.elementFromPoint(cx,cy);" +
                "if(el&&typeof el.click==='function'){el.click();}}catch(e){}})(" + cursor.X + "," + cursor.Y + ")";

            host.TryExecutePageScript(script);
        }

        private static IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && wParam.ToInt32() == WmMouseWheel)
            {
                try
                {
                    var host = _host;
                    var form = host?.GetOverlayForm();
                    if (host != null && host.IsInputCaptured && form != null && !form.IsDisposed && form.Visible)
                    {
                        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                        if (form.Bounds.Contains(data.Point.X, data.Point.Y))
                        {
                            var foreground = Win32.GetForegroundWindow();
                            bool overlayOwnsForeground = foreground == form.Handle ||
                                (foreground != IntPtr.Zero && Win32.IsChild(form.Handle, foreground));
                            bool gameOwnsForeground = Win32.TryGetGameWindowHandle(form.Handle, out var gameHwnd) &&
                                foreground == gameHwnd;
                            if (!overlayOwnsForeground && !gameOwnsForeground)
                                return CallNextHookEx(_mouseHook, code, wParam, lParam);
                            int delta = unchecked((short)((data.MouseData >> 16) & 0xffff));
                            if (delta != 0 && host.TryDispatchPageWheel(data.Point.X, data.Point.Y, delta))
                            {
                                HtmlUiInputTraceLogger.Event("NATIVE_WHEEL_FALLBACK delta=" + delta +
                                                             " x=" + data.Point.X + " y=" + data.Point.Y);
                                return new IntPtr(1);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    HtmlUiLogger.Debug("Native wheel dispatch failed: " + ex.GetBaseException().Message);
                }
            }
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }
    }
}
