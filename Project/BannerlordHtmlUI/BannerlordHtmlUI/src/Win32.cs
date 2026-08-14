using System;
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

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        internal const int GWL_EXSTYLE = -20;
        internal const long WS_EX_NOACTIVATE = 0x08000000L;
        internal const long WS_EX_TOOLWINDOW = 0x00000080L;
        internal const long WS_EX_TRANSPARENT = 0x00000020L;
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

        // WS_EX_TRANSPARENT makes a window transparent to hit-testing: mouse events
        // pass through to the window below it. Applied to the WebView2 child HWND it
        // lets clicks fall through to the game in Passive overlay mode.
        internal static void SetHitTestTransparent(IntPtr hWnd, bool enabled)
        {
            if (hWnd == IntPtr.Zero) return;
            var current = Environment.Is64BitProcess
                ? GetWindowLongPtr64(hWnd, GWL_EXSTYLE).ToInt64()
                : GetWindowLongPtr32(hWnd, GWL_EXSTYLE).ToInt64();

            var next = enabled ? (current | WS_EX_TRANSPARENT) : (current & ~WS_EX_TRANSPARENT);

            var value = new IntPtr(next);
            if (Environment.Is64BitProcess)
                SetWindowLongPtr64(hWnd, GWL_EXSTYLE, value);
            else
                SetWindowLongPtr32(hWnd, GWL_EXSTYLE, value);
        }

        // Apply hit-test transparency to a window AND all of its descendant windows.
        // WebView2 maintains several child HWNDs (browser, renderer hosts), so we must
        // walk the whole tree or clicks will still be swallowed by one of the children.
        internal static void SetHitTestTransparentTree(IntPtr hWnd, bool enabled)
        {
            SetHitTestTransparent(hWnd, enabled);
            EnumChildWindows(hWnd, (child, _) =>
            {
                SetHitTestTransparent(child, enabled);
                return true;
            }, IntPtr.Zero);
        }
    }
}
