using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BannerlordHtmlUI
{
    internal static class Win32
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private static readonly object GameWindowSync = new object();
        private static IntPtr _lastKnownGameWindow;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        internal static readonly IntPtr HWND_TOP = IntPtr.Zero;
        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal const uint SWP_NOOWNERZORDER = 0x0200;

        internal const int GWL_EXSTYLE = -20;
        internal const long WS_EX_NOACTIVATE = 0x08000000L;
        internal const long WS_EX_TOOLWINDOW = 0x00000080L;
        internal const int SW_SHOWNOACTIVATE = 4;

        internal static void SetNoActivate(IntPtr hWnd, bool enabled)
        {
            if (hWnd == IntPtr.Zero) return;

            var current = Environment.Is64BitProcess
                ? GetWindowLongPtr64(hWnd, GWL_EXSTYLE).ToInt64()
                : GetWindowLongPtr32(hWnd, GWL_EXSTYLE).ToInt64();

            var next = current | WS_EX_TOOLWINDOW;
            if (enabled) next |= WS_EX_NOACTIVATE;
            else next &= ~WS_EX_NOACTIVATE;

            var value = new IntPtr(next);
            if (Environment.Is64BitProcess)
                SetWindowLongPtr64(hWnd, GWL_EXSTYLE, value);
            else
                SetWindowLongPtr32(hWnd, GWL_EXSTYLE, value);
        }

        internal static bool TryGetGameWindowHandle(IntPtr excludedWindow, out IntPtr handle)
        {
            handle = IntPtr.Zero;
            var processId = unchecked((uint)Process.GetCurrentProcess().Id);

            lock (GameWindowSync)
            {
                if (IsUsableGameWindow(_lastKnownGameWindow, processId, excludedWindow))
                {
                    handle = _lastKnownGameWindow;
                    return true;
                }
            }

            try
            {
                var main = Process.GetCurrentProcess().MainWindowHandle;
                if (IsUsableGameWindow(main, processId, excludedWindow))
                    return RememberGameWindow(main, out handle);
            }
            catch { }

            try
            {
                var foreground = GetForegroundWindow();
                if (IsUsableGameWindow(foreground, processId, excludedWindow))
                    return RememberGameWindow(foreground, out handle);
            }
            catch { }

            try
            {
                IntPtr candidate = IntPtr.Zero;
                EnumWindows((hWnd, _) =>
                {
                    if (IsUsableGameWindow(hWnd, processId, excludedWindow))
                    {
                        candidate = hWnd;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (candidate != IntPtr.Zero)
                    return RememberGameWindow(candidate, out handle);
            }
            catch { }

            return false;
        }

        private static bool RememberGameWindow(IntPtr handle, out IntPtr result)
        {
            lock (GameWindowSync) _lastKnownGameWindow = handle;
            result = handle;
            return true;
        }

        private static bool IsUsableGameWindow(IntPtr hWnd, uint processId, IntPtr excludedWindow)
        {
            if (hWnd == IntPtr.Zero || hWnd == excludedWindow || !IsWindow(hWnd) || !IsWindowVisible(hWnd))
                return false;
            if (IsIconic(hWnd)) return false;

            GetWindowThreadProcessId(hWnd, out var ownerProcessId);
            return ownerProcessId == processId;
        }

        internal static void BringWindowAboveOwnerWithoutActivate(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;

            SetWindowPos(
                hWnd,
                HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }
}
