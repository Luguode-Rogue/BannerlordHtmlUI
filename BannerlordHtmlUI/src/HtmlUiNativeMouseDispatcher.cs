using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Dispatches physical mouse clicks into the page while the overlay owns input.
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

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        private static readonly object Sync = new object();
        private static HtmlUiHost _host;
        private static Timer _timer;
        private static bool _leftWasDown;

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
                HtmlUiLogger.Info("Native mouse dispatcher started (focus-independent click dispatch).");
            }
        }

        public static void Stop()
        {
            lock (Sync)
            {
                if (_timer == null) return;
                try { _timer.Stop(); _timer.Dispose(); } catch { }
                _timer = null;
                _host = null;
                _leftWasDown = false;
                HtmlUiLogger.Info("Native mouse dispatcher stopped.");
            }
        }

        private static void Poll()
        {
            var host = _host;
            if (host == null || host.IsDisposed || !host.IsInputCaptured) return;

            var form = host.GetOverlayForm();
            if (form == null || form.IsDisposed || !form.Visible) return;

            if (!GetCursorPos(out var cursor)) return;
            var bounds = form.Bounds;
            if (!bounds.Contains(cursor.X, cursor.Y)) return;

            bool leftDown = (GetAsyncKeyState(VkLeftButton) & 0x8000) != 0;
            bool rising = leftDown && !_leftWasDown;
            _leftWasDown = leftDown;
            if (!rising) return;

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
    }
}
