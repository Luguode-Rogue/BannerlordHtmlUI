using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using TaleWorlds.InputSystem;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiInputTraceLogger
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<InputKey, bool> LastDown = new Dictionary<InputKey, bool>();
        private static string _path;
        private static bool _initialized;
        private static long _lastMouseMoveTick;
        private static IntPtr _lastForeground;
        private static HtmlUiInputMode _lastMode = HtmlUiInputMode.Hidden;
        private static string _lastPage;

        public static void Initialize(string moduleDirectory)
        {
            if (string.IsNullOrWhiteSpace(moduleDirectory)) return;
            try
            {
                Directory.CreateDirectory(moduleDirectory);
                _path = Path.Combine(moduleDirectory, "BannerlordHtmlUI_InputTrace.log");
                lock (Sync) File.WriteAllText(_path, string.Empty);
                LastDown.Clear();
                _lastForeground = IntPtr.Zero;
                _lastMode = HtmlUiInputMode.Hidden;
                _lastPage = null;
                _initialized = true;
                Write("=== INPUT TRACE STARTED ===");
            }
            catch
            {
                _initialized = false;
            }
        }

        public static void Shutdown()
        {
            if (!_initialized) return;
            Write("=== INPUT TRACE STOPPED ===");
            _initialized = false;
        }

        public static void Event(string message)
        {
            Write("EVENT " + message);
        }

        public static void FrameworkState(HtmlUiHost host, string reason)
        {
            if (!_initialized) return;
            try
            {
                var foreground = Win32.GetForegroundWindow();
                var mode = host == null ? HtmlUiInputMode.Hidden : host.InputMode;
                var page = host?.Pages?.CurrentId;
                var visible = host != null && host.IsVisible;
                Write("FRAMEWORK reason=" + reason +
                      " foreground=" + foreground +
                      " visible=" + visible +
                      " inputMode=" + mode +
                      " page=" + (page ?? "<null>") +
                      " captured=" + (host != null && host.IsInputCaptured));
            }
            catch (Exception ex)
            {
                Write("TRACE_ERROR FrameworkState " + ex.GetBaseException().Message);
            }
        }

        public static void KeyMessage(int msg, long wParam, long lParam, string source)
        {
            Write("WINMSG source=" + source + " msg=0x" + msg.ToString("X4") + " vk=0x" + wParam.ToString("X2") + " lParam=0x" + lParam.ToString("X8"));
        }

        public static void MouseMessage(int msg, long wParam, long lParam, string source)
        {
            var x = unchecked((short)(lParam & 0xFFFF));
            var y = unchecked((short)((lParam >> 16) & 0xFFFF));
            Write("MOUSE source=" + source + " msg=0x" + msg.ToString("X4") + " x=" + x + " y=" + y + " wParam=0x" + wParam.ToString("X"));
        }

        public static void BannerlordInput(HtmlUiHost host)
        {
            if (!_initialized) return;

            try
            {
                foreach (var raw in Enum.GetValues(typeof(InputKey)))
                {
                    var key = (InputKey)raw;
                    bool down;
                    try { down = Input.IsKeyDown(key); }
                    catch { continue; }

                    bool previous;
                    if (!LastDown.TryGetValue(key, out previous))
                    {
                        LastDown[key] = down;
                        if (!down) continue;
                        previous = false;
                    }

                    if (down == previous) continue;
                    LastDown[key] = down;
                    var kind = down ? "DOWN" : "UP";
                    Write("GAME_INPUT " + kind + " key=" + key + " type=" + GetInputTypeSafe(key));
                }

                try
                {
                    var dx = (int)Input.GetMouseMoveX();
                    var dy = (int)Input.GetMouseMoveY();
                    if (dx != 0 || dy != 0)
                    {
                        var now = Environment.TickCount;
                        if (now - _lastMouseMoveTick >= 50)
                        {
                            _lastMouseMoveTick = now;
                            Write("GAME_MOUSE_MOVE dx=" + dx + " dy=" + dy);
                        }
                    }
                }
                catch { }

                var foreground = Win32.GetForegroundWindow();
                var mode = host == null ? HtmlUiInputMode.Hidden : host.InputMode;
                var page = host?.Pages?.CurrentId;
                if (foreground != _lastForeground || mode != _lastMode || !string.Equals(page, _lastPage, StringComparison.Ordinal))
                {
                    _lastForeground = foreground;
                    _lastMode = mode;
                    _lastPage = page;
                    var game = IntPtr.Zero;
                    var overlay = IntPtr.Zero;
                    try
                    {
                        var field = host?.GetType().GetField("_form", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        var form = field?.GetValue(host) as HtmlUiOverlayForm;
                        overlay = form != null && form.IsHandleCreated ? form.Handle : IntPtr.Zero;
                        Win32.TryGetGameWindowHandle(overlay, out game);
                    }
                    catch { }
                    Write("STATE foreground=" + foreground + " game=" + game + " overlay=" + overlay + " inputMode=" + mode + " page=" + (page ?? "<null>"));
                }
            }
            catch (Exception ex)
            {
                Write("TRACE_ERROR BannerlordInput " + ex.GetBaseException().Message);
            }
        }

        private static string GetInputTypeSafe(InputKey key)
        {
            try { return Key.GetInputType(key).ToString(); }
            catch { return "Unknown"; }
        }

        private static void Write(string message)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(_path)) return;
            var line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] [" + Thread.CurrentThread.ManagedThreadId + "] " + message;
            try
            {
                lock (Sync) File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
